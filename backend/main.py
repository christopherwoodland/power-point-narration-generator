"""
FastAPI backend for PowerPoint Narration Generator.

POST /api/parse   — parse Word doc, return slide list (no TTS yet)
POST /api/process — full pipeline: parse Word → TTS → embed audio → return PPTX
GET  /            — serve the wizard UI
"""
import io
import json
import tempfile
import os
from pathlib import Path

from fastapi import FastAPI, File, UploadFile, Form, HTTPException
from fastapi.responses import StreamingResponse, HTMLResponse, JSONResponse
from fastapi.staticfiles import StaticFiles
from fastapi.middleware.cors import CORSMiddleware

from word_parser import extract_slides
from tts_client import synthesize_to_mp3
from pptx_builder import embed_audio_into_pptx
from translator import translate_for_voice
from pptx import Presentation

app = FastAPI(title="PowerPoint Narration Generator")

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)

# Serve static frontend files
_frontend = Path(__file__).parent.parent / "frontend"
if _frontend.exists():
    app.mount("/static", StaticFiles(directory=str(_frontend)), name="static")


@app.get("/", response_class=HTMLResponse)
async def index():
    index_file = _frontend / "index.html"
    if index_file.exists():
        return HTMLResponse(index_file.read_text(encoding="utf-8"))
    return HTMLResponse("<h1>Frontend not found</h1>", status_code=404)


@app.post("/api/parse")
async def parse_doc(script: UploadFile = File(...), pptx: UploadFile = File(...)):
    """
    Step 1 of the wizard: parse the Word doc and return slide list alongside
    the actual slide count from the PPTX so the UI can show any mismatches.
    """
    docx_bytes = await script.read()
    pptx_bytes = await pptx.read()

    slides = extract_slides(docx_bytes)

    prs = Presentation(io.BytesIO(pptx_bytes))
    pptx_slide_count = len(prs.slides)

    return JSONResponse({
        "slides": slides,
        "pptx_slide_count": pptx_slide_count,
        "word_slide_count": len(slides),
    })


@app.post("/api/process")
async def process(
    script: UploadFile = File(...),
    pptx: UploadFile = File(...),
    voice: str = Form("en-US-JennyNeural"),
    slide_mapping: str = Form("{}"),  # JSON: {"0": 0, "1": 1, ...} word→pptx index
):
    """
    Full pipeline:
      1. Parse Word doc into slide blocks.
      2. Synthesize each block to MP3 via Azure TTS.
      3. Embed audio into PPTX slides per the mapping.
      4. Return the modified PPTX as a download.
    """
    docx_bytes = await script.read()
    pptx_bytes = await pptx.read()

    # Parse word doc
    slides = extract_slides(docx_bytes)

    prs = Presentation(io.BytesIO(pptx_bytes))
    pptx_slide_count = len(prs.slides)

    # Parse mapping: word slide index → pptx slide index
    try:
        mapping: dict[str, int] = json.loads(slide_mapping)
    except Exception:
        mapping = {}

    # Default mapping: pair by position up to min(word, pptx)
    if not mapping:
        for i in range(min(len(slides), pptx_slide_count)):
            mapping[str(i)] = i

    # Build per-pptx-slide audio list
    slide_audio: list[bytes | None] = [None] * pptx_slide_count

    for word_idx_str, pptx_idx in mapping.items():
        word_idx = int(word_idx_str)
        if word_idx >= len(slides):
            continue
        if pptx_idx >= pptx_slide_count:
            continue

        text = slides[word_idx]["text"]
        if not text.strip():
            continue

        print(f"[Process] Synthesizing slide {word_idx + 1} → PPTX slide {pptx_idx + 1} ({len(text)} chars)", flush=True)
        try:
            translated = translate_for_voice(text, voice=voice)
            audio = synthesize_to_mp3(translated, voice=voice)
            slide_audio[pptx_idx] = audio if audio else None
        except Exception as exc:
            raise HTTPException(
                status_code=502,
                detail=f"TTS failed for slide {word_idx + 1}: {exc}",
            )

    # Embed audio
    print(f"[Process] Embedding audio into PPTX ({sum(1 for a in slide_audio if a)} slides with audio)...", flush=True)
    result_bytes = embed_audio_into_pptx(pptx_bytes, slide_audio)
    print(f"[Process] Done — {len(result_bytes):,} bytes output", flush=True)

    return StreamingResponse(
        io.BytesIO(result_bytes),
        media_type="application/vnd.openxmlformats-officedocument.presentationml.presentation",
        headers={"Content-Disposition": "attachment; filename=narrated_presentation.pptx"},
    )
