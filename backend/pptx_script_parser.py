"""
Extract per-slide narration text from a PowerPoint file.

Strategy (in order):
  1. Extract text from all `a:t` elements in slide XML — catches normal shapes,
     text boxes, and SmartArt text that is inlined in the slide.
  2. If any slide has sparse text (< OCR_THRESHOLD chars), run the entire PPTX
     through Azure Document Intelligence (prebuilt-layout) which uses OCR to
     read text from images, charts, diagrams, and SmartArt data files.
  3. Merge: for slides where XML text is sparse, use the OCR result instead.

Returns the same structure as word_parser:
    [{"title": str, "text": str}, ...]
"""
import io
import os
import xml.etree.ElementTree as ET
import zipfile

from azure.ai.documentintelligence import DocumentIntelligenceClient
from azure.identity import DefaultAzureCredential

_A_NS = "http://schemas.openxmlformats.org/drawingml/2006/main"
_P_NS = "http://schemas.openxmlformats.org/presentationml/2006/main"

_DOC_INTEL_ENDPOINT = os.environ.get(
    "AZURE_DOC_INTEL_ENDPOINT",
    "https://bhs-development-public-foundry-r.cognitiveservices.azure.com/",
).rstrip("/")

# Slides with fewer body chars than this will be supplemented with OCR
_OCR_THRESHOLD = 60


def _get_shape_texts(slide_xml: bytes) -> tuple[str, str]:
    """
    Parse a slide XML and return (title, body_text).

    Title: comes from the title/ctrTitle placeholder shape.
    Body:  ALL other a:t text runs anywhere in the slide XML
           (catches normal shapes, text boxes, and inlined SmartArt text).
           Duplicate runs are deduplicated while preserving order.
    """
    root = ET.fromstring(slide_xml)
    title = ""
    title_texts: set[str] = set()

    # --- find title text from placeholder shapes ---
    for sp in root.iter(f"{{{_P_NS}}}sp"):
        nvSpPr = sp.find(f".//{{{_P_NS}}}nvSpPr")
        if nvSpPr is None:
            continue
        ph = nvSpPr.find(f".//{{{_P_NS}}}ph")
        if ph is None:
            continue
        ph_type = ph.get("type", "")
        if ph_type in ("title", "ctrTitle") and not title:
            runs = [t.text or "" for t in sp.iter(f"{{{_A_NS}}}t")]
            title = "".join(runs).strip()
            title_texts = set(r.strip() for r in runs if r.strip())

    # --- collect ALL a:t text from the entire slide (deduped) ---
    seen: set[str] = set()
    body_parts: list[str] = []
    for t_el in root.iter(f"{{{_A_NS}}}t"):
        chunk = (t_el.text or "").strip()
        if not chunk or chunk in seen or chunk in title_texts:
            continue
        seen.add(chunk)
        body_parts.append(chunk)

    return title, "\n".join(body_parts)


def _ocr_pptx_pages(pptx_bytes: bytes) -> list[str]:
    """
    Send the PPTX to Azure Document Intelligence (prebuilt-layout) and return
    a list of text strings, one per page/slide. Uses DefaultAzureCredential.
    """
    print("[OCR] Running Document Intelligence on PPTX...", flush=True)
    client = DocumentIntelligenceClient(
        endpoint=_DOC_INTEL_ENDPOINT,
        credential=DefaultAzureCredential(),
    )
    poller = client.begin_analyze_document(
        "prebuilt-layout",
        analyze_request=pptx_bytes,
        content_type="application/octet-stream",
    )
    result = poller.result()

    pages_text: list[str] = []
    for page in result.pages or []:
        lines = [line.content for line in (page.lines or []) if line.content]
        pages_text.append("\n".join(lines))
        print(f"[OCR] Page {page.page_number}: {len(lines)} lines", flush=True)

    print(f"[OCR] Done — {len(pages_text)} pages extracted", flush=True)
    return pages_text


_REL_NS = "http://schemas.openxmlformats.org/package/2006/relationships"
_NOTES_REL_TYPE = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/notesSlide"


