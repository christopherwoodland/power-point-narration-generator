/* ── State ──────────────────────────────────────────────── */
let docxFile = null;
let pptxFile = null;
let parsedSlides = [];
let pptxSlideCount = 0;

/* ── Element refs ───────────────────────────────────────── */
const inputDocx    = document.getElementById("input-docx");
const inputPptx    = document.getElementById("input-pptx");
const cardDocx     = document.getElementById("card-docx");
const cardPptx     = document.getElementById("card-pptx");
const docxName     = document.getElementById("docx-name");
const pptxName     = document.getElementById("pptx-name");
const btnParse     = document.getElementById("btn-parse");
const parseError   = document.getElementById("parse-error");
const voiceSelect  = document.getElementById("voice-select");

const countsBar    = document.getElementById("counts-bar");
const mismatch     = document.getElementById("mismatch-banner");
const mismatchMsg  = document.getElementById("mismatch-msg");
const mappingTbody = document.getElementById("mapping-tbody");
const btnBack1     = document.getElementById("btn-back-1");
const btnGenerate  = document.getElementById("btn-generate");

const progressFill  = document.getElementById("progress-fill");
const progressLabel = document.getElementById("progress-label");
const progressWrap  = document.getElementById("progress-wrap");
const doneWrap      = document.getElementById("done-wrap");
const downloadLink  = document.getElementById("download-link");
const btnRestart    = document.getElementById("btn-restart");
const genErrorWrap  = document.getElementById("gen-error-wrap");
const genErrorMsg   = document.getElementById("gen-error-msg");
const btnBack2      = document.getElementById("btn-back-2");

/* ── Step helpers ───────────────────────────────────────── */
function goTo(step) {
  [1, 2, 3].forEach(n => {
    document.getElementById(`panel-${n}`).classList.toggle("active", n === step);
    const ind = document.getElementById(`step-indicator-${n}`);
    ind.classList.remove("active", "done");
    if (n < step) ind.classList.add("done");
    if (n === step) ind.classList.add("active");
  });
}

/* ── File pickers ───────────────────────────────────────── */
function setupFilePicker(card, input, nameEl, onPick) {
  // The card is a <label> containing the input, so clicks are handled natively.
  // Only handle keyboard activation for accessibility.
  card.addEventListener("keydown", e => { if (e.key === "Enter" || e.key === " ") input.click(); });
  input.addEventListener("change", () => {
    const file = input.files[0];
    if (!file) return;
    onPick(file);
    nameEl.textContent = file.name;
    card.classList.add("has-file");
    checkReady();
  });
}

setupFilePicker(cardDocx, inputDocx, docxName, f => { docxFile = f; });
setupFilePicker(cardPptx, inputPptx, pptxName, f => { pptxFile = f; });

function checkReady() {
  btnParse.disabled = !(docxFile && pptxFile);
}

/* ── Step 1 → Step 2: Parse ─────────────────────────────── */
btnParse.addEventListener("click", async () => {
  parseError.classList.add("hidden");
  btnParse.disabled = true;
  btnParse.textContent = "Parsing…";

  const fd = new FormData();
  fd.append("script", docxFile);
  fd.append("pptx",   pptxFile);

  try {
    const res = await fetch("/api/parse", { method: "POST", body: fd });
    if (!res.ok) {
      const err = await res.json().catch(() => ({ detail: res.statusText }));
      throw new Error(err.detail || res.statusText);
    }
    const data = await res.json();
    parsedSlides   = data.slides;
    pptxSlideCount = data.pptx_slide_count;
    buildMappingTable();
    goTo(2);
  } catch (e) {
    parseError.textContent = `Error: ${e.message}`;
    parseError.classList.remove("hidden");
  } finally {
    btnParse.disabled = false;
    btnParse.textContent = "Next: Preview Slides →";
  }
});

