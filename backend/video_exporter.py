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
_AUDIO_DELAY_MS = 200        # ms silence before audio begins (small natural pause)


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


def _export_slides_as_png_libreoffice(pptx_path: str, out_dir: str) -> list[str]:
    """
    Export every slide as a PNG using LibreOffice headless + pdftoppm.
    Works on Linux/macOS — no PowerPoint required.

    Flow:
      1. libreoffice --headless --convert-to pdf  →  input.pdf
      2. pdftoppm -png -rx 192 -ry 192            →  slide-NNNN.png  (1920×1080 at a 10:1 ratio)
    """
    import platform
    pptx_abs = str(Path(pptx_path).resolve())
    pdf_path  = str(Path(out_dir) / "input.pdf")

    # Step 1: PPTX → PDF
    # Use a writable temp dir for LibreOffice's user profile so it works when
    # HOME is /nonexistent (typical for non-root container users).
    lo_profile_dir = str(Path(out_dir) / "lo_profile")
    Path(lo_profile_dir).mkdir(parents=True, exist_ok=True)
    lo_profile_url = f"file://{lo_profile_dir}"
    lo_env = {**os.environ, "HOME": out_dir}
    lo_bin = "libreoffice"
    result = subprocess.run(
        [
            lo_bin,
            "--headless",
            f"-env:UserInstallation={lo_profile_url}",
            "--convert-to", "pdf",
            "--outdir", out_dir,
            pptx_abs,
        ],
        capture_output=True,
        timeout=120,
        env=lo_env,
    )
    if result.returncode != 0 or not Path(pdf_path).exists():
        raise RuntimeError(
            f"LibreOffice PDF conversion failed: "
            f"{result.stderr.decode(errors='replace')[:400]}"
        )

    # Step 2: PDF → PNG per-page  (192 dpi ≈ 1600px wide for a 16:9 slide — close enough)
    prefix = str(Path(out_dir) / "slide")
    result = subprocess.run(
        ["pdftoppm", "-png", "-rx", "192", "-ry", "192", "-cropbox", pdf_path, prefix],
        capture_output=True,
        timeout=300,
    )
    if result.returncode != 0:
        raise RuntimeError(
            f"pdftoppm failed: {result.stderr.decode(errors='replace')[:400]}"
        )

    # pdftoppm names files  slide-1.png, slide-2.png … or slide-01.png etc.
    png_paths = sorted(
        Path(out_dir).glob("slide-*.png"),
        key=lambda p: int("".join(filter(str.isdigit, p.stem)) or "0"),
    )
    if not png_paths:
        raise RuntimeError("pdftoppm produced no PNG files.")
    return [str(p) for p in png_paths]


def _export_slides_as_png(pptx_path: str, out_dir: str, total: int) -> list[str]:
    """
    Export every slide as a PNG.

    On Windows with PowerPoint installed: uses COM automation for pixel-perfect output.
    On Linux/macOS (or when win32com is unavailable): uses LibreOffice headless + pdftoppm.

    Returns a list of absolute PNG paths ordered by slide number.
    """
    import sys
    if sys.platform == "win32":
        try:
            import win32com.client  # type: ignore  # noqa: F401
            return _export_slides_as_png_win32(pptx_path, out_dir)
        except ImportError:
            pass  # fall through to LibreOffice

    return _export_slides_as_png_libreoffice(pptx_path, out_dir)


def _export_slides_as_png_win32(pptx_path: str, out_dir: str) -> list[str]:
    """
    Use PowerPoint COM to export every slide as a PNG (Windows only).
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
    """Encode a single PNG + optional audio into an MP4 clip.

    All clips share the same codec profile (H.264 yuv420p 25fps / AAC 44100 stereo)
    so the concat demuxer can stream-copy without re-encoding.
    Slides without audio get a silent AAC track for consistent stream layout.
    """
    vf = (
        f"scale={_SLIDE_W_PX}:{_SLIDE_H_PX}:force_original_aspect_ratio=decrease,"
        f"pad={_SLIDE_W_PX}:{_SLIDE_H_PX}:(ow-iw)/2:(oh-ih)/2,fps=25"
    )
    common_audio = ["-c:a", "aac", "-b:a", "128k", "-ar", "44100", "-ac", "2"]

    # ultrafast + stillimage tune + single thread: minimises RAM usage on constrained
    # containers (avoids SIGKILL from OOM with 1920x1080 frames).
    video_encode = ["-c:v", "libx264", "-preset", "ultrafast", "-tune", "stillimage",
                    "-crf", "23", "-pix_fmt", "yuv420p", "-threads", "1"]

    if mp3_path:
        # Delay audio so the slide is visible for a beat before narration starts.
        # apad + -shortest: clip ends when delayed audio ends.
        cmd = [
            "ffmpeg", "-y",
            "-loop", "1", "-framerate", "25", "-i", png_path,
            "-i", mp3_path,
            "-vf", vf,
            "-af", f"adelay={_AUDIO_DELAY_MS}|{_AUDIO_DELAY_MS},aresample=44100",
            "-shortest",
            *video_encode,
            *common_audio,
            out_path,
        ]
    else:
        # No audio — synthesise a silent stereo track so concat has a consistent
        # audio stream across all clips (avoids channel-layout mismatch errors).
        cmd = [
            "ffmpeg", "-y",
            "-loop", "1", "-framerate", "25", "-i", png_path,
            "-f", "lavfi", "-i", "anullsrc=r=44100:cl=stereo",
            "-vf", vf,
            "-t", str(duration),
            *video_encode,
            *common_audio,
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
