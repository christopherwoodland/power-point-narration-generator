import { useEffect, useRef, useState } from 'react';
import JSZip from 'jszip';
import type { AppConfig, WizardState } from '../types';
import { processNarration, streamAiGeneration, streamVideoExport } from '../api/narrationApi';
import ProgressBar from '../components/ProgressBar';

interface Props {
  state: WizardState;
  config: AppConfig;
  onResultReady: (bytes: Uint8Array) => void;
  onQualityCheck: () => void;
  onRestart: () => void;
}

export default function Step3Generate({ state, config, onResultReady, onQualityCheck, onRestart }: Props) {
  const panelRef = useRef<HTMLDivElement>(null);
  useEffect(() => { panelRef.current?.focus(); }, []);

  const [progress, setProgress] = useState(0);
  const [progressLabel, setProgressLabel] = useState('Starting…');
  const [done, setDone] = useState(false);
  const [error, setError] = useState('');
  const [downloadUrl, setDownloadUrl] = useState('');
  const [exportingVideo, setExportingVideo] = useState(false);
  const [videoProgress, setVideoProgress] = useState(0);
  const [videoLabel, setVideoLabel] = useState('');
  const [mp4Url, setMp4Url] = useState('');
  const [videoError, setVideoError] = useState('');
  const [exportingMp3, setExportingMp3] = useState(false);
  const [mp3Url, setMp3Url] = useState('');
  const [mp3Error, setMp3Error] = useState('');
  const resultBytesRef = useRef<Uint8Array | null>(null);
  const hasStarted = useRef(false);

  useEffect(() => {
    if (hasStarted.current) return;
    hasStarted.current = true;
    run();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const run = async () => {
    try {
      if (state.aiMode) {
        await runAiMode();
      } else {
        await runStandardMode();
      }
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'Generation failed');
    }
  };

  const runStandardMode = async () => {
    const fd = new FormData();
    // In single-PPTX mode, scriptFile is null; use pptxFile as both script and presentation
    const scriptSource = state.scriptFile ?? state.pptxFile!;
    fd.append('script', scriptSource);
    fd.append('pptx', state.pptxFile!);
    fd.append('voice', state.voice);
    fd.append('slide_mapping', JSON.stringify(state.slideMapping));

    // Send user-edited slide texts so TTS uses the modified narration
    const slideTexts: Record<number, string> = {};
    state.parsedSlides.forEach((s, i) => { slideTexts[i] = s.text; });
    fd.append('slide_texts', JSON.stringify(slideTexts));

    setProgressLabel('Synthesising audio and embedding into slides…');
    setProgress(30);

    const blob = await processNarration(fd);
    const bytes = new Uint8Array(await blob.arrayBuffer());
    resultBytesRef.current = bytes;
    onResultReady(bytes);
    setDownloadUrl(URL.createObjectURL(blob));
    setProgress(100);
    setProgressLabel('Done!');
    setDone(true);
  };

  const handleVideoExport = async () => {
    const bytes = resultBytesRef.current;
    if (!bytes) return;

    setExportingVideo(true);
    setVideoError('');
    setVideoProgress(0);
    setVideoLabel('Starting video export…');

    try {
      const blob = new Blob([bytes.buffer as ArrayBuffer], {
        type: 'application/vnd.openxmlformats-officedocument.presentationml.presentation',
      });
      const fd = new FormData();
      fd.append('pptx', blob, 'narrated_presentation.pptx');

      for await (const event of streamVideoExport(fd)) {
        if (event.type === 'progress') {
          const slide = event.slide ?? 0;
          const total = event.total ?? 0;
          const pct = total > 0 ? Math.round((slide / total) * 90) : 0;
          setVideoProgress(pct);
          setVideoLabel(event.message ?? `Processing slide ${slide} of ${total}…`);
        } else if (event.type === 'done' && event.mp4) {
          const mp4Bytes = Uint8Array.from(atob(event.mp4), c => c.charCodeAt(0));
          const mp4Blob = new Blob([mp4Bytes], { type: 'video/mp4' });
          setMp4Url(URL.createObjectURL(mp4Blob));
          setVideoProgress(100);
          setVideoLabel('Done!');
        } else if (event.type === 'error') {
          throw new Error(event.message ?? 'Video export failed');
        }
      }
    } catch (e: unknown) {
      setVideoError(e instanceof Error ? e.message : 'Video export failed');
    } finally {
      setExportingVideo(false);
    }
  };

  const handleMp3Export = async () => {
    const bytes = resultBytesRef.current;
    if (!bytes) return;

    setExportingMp3(true);
    setMp3Error('');

    try {
      const pptxZip = await JSZip.loadAsync(bytes);
      const audioZip = new JSZip();
      const audioExtensions = ['.mp3', '.m4a', '.wav', '.wma', '.ogg'];

      let fileCount = 0;
      for (const [path, file] of Object.entries(pptxZip.files)) {
        if (file.dir) continue;
        const lower = path.toLowerCase();
        if (lower.startsWith('ppt/media/') && audioExtensions.some(ext => lower.endsWith(ext))) {
          const data = await file.async('uint8array');
          const filename = path.split('/').pop()!;
          audioZip.file(filename, data);
          fileCount++;
        }
      }

      if (fileCount === 0) {
        throw new Error('No audio files found in the presentation.');
      }

      const zipBlob = await audioZip.generateAsync({ type: 'blob' });
      setMp3Url(URL.createObjectURL(zipBlob));
    } catch (e: unknown) {
      setMp3Error(e instanceof Error ? e.message : 'MP3 export failed');
    } finally {
      setExportingMp3(false);
    }
  };

  const runAiMode = async () => {
    const fd = new FormData();
    fd.append('script', state.scriptFile!);
    fd.append('voice', state.voice);

    // Send user-edited slide texts
    const slideTexts: Record<number, string> = {};
    state.parsedSlides.forEach((s, i) => { slideTexts[i] = s.text; });
    fd.append('slide_texts', JSON.stringify(slideTexts));

    const total = state.parsedSlides.length;
    let slidesDone = 0;

    for await (const event of streamAiGeneration(fd)) {
      if (event.type === 'progress') {
        slidesDone = event.slide ?? slidesDone;
        const pct = total > 0 ? Math.round((slidesDone / (total * 4)) * 100) : 0;
        setProgress(Math.min(95, pct));
        setProgressLabel(event.message ?? `Processing slide ${slidesDone} of ${total}…`);
      } else if (event.type === 'done' && event.pptx) {
        const bytes = Uint8Array.from(atob(event.pptx), c => c.charCodeAt(0));
        resultBytesRef.current = bytes;
        onResultReady(bytes);
        const blob = new Blob([bytes], {
          type: 'application/vnd.openxmlformats-officedocument.presentationml.presentation',
        });
        setDownloadUrl(URL.createObjectURL(blob));
        setProgress(100);
        setProgressLabel('Done!');
        setDone(true);
      } else if (event.type === 'error') {
        throw new Error(event.message ?? 'AI generation failed');
      }
    }
  };

  return (
    <div ref={panelRef} tabIndex={-1} className="panel" data-testid="step-3">
      <div className="panel-header" aria-live="polite" aria-atomic="true">
        <h2 className="panel-title">
          {done ? 'Narration complete' : 'Generating narrated presentation…'}
        </h2>
      </div>

      {!done && !error && (
        <div className="generate-progress" data-testid="progress-wrap">
          <ProgressBar value={progress} label={progressLabel} />
          <p className="progress-hint">This may take a minute depending on slide count…</p>
        </div>
      )}

      {error && (
        <div className="alert alert--error" role="alert" data-testid="gen-error">
          <strong>Generation failed:</strong> {error}
          <div className="error-restart">
            <button className="btn btn--ghost" onClick={onRestart}>Start over</button>
          </div>
        </div>
      )}

      {done && (
        <div className="done-wrap" data-testid="done-wrap">
          <div className="done-hero done-hero--green">
            <div className="done-icon" aria-hidden="true">
              <svg width="64" height="64" viewBox="0 0 64 64" fill="none">
                <circle cx="32" cy="32" r="30" fill="var(--color-green-light)" />
                <circle cx="32" cy="32" r="30" stroke="var(--color-green)" strokeWidth="1.5" strokeOpacity="0.4" />
                <path d="M19 32l9 9 17-18" stroke="var(--color-green)" strokeWidth="3.5"
                  strokeLinecap="round" strokeLinejoin="round" />
              </svg>
            </div>
            <div className="done-hero-text">
              <h3 className="done-title done-title--green">Ready to download</h3>
              <p className="done-subtitle">
                Audio has been synthesised and embedded into each slide.
              </p>
            </div>
          </div>

          <div className="done-actions">
            <a
              href={downloadUrl}
              download="narrated_presentation.pptx"
              className="btn btn--primary btn--lg"
              data-testid="download-link"
            >
              <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true" style={{marginRight: '6px', verticalAlign: 'text-bottom'}}>
                <path d="M8 12l-4.5-4.5 1.06-1.06L7 8.88V2h2v6.88l2.44-2.44L12.5 7.5 8 12zM2 14h12v-2H2v2z"/>
              </svg>
              Download PPTX
            </a>

            {config.enable_quality_check && (
              <button
                className="btn btn--secondary"
                data-testid="btn-quality-check"
                onClick={onQualityCheck}
              >
                <svg width="15" height="15" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true" style={{marginRight: '5px', verticalAlign: 'text-bottom'}}>
                  <path d="M8 1a7 7 0 1 0 0 14A7 7 0 0 0 8 1zm.75 10.5h-1.5v-5h1.5v5zm0-6.5h-1.5V3.5h1.5V5z"/>
                </svg>
                Run Quality Check
              </button>
            )}

            {config.enable_video_export && !mp4Url && (
              <button
                className="btn btn--secondary"
                data-testid="btn-export-video"
                onClick={handleVideoExport}
                disabled={exportingVideo}
              >
                <svg width="15" height="15" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true" style={{marginRight: '5px', verticalAlign: 'text-bottom'}}>
                  <path d="M10.5 8L6 5v6l4.5-3zM2 3.5A1.5 1.5 0 0 1 3.5 2h9A1.5 1.5 0 0 1 14 3.5v9a1.5 1.5 0 0 1-1.5 1.5h-9A1.5 1.5 0 0 1 2 12.5v-9z"/>
                </svg>
                {exportingVideo ? videoLabel || 'Exporting…' : 'Export as MP4'}
              </button>
            )}

            {mp4Url && (
              <a
                href={mp4Url}
                download="narrated_presentation.mp4"
                className="btn btn--secondary"
                data-testid="download-video-link"
              >
                <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true" style={{marginRight: '6px', verticalAlign: 'text-bottom'}}>
                  <path d="M8 12l-4.5-4.5 1.06-1.06L7 8.88V2h2v6.88l2.44-2.44L12.5 7.5 8 12zM2 14h12v-2H2v2z"/>
                </svg>
                Download MP4
              </a>
            )}

            {config.enable_mp3_export && !mp3Url && (
              <button
                className="btn btn--secondary"
                data-testid="btn-export-mp3"
                onClick={handleMp3Export}
                disabled={exportingMp3}
              >
                <svg width="15" height="15" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true" style={{marginRight: '5px', verticalAlign: 'text-bottom'}}>
                  <path d="M8 1a2 2 0 0 1 2 2v5a2 2 0 1 1-4 0V3a2 2 0 0 1 2-2zm4 7a4 4 0 0 1-3.25 3.93V13.5h2v1.5h-5.5v-1.5h2v-1.57A4 4 0 0 1 4 8h1.5a2.5 2.5 0 0 0 5 0H12z"/>
                </svg>
                {exportingMp3 ? 'Extracting…' : 'Export MP3s'}
              </button>
            )}

            {mp3Url && (
              <a
                href={mp3Url}
                download="narration_audio_files.zip"
                className="btn btn--secondary"
                data-testid="download-mp3-link"
              >
                <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true" style={{marginRight: '6px', verticalAlign: 'text-bottom'}}>
                  <path d="M8 12l-4.5-4.5 1.06-1.06L7 8.88V2h2v6.88l2.44-2.44L12.5 7.5 8 12zM2 14h12v-2H2v2z"/>
                </svg>
                Download MP3s
              </a>
            )}

            <button className="btn btn--ghost" onClick={onRestart}>Start over</button>
          </div>

          {videoError && (
            <p className="alert alert--error video-error">{videoError}</p>
          )}

          {mp3Error && (
            <p className="alert alert--error video-error">{mp3Error}</p>
          )}

          {exportingVideo && (
            <div className="video-progress" aria-live="polite" aria-atomic="false">
              <ProgressBar value={videoProgress} label={videoLabel} />
            </div>
          )}
        </div>
      )}
    </div>
  );
}
