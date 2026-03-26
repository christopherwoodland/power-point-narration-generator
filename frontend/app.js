/* ── State ──────────────────────────────────────────────── */
let docxFile = null;
let pptxFile = null;
let parsedSlides = [];
let pptxSlideCount = 0;
let aiMode = false;

/* ── Element refs ───────────────────────────────────────── */
const chkAiMode    = document.getElementById("chk-ai-mode");
const cardPptxWrap = document.getElementById("card-pptx");
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
const btnRestart      = document.getElementById("btn-restart");
const btnExportVideo       = document.getElementById("btn-export-video");
const doneActionRow        = document.getElementById("done-action-row");
const doneUtilRow          = document.getElementById("done-util-row");
const videoProgressWrap    = document.getElementById("video-progress-wrap");
const videoProgressFill    = document.getElementById("video-progress-fill");
const videoProgressLabel   = document.getElementById("video-progress-label");
const genErrorWrap  = document.getElementById("gen-error-wrap");
const genErrorMsg   = document.getElementById("gen-error-msg");
const btnBack2       = document.getElementById("btn-back-2");
const btnQualityCheck = document.getElementById("btn-quality-check");

/* QC panel */
const inputQcDocx    = document.getElementById("input-qc-docx");
const inputQcPptx    = document.getElementById("input-qc-pptx");
const cardQcDocx     = document.getElementById("card-qc-docx");
const cardQcPptx     = document.getElementById("card-qc-pptx");
const qcDocxName     = document.getElementById("qc-docx-name");
const qcPptxName     = document.getElementById("qc-pptx-name");
const btnRunQc       = document.getElementById("btn-run-qc");
const btnBack4       = document.getElementById("btn-back-4");
const qcError        = document.getElementById("qc-error");
const qcRunning      = document.getElementById("qc-running");
const qcRunningLabel = document.getElementById("qc-running-label");
const qcResults      = document.getElementById("qc-results");
const qcTbody        = document.getElementById("qc-tbody");
const qcSummaryBar   = document.getElementById("qc-summary-bar");
const qcVoiceSelect  = document.getElementById("qc-voice-select");

let qcDocxFile = null;
let qcPptxFile = null;

