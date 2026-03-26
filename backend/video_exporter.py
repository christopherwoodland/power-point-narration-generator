"""
PPTX → MP4 video exporter.

For each slide in the PPTX:
  1. Export the slide as a PNG using PowerPoint COM automation.
  2. Extract the embedded MP3 audio (same ZIP-walk logic as quality_checker).
  3. Build a per-slide MP4 clip with FFmpeg (loop PNG + audio, or a fixed 3-second
     hold for slides without audio).
  4. Concatenate all clips into a final MP4 with FFmpeg.

Requires:
  - pywin32  (pip install pywin32)  — PowerPoint must be installed on the machine.
  - ffmpeg / ffprobe on PATH.
"""
import io
import os
import shutil
import subprocess
import tempfile
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

_REL_NS = "http://schemas.openxmlformats.org/package/2006/relationships"
_AUDIO_REL_TYPE = (
    "http://schemas.openxmlformats.org/officeDocument/2006/relationships/audio"
)
_SLIDE_W_PX = 1920
_SLIDE_H_PX = 1080
_FALLBACK_DURATION_S = 3.0  # seconds to show a slide that has no audio
_AUDIO_DELAY_MS = 400        # ms to show slide before audio begins


def _resolve_rel_target(rel_target: str) -> str:
    """Convert a relationship Target relative to ppt/slides/ to a ZIP member path."""
    if rel_target.startswith("/"):
        return rel_target.lstrip("/")
    parts = f"ppt/slides/{rel_target}".replace("\\", "/").split("/")
    resolved: list[str] = []
    for p in parts:
        if p == "..":
            if resolved:
                resolved.pop()
        elif p not in ("", "."):
            resolved.append(p)
    return "/".join(resolved)


def _extract_audio_per_slide(pptx_bytes: bytes) -> dict[int, bytes | None]:
    """Return {1-based slide index: mp3 bytes or None}."""
    result: dict[int, bytes | None] = {}
    with zipfile.ZipFile(io.BytesIO(pptx_bytes)) as z:
        members = set(z.namelist())
        slide_num = 1
        while f"ppt/slides/slide{slide_num}.xml" in members:
            audio: bytes | None = None
            rels_path = f"ppt/slides/_rels/slide{slide_num}.xml.rels"
            if rels_path in members:
                try:
                    root = ET.fromstring(z.read(rels_path))
                    for rel in root.findall(f"{{{_REL_NS}}}Relationship"):
                        if rel.get("Type", "") == _AUDIO_REL_TYPE:
                            target = rel.get("Target", "")
                            media_path = _resolve_rel_target(target)
                            if media_path in members:
                                audio = z.read(media_path)
                            break
                except ET.ParseError:
                    pass
            result[slide_num] = audio
            slide_num += 1
    return result


def _export_slides_as_png(pptx_path: str, out_dir: str, total: int) -> list[str]:
    """
    Use PowerPoint COM to export every slide as a PNG.
    Returns a list of absolute PNG paths ordered by slide number.
    """
    import win32com.client  # type: ignore

    pptx_abs = str(Path(pptx_path).resolve())
    ppt_app = win32com.client.Dispatch("PowerPoint.Application")
    try:
        prs = ppt_app.Presentations.Open(pptx_abs, ReadOnly=True, WithWindow=False)
        try:
            paths: list[str] = []
            for i in range(1, prs.Slides.Count + 1):
                out_path = str(Path(out_dir) / f"slide{i:04d}.png")
                prs.Slides(i).Export(out_path, "PNG", _SLIDE_W_PX, _SLIDE_H_PX)
                paths.append(out_path)
            return paths
        finally:
            prs.Close()
    finally:
        ppt_app.Quit()


def _audio_duration(mp3_path: str) -> float:
    """Return the duration of an audio file in seconds using ffprobe."""
    cmd = [
        "ffprobe", "-v", "error",
        "-show_entries", "format=duration",
        "-of", "default=noprint_wrappers=1:nokey=1",
        mp3_path,
    ]
    out = subprocess.check_output(cmd, stderr=subprocess.DEVNULL)
    return float(out.strip())


