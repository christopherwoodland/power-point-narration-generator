"""
Quality Check agent for narrated PPTX.

For each slide that has embedded audio:
  1. Extract the MP3 from the PPTX zip.
  2. Transcribe it via Azure STT.
  3. Ask GPT-5.2 to compare the original script text with the transcription
     and return a structured confidence score + issues list.

Uses DefaultAzureCredential — no API key required.
Azure OpenAI endpoint: https://bhs-development-public-foundry-r.cognitiveservices.azure.com
Deployment: gpt-5.2
"""
import json
import zipfile
import io
import os
import re
import xml.etree.ElementTree as ET
import httpx
from azure.identity import DefaultAzureCredential

from stt_client import transcribe_mp3
from translator import _locale_from_voice

# OPC / OOXML namespaces
_REL_NS  = "http://schemas.openxmlformats.org/package/2006/relationships"
_A_NS    = "http://schemas.openxmlformats.org/drawingml/2006/main"
_P_NS    = "http://schemas.openxmlformats.org/presentationml/2006/main"
# Only the explicit audio link relationship (not the duplicate media rel)
_AUDIO_REL_TYPE = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/audio"


def _resolve_rel_target(rel_target: str, slide_num: int) -> str:
    """
    Convert a relationship Target (relative to ppt/slides/) into a ZIP member path.
    e.g. '../media/audio_slide1.mp3' → 'ppt/media/audio_slide1.mp3'
    """
    if rel_target.startswith("/"):
        return rel_target.lstrip("/")
    # Relative to ppt/slides/ — strip leading ".."
    parts = f"ppt/slides/{rel_target}".replace("\\", "/").split("/")
    resolved: list[str] = []
    for p in parts:
        if p == "..":
            if resolved:
                resolved.pop()
        elif p not in ("", "."):
            resolved.append(p)
    return "/".join(resolved)


def _get_slide_title(slide_xml: bytes) -> str:
    """Extract the title text from a slide XML byte string."""
    try:
        root = ET.fromstring(slide_xml)
        # Find the title placeholder (type="title" or idx="0")
        for sp in root.iter(f"{{{_P_NS}}}sp"):
            nvSpPr = sp.find(f".//{{{_P_NS}}}nvSpPr")
            if nvSpPr is None:
                continue
            ph = nvSpPr.find(f".//{{{_P_NS}}}ph")
            if ph is None:
                continue
            ph_type = ph.get("type", "")
            ph_idx  = ph.get("idx", "")
            if ph_type == "title" or (ph_type == "" and ph_idx in ("", "0")):
                texts = [t.text or "" for t in sp.iter(f"{{{_A_NS}}}t")]
                title = "".join(texts).strip()
                if title:
                    return title
    except ET.ParseError:
        pass
    return ""


def _extract_slides_from_pptx(pptx_bytes: bytes) -> dict[int, dict]:
    """
    Open the PPTX as a ZIP and, for each slide:
      - Read ppt/slides/_rels/slideN.xml.rels to find the audio file
      - Read ppt/slides/slideN.xml to get the slide title

    Returns {1-based slide number: {"title": str, "audio": bytes|None}}
    """
    slides: dict[int, dict] = {}

    with zipfile.ZipFile(io.BytesIO(pptx_bytes)) as z:
        members = set(z.namelist())

        # Enumerate slides in order
        slide_num = 1
        while True:
            slide_path = f"ppt/slides/slide{slide_num}.xml"
            if slide_path not in members:
                break

            slide_xml = z.read(slide_path)
            title = _get_slide_title(slide_xml)
            if not title:
                title = f"Slide {slide_num}"

            audio: bytes | None = None

            rels_path = f"ppt/slides/_rels/slide{slide_num}.xml.rels"
            if rels_path in members:
                rels_xml = z.read(rels_path)
                try:
                    root = ET.fromstring(rels_xml)
                    for rel in root.findall(f"{{{_REL_NS}}}Relationship"):
                        if rel.get("Type", "") == _AUDIO_REL_TYPE:
                            target = rel.get("Target", "")
                            media_path = _resolve_rel_target(target, slide_num)
                            if media_path in members:
                                audio = z.read(media_path)
                                print(
                                    f"[QA] Slide {slide_num}: found audio "
                                    f"'{media_path}' ({len(audio):,} bytes)",
                                    flush=True,
                                )
                            else:
                                print(
                                    f"[QA] Slide {slide_num}: rel points to "
                                    f"'{media_path}' but not found in ZIP",
                                    flush=True,
                                )
                            break
                except ET.ParseError as exc:
                    print(f"[QA] Slide {slide_num}: could not parse rels: {exc}", flush=True)

            slides[slide_num] = {"title": title, "audio": audio}
            slide_num += 1

    print(
        f"[QA] PPTX has {len(slides)} slides, "
        f"{sum(1 for s in slides.values() if s['audio'])} with audio",
        flush=True,
    )
    return slides

COGNITIVE_SERVICES_SCOPE = "https://cognitiveservices.azure.com/.default"

