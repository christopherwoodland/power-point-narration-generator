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
from azure_credential import get_credential

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

_SYSTEM_PROMPT = """You are an instructional audio quality evaluator for e-learning and training content.

You will receive the ORIGINAL SCRIPT text that was intended to be spoken, and a TRANSCRIPTION
of the audio produced by a text-to-speech (TTS) system (which may also have been translated first).

Your job is to evaluate whether the AUDIO faithfully communicates the same MEANING and
INSTRUCTIONAL INTENT as the original script. You are NOT doing a word-for-word diff.

EVALUATION RULES:
1. MEANING over wording — paraphrases that convey identical information are fine (confidence ≥ 90).
   Do not penalise contractions, article changes, or minor grammatical reordering.
2. INSTRUCTIONAL accuracy is critical — changes that would cause a learner to do the wrong thing
   must be flagged as critical. Examples: wrong button name, reversed step order, omitted action,
   wrong number, wrong keyboard shortcut.
3. CONTEXT awareness — consider what type of content this is (intro, instruction, summary).
   An intro slide with a slight tonal shift is minor; a how-to slide missing a step is critical.
4. TRANSLATED content — if the transcription is in a different language to the original, assess
   whether the key concepts, actions, labels, and outcomes are faithfully represented in the
   target language. Fluency and natural phrasing in the target language is expected.
5. TTS/STT noise — short filler differences ("uh", "the"/"a", minor punctuation rhythm) are not
   worth reporting at all.

SEVERITY DEFINITIONS:
- critical_issue: a learner would receive incorrect information, perform the wrong action, or
  be confused about what to do next.
- minor_issue: a stylistic or phrasing difference that does not change meaning or instruction.

CONFIDENCE SCORING:
100 = Meaning perfectly preserved, no issues.
85–99 = Minor phrasing variation only, meaning intact.
60–84 = Some meaningful differences that slightly reduce clarity but do not mislead.
40–59 = One or more confusing differences; learner may be uncertain.
0–39 = Critical error — learner would likely do the wrong thing or be significantly misled.

Respond ONLY with a JSON object in this exact schema — no markdown, no explanation:
{
  "confidence": <integer 0-100>,
  "critical_issues": [<string>, ...],
  "minor_issues": [<string>, ...],
  "summary": "<one concise sentence giving a clear verdict for a reviewer>"
}

critical_issues: list only items where meaning or instruction is wrong. Empty array if none.
minor_issues: list only genuine (non-trivial) stylistic or phrasing differences. Empty array if none.
summary: tell a reviewer the verdict at a glance — what is the net effect on a learner?
  Examples of good summaries:
  - "All instructions clearly conveyed; minor phrasing variation only."
  - "Button label 'Next' was spoken as 'Real' — learner may click the wrong control."
  - "Japanese translation is accurate and natural; key actions fully preserved."
  - "Step 3 omitted entirely — learner will not know to confirm the dialog before continuing." """


def _score_slide(original_text: str, transcription: str) -> dict:
    """Call GPT-5.2 to compare original vs transcription and return a score dict."""
    if not transcription.strip():
        return {
            "confidence": 0,
            "critical_issues": ["No audio transcription returned — audio may be silent or STT failed"],
            "minor_issues": [],
            "summary": "Unable to evaluate: no transcription available",
        }

    aad_token = get_credential().get_token(COGNITIVE_SERVICES_SCOPE).token

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
        "max_completion_tokens": 400,
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
    print(
        f"[QA] Score={result.get('confidence')}  "
        f"critical={len(result.get('critical_issues', []))}  "
        f"minor={len(result.get('minor_issues', []))}",
        flush=True,
    )
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
                "critical_issues": [],
                "minor_issues": [],
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
            "critical_issues": score.get("critical_issues", []),
            "minor_issues": score.get("minor_issues", []),
            "summary": score.get("summary", ""),
        })

    return results