def _make_slide_clip(
    png_path: str,
    mp3_path: str | None,
    duration: float,
    out_path: str,
) -> None:
    """Encode a single PNG + optional audio into an MP4 clip."""
    cmd = [
        "ffmpeg", "-y",
        "-loop", "1",
        "-framerate", "1",
        "-i", png_path,
    ]
    if mp3_path:
        cmd += [
            "-i", mp3_path,
            "-af", f"adelay={_AUDIO_DELAY_MS}|{_AUDIO_DELAY_MS}",
            "-shortest",
        ]
    else:
        cmd += ["-t", str(duration)]

    cmd += [
        "-vf", f"scale={_SLIDE_W_PX}:{_SLIDE_H_PX}:force_original_aspect_ratio=decrease,"
               f"pad={_SLIDE_W_PX}:{_SLIDE_H_PX}:(ow-iw)/2:(oh-ih)/2",
        "-c:v", "libx264",
        "-preset", "fast",
        "-crf", "23",
        "-pix_fmt", "yuv420p",
        "-c:a", "aac",
        "-b:a", "128k",
        out_path,
    ]
    subprocess.run(cmd, check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


def _concat_clips(clip_paths: list[str], out_path: str) -> None:
    """Concatenate per-slide MP4 clips into a single MP4 using FFmpeg concat demuxer."""
    concat_list = out_path + ".txt"
    with open(concat_list, "w", encoding="utf-8") as f:
        for cp in clip_paths:
            escaped = cp.replace("'", "\\'").replace("\\", "/")
            f.write(f"file '{escaped}'\n")
    try:
        cmd = [
            "ffmpeg", "-y",
            "-f", "concat", "-safe", "0",
            "-i", concat_list,
            "-c", "copy",
            out_path,
        ]
        subprocess.run(cmd, check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    finally:
        Path(concat_list).unlink(missing_ok=True)


def export_video(pptx_bytes: bytes, on_progress=None) -> bytes:
    """
    Convert a narrated PPTX to an MP4 slideshow.

    Args:
        pptx_bytes: raw bytes of the input PPTX.
        on_progress: optional callable(current_slide, total, phase_str).

    Returns:
        MP4 bytes.
    """
    def _progress(n: int, total: int, phase: str):
        if on_progress:
            try:
                on_progress(n, total, phase)
            except Exception:
                pass

    tmp = tempfile.mkdtemp(prefix="pptx_video_")
    try:
        # ── Write PPTX to disk (COM needs a file path) ─────────────────────
        pptx_path = os.path.join(tmp, "input.pptx")
        with open(pptx_path, "wb") as fh:
            fh.write(pptx_bytes)

        # ── Extract per-slide audio from ZIP ────────────────────────────────
        audio_map = _extract_audio_per_slide(pptx_bytes)
        total = len(audio_map)
        print(f"[Video] {total} slides, "
              f"{sum(1 for a in audio_map.values() if a)} with audio", flush=True)

        if total == 0:
            raise ValueError("PPTX contains no slides.")

        # ── Export slides as PNGs via PowerPoint COM ─────────────────────────
        _progress(0, total, "export")
        png_dir = os.path.join(tmp, "slides")
        os.makedirs(png_dir)
        print("[Video] Exporting slides as PNG via PowerPoint…", flush=True)
        png_paths = _export_slides_as_png(pptx_path, png_dir, total)

        # ── Build per-slide MP4 clips ────────────────────────────────────────
        clip_paths: list[str] = []
        for i, png_path in enumerate(png_paths):
            slide_num = i + 1
            _progress(slide_num, total, "encode")
            audio_bytes = audio_map.get(slide_num)

            mp3_path: str | None = None
            duration = _FALLBACK_DURATION_S

            if audio_bytes:
                mp3_path = os.path.join(tmp, f"audio_{slide_num:04d}.mp3")
                with open(mp3_path, "wb") as fh:
                    fh.write(audio_bytes)
                try:
                    duration = _audio_duration(mp3_path)
                except Exception:
                    duration = _FALLBACK_DURATION_S

            clip_path = os.path.join(tmp, f"clip_{slide_num:04d}.mp4")
            print(f"[Video] Encoding slide {slide_num}/{total} "
                  f"({duration:.1f}s)…", flush=True)
            _make_slide_clip(png_path, mp3_path, duration, clip_path)
            clip_paths.append(clip_path)

        # ── Concatenate all clips ────────────────────────────────────────────
        _progress(total, total, "concat")
        output_path = os.path.join(tmp, "output.mp4")
        print("[Video] Concatenating clips…", flush=True)
        _concat_clips(clip_paths, output_path)

        with open(output_path, "rb") as fh:
            mp4_bytes = fh.read()

        print(f"[Video] Done — {len(mp4_bytes):,} bytes", flush=True)
        return mp4_bytes

    finally:
        shutil.rmtree(tmp, ignore_errors=True)