/* ── Step helpers ───────────────────────────────────────── */
function goTo(step) {
  [1, 2, 3, 4].forEach(n => {
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

/* ── AI mode toggle ─────────────────────────────────────── */
chkAiMode.addEventListener("change", () => {
  aiMode = chkAiMode.checked;
  if (aiMode) {
    cardPptxWrap.classList.add("ai-mode-disabled");
    cardPptxWrap.querySelector(".upload-label").textContent = "PowerPoint (optional)";
    cardPptxWrap.querySelector(".upload-hint").textContent = "AI will generate the slides — or upload your own template";
  } else {
    cardPptxWrap.classList.remove("ai-mode-disabled");
    cardPptxWrap.querySelector(".upload-label").textContent = "PowerPoint Presentation";
    cardPptxWrap.querySelector(".upload-hint").textContent = ".pptx \u2014 the presentation to narrate";
  }
  checkReady();
});

function checkReady() {
  btnParse.disabled = aiMode ? !docxFile : !(docxFile && pptxFile);
}

/* ── Step 1 → Step 2: Parse ─────────────────────────────── */
btnParse.addEventListener("click", async () => {
  parseError.classList.add("hidden");
  btnParse.disabled = true;
  btnParse.textContent = "Parsing…";

  const fd = new FormData();
  fd.append("script", docxFile);
  if (!aiMode && pptxFile) fd.append("pptx", pptxFile);
  fd.append("ai_mode", aiMode ? "true" : "false");

  try {
    const res = await fetch("/api/parse", { method: "POST", body: fd });
    if (!res.ok) {
      const err = await res.json().catch(() => ({ detail: res.statusText }));
      const detail = Array.isArray(err.detail)
        ? err.detail.map(e => `${e.loc?.join(".")} — ${e.msg}`).join("; ")
        : err.detail || res.statusText;
      throw new Error(detail);
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

  // In AI mode: show a preview-only table, no PPTX mapping dropdowns
  if (aiMode) {
    document.querySelector("#panel-2 h2").textContent = "Review slides for AI generation";
    countsBar.innerHTML = `<span>📄 Script slides: <b>${wordCount}</b></span><span>✦ AI will build ${wordCount} slide(s) with images</span>`;
    mismatch.classList.add("hidden");
    mappingTbody.innerHTML = parsedSlides.map((slide, idx) => {
      const preview = escapeHtml(slide.text.substring(0, 220)) + (slide.text.length > 220 ? "…" : "");
      return `<tr>
        <td class="td-num">${idx + 1}</td>
        <td class="td-title">${escapeHtml(slide.title)}</td>
        <td class="td-preview-col" colspan="2"><div class="td-preview">${preview}</div></td>
      </tr>`;
    }).join("");
    // Rename button for AI mode
    btnGenerate.textContent = "✦ Generate AI Presentation →";
    return;
  }

  document.querySelector("#panel-2 h2").textContent = "Review slide mapping";
  btnGenerate.textContent = "Generate Narrated PPTX →";

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
      <td class="td-preview-col"><div class="td-preview">${preview}</div></td>
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
  if (aiMode) {
    // AI generation mode — stream NDJSON progress events from /api/generate-ai
    goTo(3);
    progressWrap.classList.remove("hidden");
    doneWrap.classList.add("hidden");
    genErrorWrap.classList.add("hidden");
    progressFill.style.width = "0%";
    progressLabel.textContent = "Starting AI generation…";

    const fd = new FormData();
    fd.append("script", docxFile);
    fd.append("voice",  voiceSelect.value);

    try {
      const res = await fetch("/api/generate-ai", { method: "POST", body: fd });
      if (!res.ok) {
        const err = await res.json().catch(() => ({ detail: res.statusText }));
        throw new Error(err.detail || res.statusText);
      }

      const reader  = res.body.getReader();
      const decoder = new TextDecoder();
      let   buf     = "";

      // Phase weights for progress bar (structure + build = 40%, image = 30%, tts = 30%)
      const phaseEnd = { structure: 0.15, image: 0.45, build: 0.50, tts: 0.95 };

      while (true) {
        const { done, value } = await reader.read();
        if (done) break;
        buf += decoder.decode(value, { stream: true });

        // Process all complete lines in the buffer
        let nl;
        while ((nl = buf.indexOf("\n")) !== -1) {
          const line = buf.slice(0, nl).trim();
          buf = buf.slice(nl + 1);
          if (!line) continue;

          let event;
          try { event = JSON.parse(line); } catch { continue; }

          if (event.type === "progress") {
            const { slide, total, phase } = event;
            progressLabel.textContent = event.message || `Processing slide ${slide} of ${total}…`;
            // Per-slide progress: each slide gets an equal share of 0–95%
            const weight = phaseEnd[phase] ?? 0.5;
            const pct    = ((slide - 1 + weight) / total) * 95;
            progressFill.style.width = pct.toFixed(1) + "%";

          } else if (event.type === "done") {
            // Decode base64 PPTX → Blob
            const b64    = event.pptx;
            const binary = atob(b64);
            const bytes  = new Uint8Array(binary.length);
            for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
            const blob = new Blob([bytes], {
              type: "application/vnd.openxmlformats-officedocument.presentationml.presentation"
            });
            downloadLink.href     = URL.createObjectURL(blob);
            downloadLink.download = "ai_generated_presentation.pptx";
            progressFill.style.width = "100%";
            progressLabel.textContent = "Done!";
            setTimeout(() => {
              progressWrap.classList.add("hidden");
              doneWrap.classList.remove("hidden");
            }, 600);

          } else if (event.type === "error") {
            throw new Error(event.message);
          }
        }
      }
    } catch (e) {
      progressWrap.classList.add("hidden");
      genErrorMsg.textContent = e.message;
      genErrorWrap.classList.remove("hidden");
    }
    return;
  }

  // Standard mode — collect mapping from dropdowns
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
  animateProgress(false);

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
function animateProgress(isAiMode = false) {
  let pct = 0;
  const msgs = isAiMode ? [
    "Analysing slide content with GPT…",
    "Generating slide images with AI…",
    "Building PowerPoint structure…",
    "Synthesising narration audio…",
    "Embedding audio into slides…",
    "Finalising AI presentation…",
  ] : [
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
  aiMode = false;
  chkAiMode.checked = false;
  cardPptxWrap.classList.remove("ai-mode-disabled");
  cardPptxWrap.querySelector(".upload-label").textContent = "PowerPoint Presentation";
  cardPptxWrap.querySelector(".upload-hint").textContent = ".pptx \u2014 the presentation to narrate";
  inputDocx.value = ""; inputPptx.value = "";
  docxName.textContent = "No file chosen";
  pptxName.textContent = "No file chosen";
  cardDocx.classList.remove("has-file");
  cardPptx.classList.remove("has-file");
  btnParse.disabled = true;
  goTo(1);
});

btnBack2.addEventListener("click", () => goTo(2));

/* ── Export as Video ───────────────────────────────────── */
btnExportVideo.addEventListener("click", async () => {
  const href = downloadLink.href;
  if (!href || !href.startsWith("blob:")) {
    alert("Please generate and download the PPTX first.");
    return;
  }

  // Show progress bar, hide action buttons
  doneActionRow.classList.add("hidden");
  doneUtilRow.classList.add("hidden");
  videoProgressWrap.classList.remove("hidden");
  videoProgressFill.style.width = "0%";
  videoProgressLabel.textContent = "Preparing video export…";

  try {
    const pptxBlob = await fetch(href).then(r => r.blob());
    const pptxFile = new File([pptxBlob], "narrated_presentation.pptx", {
      type: "application/vnd.openxmlformats-officedocument.presentationml.presentation"
    });

    const fd = new FormData();
    fd.append("pptx", pptxFile);

    const res = await fetch("/api/export-video", { method: "POST", body: fd });
    if (!res.ok) {
      const err = await res.json().catch(() => ({ detail: res.statusText }));
      throw new Error(err.detail || res.statusText);
    }

    const reader = res.body.getReader();
    const decoder = new TextDecoder();
    let buf = "";

    while (true) {
      const { value, done } = await reader.read();
      if (done) break;
      buf += decoder.decode(value, { stream: true });
      const lines = buf.split("\n");
      buf = lines.pop(); // keep incomplete last line

      for (const line of lines) {
        if (!line.trim()) continue;
        const event = JSON.parse(line);

        if (event.type === "progress") {
          videoProgressLabel.textContent = event.message;
          // export phase = 5%, encode = 5-95% spread over slides, concat = 95-100%
          let pct = 0;
          if (event.phase === "export") {
            pct = 5;
          } else if (event.phase === "encode") {
            pct = 5 + ((event.slide - 1) / Math.max(event.total, 1)) * 88;
          } else if (event.phase === "concat") {
            pct = 95;
          }
          videoProgressFill.style.width = pct.toFixed(1) + "%";

        } else if (event.type === "done") {
          videoProgressFill.style.width = "100%";
          videoProgressLabel.textContent = "Done! Downloading…";

          const b64    = event.mp4;
          const binary = atob(b64);
          const bytes  = new Uint8Array(binary.length);
          for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
          const videoBlob = new Blob([bytes], { type: "video/mp4" });
          const videoUrl  = URL.createObjectURL(videoBlob);
          const a = document.createElement("a");
          a.href = videoUrl; a.download = "narrated_presentation.mp4";
          document.body.appendChild(a); a.click(); document.body.removeChild(a);
          setTimeout(() => URL.revokeObjectURL(videoUrl), 60000);

        } else if (event.type === "error") {
          throw new Error(event.message);
        }
      }
    }
  } catch (e) {
    alert("Video export failed: " + e.message);
  } finally {
    videoProgressWrap.classList.add("hidden");
    videoProgressFill.style.width = "0%";
    doneActionRow.classList.remove("hidden");
    doneUtilRow.classList.remove("hidden");
  }
});

/* ── Quality Check nav ──────────────────────────────────── */
btnQualityCheck.addEventListener("click", () => {
  // Pre-fill QC pickers with the same files already uploaded in step 1
  if (docxFile && !qcDocxFile) {
    qcDocxFile = docxFile;
    qcDocxName.textContent = docxFile.name;
    cardQcDocx.classList.add("has-file");
  }
  // Pre-fill the narrated PPTX from the download blob if available
  const href = downloadLink.href;
  if (href && href.startsWith("blob:") && !qcPptxFile) {
    fetch(href).then(r => r.blob()).then(blob => {
      qcPptxFile = new File([blob], "narrated_presentation.pptx",
        { type: "application/vnd.openxmlformats-officedocument.presentationml.presentation" });
      qcPptxName.textContent = qcPptxFile.name;
      cardQcPptx.classList.add("has-file");
      checkQcReady();
    });
  }
  // Mirror voice selection
  qcVoiceSelect.value = voiceSelect.value;
  checkQcReady();
  goTo(4);
});

function checkQcReady() {
  btnRunQc.disabled = !(qcDocxFile && qcPptxFile);
}

setupFilePicker(cardQcDocx, inputQcDocx, qcDocxName, f => { qcDocxFile = f; checkQcReady(); });
setupFilePicker(cardQcPptx, inputQcPptx, qcPptxName, f => { qcPptxFile = f; checkQcReady(); });

btnBack4.addEventListener("click", () => goTo(3));

/* ── Run Quality Check ──────────────────────────────────── */
btnRunQc.addEventListener("click", async () => {
  qcError.classList.add("hidden");
  qcResults.classList.add("hidden");
  qcRunning.classList.remove("hidden");
  qcRunningLabel.textContent = "Transcribing audio and scoring slides…";
  btnRunQc.disabled = true;

  const fd = new FormData();
  fd.append("script", qcDocxFile);
  fd.append("pptx",   qcPptxFile);
  fd.append("voice",  qcVoiceSelect.value);

  try {
    const res = await fetch("/api/quality-check", { method: "POST", body: fd });
    if (!res.ok) {
      const err = await res.json().catch(() => ({ detail: res.statusText }));
      throw new Error(err.detail || res.statusText);
    }
    const data = await res.json();
    renderQcResults(data.results);
  } catch (e) {
    qcError.textContent = `Error: ${e.message}`;
    qcError.classList.remove("hidden");
  } finally {
    qcRunning.classList.add("hidden");
    btnRunQc.disabled = false;
  }
});

function renderQcResults(results) {
  const scored = results.filter(r => r.confidence !== null && r.confidence !== undefined);
  const avg = scored.length
    ? Math.round(scored.reduce((s, r) => s + r.confidence, 0) / scored.length)
    : null;

  const green  = scored.filter(r => r.confidence >= 80).length;
  const yellow = scored.filter(r => r.confidence >= 50 && r.confidence < 80).length;
  const red    = scored.filter(r => r.confidence < 50).length;

  qcSummaryBar.innerHTML =
    (avg !== null ? `<span>Average confidence: <b>${avg}%</b></span>` : "") +
    `<span><span class="badge-confidence high">✅ ≥80 - ${green}</span></span>` +
    `<span><span class="badge-confidence mid">⚠️ 50–79 - ${yellow}</span></span>` +
    `<span><span class="badge-confidence low">❌ &lt;50 - ${red}</span></span>`;

  qcTbody.innerHTML = results.map(r => {
    let badge;
    if (r.confidence === null || r.confidence === undefined) {
      badge = `<span class="badge-confidence none">No audio</span>`;
    } else if (r.confidence >= 80) {
      badge = `<span class="badge-confidence high">${r.confidence}%</span>`;
    } else if (r.confidence >= 50) {
      badge = `<span class="badge-confidence mid">${r.confidence}%</span>`;
    } else {
      badge = `<span class="badge-confidence low">${r.confidence}%</span>`;
    }

    const critList = r.critical_issues && r.critical_issues.length
      ? `<ul class="issues-critical">${r.critical_issues.map(i => `<li>${escapeHtml(i)}</li>`).join("")}</ul>`
      : `<span class="issues-none">None</span>`;
    const minorList = r.minor_issues && r.minor_issues.length
      ? `<ul class="issues-minor">${r.minor_issues.map(i => `<li>${escapeHtml(i)}</li>`).join("")}</ul>`
      : `<span class="issues-none">None</span>`;

    return `<tr>
      <td class="td-num">${r.slide}</td>
      <td class="td-title">${escapeHtml(r.title || "")}</td>
      <td style="text-align:center">${badge}</td>
      <td class="td-original">${escapeHtml(r.original_text || "—")}</td>
      <td class="td-transcription">${escapeHtml(r.transcription || "—")}</td>
      <td class="td-issues">${critList}</td>
      <td class="td-issues">${minorList}</td>
      <td>${escapeHtml(r.summary || "—")}</td>
    </tr>`;
  }).join("");

  qcResults.classList.remove("hidden");
}