/* ── Build mapping table ─────────────────────────────────── */
function buildMappingTable() {
  const wordCount = parsedSlides.length;

  countsBar.innerHTML =
    `<span>📄 Script slides: <b>${wordCount}</b></span>` +
    `<span>📊 PPTX slides: <b>${pptxSlideCount}</b></span>`;

  if (wordCount !== pptxSlideCount) {
    const diff = wordCount - pptxSlideCount;
    mismatchMsg.textContent = diff > 0
      ? `Your script has ${diff} more slide(s) than the PowerPoint. `
      : `Your PowerPoint has ${Math.abs(diff)} more slide(s) than the script. `;
    mismatch.classList.remove("hidden");
  } else {
    mismatch.classList.add("hidden");
  }

  // Build PPTX slide options
  const pptxOptions = ['<option value="-1">— Skip (no audio) —</option>'];
  for (let i = 0; i < pptxSlideCount; i++) {
    pptxOptions.push(`<option value="${i}">PPTX Slide ${i + 1}</option>`);
  }
  const optionsHtml = pptxOptions.join("");

  mappingTbody.innerHTML = parsedSlides.map((slide, idx) => {
    const defaultVal = idx < pptxSlideCount ? idx : -1;
    const preview = escapeHtml(slide.text.substring(0, 200)) + (slide.text.length > 200 ? "…" : "");
    const selectedOpts = pptxOptions.map((o, i) => {
      if (i === 0) return i === 0 && defaultVal === -1
        ? o.replace("<option", "<option selected")
        : o;
      const pptxIdx = i - 1;
      return pptxIdx === defaultVal
        ? o.replace("<option", "<option selected")
        : o;
    }).join("");

    return `<tr>
      <td class="td-num">${idx + 1}</td>
      <td class="td-title">${escapeHtml(slide.title)}</td>
      <td><div class="td-preview">${preview}</div></td>
      <td class="td-map">
        <select data-word-idx="${idx}">${selectedOpts}</select>
      </td>
    </tr>`;
  }).join("");
}

function escapeHtml(str) {
  return str.replace(/&/g,"&amp;").replace(/</g,"&lt;").replace(/>/g,"&gt;")
            .replace(/"/g,"&quot;").replace(/'/g,"&#39;");
}

/* ── Step 2 nav ─────────────────────────────────────────── */
btnBack1.addEventListener("click", () => goTo(1));

/* ── Step 2 → Step 3: Generate ──────────────────────────── */
btnGenerate.addEventListener("click", async () => {
  // Collect mapping from dropdowns
  const mapping = {};
  document.querySelectorAll("#mapping-tbody select").forEach(sel => {
    const wordIdx = sel.dataset.wordIdx;
    const pptxIdx = parseInt(sel.value, 10);
    if (pptxIdx >= 0) mapping[wordIdx] = pptxIdx;
  });

  if (Object.keys(mapping).length === 0) {
    alert("No slides are mapped to PPTX slides. Please assign at least one.");
    return;
  }

  goTo(3);
  progressWrap.classList.remove("hidden");
  doneWrap.classList.add("hidden");
  genErrorWrap.classList.add("hidden");
  animateProgress();

  const fd = new FormData();
  fd.append("script",        docxFile);
  fd.append("pptx",          pptxFile);
  fd.append("voice",         voiceSelect.value);
  fd.append("slide_mapping", JSON.stringify(mapping));

  try {
    const res = await fetch("/api/process", { method: "POST", body: fd });
    if (!res.ok) {
      const err = await res.json().catch(() => ({ detail: res.statusText }));
      throw new Error(err.detail || res.statusText);
    }
    const blob = await res.blob();
    const url  = URL.createObjectURL(blob);
    downloadLink.href = url;
    progressFill.style.width = "100%";
    progressLabel.textContent = "Done!";
    setTimeout(() => {
      progressWrap.classList.add("hidden");
      doneWrap.classList.remove("hidden");
    }, 600);
  } catch (e) {
    progressWrap.classList.add("hidden");
    genErrorMsg.textContent = e.message;
    genErrorWrap.classList.remove("hidden");
  }
});

/* ── Fake progress animation while backend works ─────────── */
function animateProgress() {
  let pct = 0;
  const msgs = [
    "Sending to Azure TTS…",
    "Synthesizing slide narrations…",
    "Embedding audio into slides…",
    "Finalising presentation…",
  ];
  let msgIdx = 0;
  progressFill.style.width = "0%";

  const iv = setInterval(() => {
    // Slow asymptotic approach to 90%
    if (pct < 90) {
      pct += (90 - pct) * 0.04;
      progressFill.style.width = pct.toFixed(1) + "%";
    }
    if (Math.floor(pct) % 22 === 0 && msgIdx < msgs.length - 1) {
      msgIdx++;
      progressLabel.textContent = msgs[msgIdx];
    }
  }, 400);

  // Stop when done (called from fetch handler)
  progressFill._stopAnim = () => clearInterval(iv);
  progressWrap.addEventListener("hidden", () => clearInterval(iv), { once: true });
}

/* ── Restart ─────────────────────────────────────────────── */
btnRestart.addEventListener("click", () => {
  docxFile = null; pptxFile = null;
  parsedSlides = []; pptxSlideCount = 0;
  inputDocx.value = ""; inputPptx.value = "";
  docxName.textContent = "No file chosen";
  pptxName.textContent = "No file chosen";
  cardDocx.classList.remove("has-file");
  cardPptx.classList.remove("has-file");
  btnParse.disabled = true;
  goTo(1);
});

btnBack2.addEventListener("click", () => goTo(2));