_OPENAI_ENDPOINT = os.environ.get(
    "AZURE_OPENAI_ENDPOINT",
    "https://bhs-development-public-foundry-r.cognitiveservices.azure.com",
).rstrip("/")
_OPENAI_DEPLOYMENT = os.environ.get("AZURE_OPENAI_DEPLOYMENT", "gpt-5.2")
_OPENAI_API_VERSION = "2025-01-01-preview"

_CHAT_URL = (
    f"{_OPENAI_ENDPOINT}/openai/deployments/{_OPENAI_DEPLOYMENT}"
    f"/chat/completions?api-version={_OPENAI_API_VERSION}"
)

_SYSTEM_PROMPT = """You are an audio quality evaluation assistant.
You will be given the ORIGINAL script text that was intended to be spoken,
and a TRANSCRIPTION of the actual audio that was generated by a text-to-speech system.

Your task is to compare the two and evaluate the accuracy of the TTS output.

Respond ONLY with a JSON object in this exact schema — no markdown, no explanation:
{
  "confidence": <integer 0-100>,
  "issues": [<string>, ...],
  "summary": "<one sentence>"
}

confidence: 100 = perfect match, 0 = completely different.
issues: list specific word substitutions, omissions, or additions. Empty array if none.
summary: concise one-sentence verdict.

If the original was in English but the transcription is in a different language
(because translation was applied before TTS), evaluate the transcription for
fluency and completeness relative to the translated content, not the English original."""


def _score_slide(original_text: str, transcription: str) -> dict:
    """Call GPT-5.2 to compare original vs transcription and return a score dict."""
    if not transcription.strip():
        return {
            "confidence": 0,
            "issues": ["No audio transcription returned — audio may be silent or STT failed"],
            "summary": "Unable to evaluate: no transcription available",
        }

    aad_token = DefaultAzureCredential().get_token(COGNITIVE_SERVICES_SCOPE).token

    payload = {
        "messages": [
            {"role": "system", "content": _SYSTEM_PROMPT},
            {
                "role": "user",
                "content": (
                    f"ORIGINAL SCRIPT:\n{original_text}\n\n"
                    f"AUDIO TRANSCRIPTION:\n{transcription}"
                ),
            },
        ],
        "temperature": 0,
        "max_completion_tokens": 300,
    }

    print(f"[QA] Calling GPT-5.2 to score slide...", flush=True)
    response = httpx.post(
        _CHAT_URL,
        headers={
            "Authorization": f"Bearer {aad_token}",
            "Content-Type": "application/json",
        },
        content=json.dumps(payload),
        timeout=60,
    )
    if not response.is_success:
        print(f"[QA] GPT error {response.status_code}: {response.text[:500]}", flush=True)
    response.raise_for_status()

    content = response.json()["choices"][0]["message"]["content"]
    result = json.loads(content)
    print(f"[QA] Score={result.get('confidence')}  issues={len(result.get('issues', []))}", flush=True)
    return result


def run_quality_check(
    pptx_bytes: bytes,
    slide_texts: list[dict],   # [{"title": str, "text": str}, ...] from word_parser
    voice: str,                # e.g. "fr-FR-DeniseNeural" — used to derive STT locale
) -> list[dict]:
    """
    For each slide in the PPTX:
      - Extract audio via XML relationships (not filename guessing)
      - Transcribe via Azure STT
      - Score against the matching Word script section via GPT-5.2

    Matching: PPTX slide N pairs with Word slide index N-1 (positional).
    Slides without audio get confidence=None.
    """
    locale = _locale_from_voice(voice)

    # Use proper PPTX XML metadata to discover audio
    pptx_slides = _extract_slides_from_pptx(pptx_bytes)

    results = []
    for pptx_num in sorted(pptx_slides.keys()):
        info = pptx_slides[pptx_num]
        slide_title = info["title"]
        audio = info["audio"]

        # Match against Word doc text by position (PPTX slide N → Word slide N-1)
        word_idx = pptx_num - 1
        if word_idx < len(slide_texts):
            original_text = slide_texts[word_idx].get("text", "")
        else:
            original_text = ""

        if not audio:
            results.append({
                "slide": pptx_num,
                "title": slide_title,
                "original_text": original_text,
                "transcription": None,
                "confidence": None,
                "issues": [],
                "summary": "No audio embedded for this slide",
            })
            continue

        print(f"[QA] Slide {pptx_num}: transcribing audio ({len(audio):,} bytes)...", flush=True)
        try:
            transcription = transcribe_mp3(audio, locale=locale)
        except Exception as exc:
            print(f"[QA] Slide {pptx_num}: STT error — {exc}", flush=True)
            transcription = ""

        score = _score_slide(original_text, transcription)

        results.append({
            "slide": pptx_num,
            "title": slide_title,
            "original_text": original_text,
            "transcription": transcription,
            "confidence": score.get("confidence"),
            "issues": score.get("issues", []),
            "summary": score.get("summary", ""),
        })

    return results