def _get_notes_text(z: zipfile.ZipFile, slide_num: int, members: set) -> str:
    """
    Return speaker-notes text for a slide, or '' if none.
    Follows the slide's relationship file to find the notes slide, then
    extracts all a:t text from it excluding the slide-image placeholder.
    """
    rels_path = f"ppt/slides/_rels/slide{slide_num}.xml.rels"
    if rels_path not in members:
        return ""
    rels_root = ET.fromstring(z.read(rels_path))
    notes_target = None
    for rel in rels_root:
        if rel.get("Type") == _NOTES_REL_TYPE:
            # Target is relative to ppt/slides/, e.g. "../notesSlides/notesSlide1.xml"
            target = rel.get("Target", "")
            # Resolve relative path
            notes_target = "ppt/notesSlides/" + target.split("/notesSlides/")[-1]
            break
    if not notes_target or notes_target not in members:
        return ""
    notes_root = ET.fromstring(z.read(notes_target))
    # Skip the sp that contains a ph of type "sp" (slide image thumbnail)
    parts: list[str] = []
    for sp in notes_root.iter(f"{{{_P_NS}}}sp"):
        nvSpPr = sp.find(f".//{{{_P_NS}}}nvSpPr")
        if nvSpPr is not None:
            ph = nvSpPr.find(f".//{{{_P_NS}}}ph")
            if ph is not None and ph.get("type") == "sp":
                continue  # skip slide-image placeholder
        texts = [t.text or "" for t in sp.iter(f"{{{_A_NS}}}t")]
        chunk = "".join(texts).strip()
        if chunk:
            parts.append(chunk)
    return "\n".join(parts)


def extract_slides_from_pptx(pptx_bytes: bytes) -> list[dict]:
    """
    Extract per-slide text from a PPTX file.
    Layer 1: shape XML text (all a:t elements).
    Layer 2: speaker notes (free, no API call) — appended to body text.
    Layer 3: Document Intelligence OCR fallback for slides still sparse after
             layers 1+2 (e.g. SmartArt data files, image-only diagrams).
    Returns [{"title": str, "text": str}, ...] ordered by slide number.
    """
    slides: list[dict] = []

    with zipfile.ZipFile(io.BytesIO(pptx_bytes)) as z:
        members = set(z.namelist())
        slide_num = 1
        while True:
            slide_path = f"ppt/slides/slide{slide_num}.xml"
            if slide_path not in members:
                break
            slide_xml = z.read(slide_path)
            title, text = _get_shape_texts(slide_xml)

            # Layer 2: append speaker notes
            notes_text = _get_notes_text(z, slide_num, members)
            if notes_text:
                combined = (text + "\n" + notes_text).strip() if text else notes_text
                print(f"[Notes] Slide {slide_num}: added {len(notes_text)} chars from speaker notes", flush=True)
            else:
                combined = text

            slides.append({
                "title": title or f"Slide {slide_num}",
                "text": combined,
            })
            slide_num += 1

    if not slides:
        return slides

    # Check if any slides have sparse text — if so, run OCR on the whole PPTX
    sparse = [i for i, s in enumerate(slides) if len(s["text"]) < _OCR_THRESHOLD]
    if sparse:
        print(
            f"[OCR] {len(sparse)} slide(s) have sparse text — triggering OCR "
            f"(slides: {[i+1 for i in sparse]})",
            flush=True,
        )
        try:
            ocr_pages = _ocr_pptx_pages(pptx_bytes)
            for i, slide in enumerate(slides):
                if i < len(ocr_pages) and len(slide["text"]) < _OCR_THRESHOLD:
                    ocr_text = ocr_pages[i]
                    # Strip the title line from OCR text if it's at the top
                    if slide["title"] and ocr_text.startswith(slide["title"]):
                        ocr_text = ocr_text[len(slide["title"]):].strip()
                    if ocr_text:
                        print(
                            f"[OCR] Slide {i+1}: replacing sparse text "
                            f"({len(slide['text'])} chars) with OCR "
                            f"({len(ocr_text)} chars)",
                            flush=True,
                        )
                        slide["text"] = ocr_text
        except Exception as exc:
            print(f"[OCR] Failed — falling back to shape text: {exc}", flush=True)

    return slides
