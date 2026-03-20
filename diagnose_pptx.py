"""
Diagnostic: inspect a PPTX to verify audio embedding and autoplay.
Usage: python diagnose_pptx.py [path/to/file.pptx]
"""
import sys
import zipfile
import os

pptx_path = sys.argv[1] if len(sys.argv) > 1 else "test_output.pptx"

if not os.path.exists(pptx_path):
    print(f"File not found: {pptx_path}")
    sys.exit(1)

print(f"Inspecting: {pptx_path}  ({os.path.getsize(pptx_path):,} bytes on disk)\n")

with zipfile.ZipFile(pptx_path) as z:
    names = z.namelist()
    audio_files = sorted(n for n in names if "media" in n and n.endswith(".mp3"))
    rels_files  = sorted(n for n in names if "slides/_rels" in n)
    slide_files = sorted(n for n in names if n.startswith("ppt/slides/slide") and n.endswith(".xml"))

    # ── Audio files ──────────────────────────────────────────────────────────
    print(f"=== Embedded audio files ({len(audio_files)}) ===")
    if not audio_files:
        print("  NONE — no MP3 files found!  TTS output was not embedded.")
    for a in audio_files:
        size = z.getinfo(a).file_size
        status = "OK" if size > 1000 else f"PROBLEM: only {size} bytes (empty/bad MP3)"
        print(f"  {a}  =>  {size:,} bytes  [{status}]")

    # ── Slide .rels ───────────────────────────────────────────────────────────
    print(f"\n=== Slide relationship files ({len(rels_files)}) ===")
    for r in rels_files:
        content = z.read(r).decode("utf-8", errors="replace")
        has_audio = "audio" in content.lower() or "mp3" in content.lower()
        print(f"  {r}: has_audio_rel={has_audio}")
        if has_audio:
            for part in content.split("<"):
                if "audio" in part.lower() or "mp3" in part.lower():
                    print(f"    <{part.strip()}")

    # ── Slide XML ─────────────────────────────────────────────────────────────
    print(f"\n=== Slide XML autoplay check ({len(slide_files)} slides) ===")
    for s in slide_files:
        content = z.read(s).decode("utf-8", errors="replace")
        has_timing = "p:timing" in content
        has_pic    = "p:pic" in content
        has_cNvPr  = "cNvPr" in content
        print(f"  {s}: timing={has_timing}, pic={has_pic}, cNvPr={has_cNvPr}")
        if has_timing:
            # show the timing block snippet
            start = content.find("<p:timing")
            end   = content.find("</p:timing>", start) + len("</p:timing>")
            snippet = content[start:end][:300]
            print(f"    timing snippet: {snippet}...")
