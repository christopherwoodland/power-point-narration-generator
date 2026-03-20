import sys, pathlib
sys.path.insert(0, str(pathlib.Path(__file__).parent / "backend"))
from word_parser import extract_slides

data = open("worddoc.docx", "rb").read()
slides = extract_slides(data)
print(f"Found {len(slides)} slides")
for s in slides[:6]:
    preview = s["text"][:80].replace("\n", " ")
    print(f"  [{s['title']}] => {preview}...")
