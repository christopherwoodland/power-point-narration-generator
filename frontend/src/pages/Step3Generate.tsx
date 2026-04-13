import { useEffect, useRef, useState } from 'react';
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
    fd.append('script', state.scriptFile!);
    fd.append('pptx', state.pptxFile!);
    fd.append('voice', state.voice);
    fd.append('slide_mapping', JSON.stringify(state.slideMapping));

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

  const runAiMode = async () => {
    const fd = new FormData();
    fd.append('script', state.scriptFile!);
    fd.append('voice', state.voice);

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
    <div className="panel" data-testid="step-3">
      <div className="panel-header" aria-live="polite" aria-atomic="true">
        <h2 className="panel-title">
          {done ? 'Your narrated presentation is ready' : 'Generating narrated presentation…'}
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
          <div className="done-icon" aria-hidden="true">
            <svg width="48" height="48" viewBox="0 0 48 48" fill="none">
              <circle cx="24" cy="24" r="22" fill="var(--color-violet-light)" />
              <path d="M14 24l7 7 13-14" stroke="var(--color-violet)" strokeWidth="3"
                strokeLinecap="round" strokeLinejoin="round" />
            </svg>
          </div>

          <div className="done-actions">
            <a
              href={downloadUrl}
              download="narrated_presentation.pptx"
              className="btn btn--primary"
              data-testid="download-link"
            >
              <span aria-hidden="true">↓ </span>Download PPTX
            </a>

            {config.enable_quality_check && (
              <button
                className="btn btn--secondary"
                data-testid="btn-quality-check"
                onClick={onQualityCheck}
              >
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
                {exportingVideo ? videoLabel || 'Exporting video…' : 'Export as MP4'}
              </button>
            )}

            {mp4Url && (
              <a
                href={mp4Url}
                download="narrated_presentation.mp4"
                className="btn btn--secondary"
                data-testid="download-video-link"
              >
                <span aria-hidden="true">↓ </span>Download MP4
              </a>
            )}

            {videoError && (
              <p className="alert alert--error video-error">{videoError}</p>
            )}

            {exportingVideo && (
              <div className="video-progress" aria-live="polite" aria-atomic="false">
                <ProgressBar value={videoProgress} label={videoLabel} />
              </div>
            )}

            <button className="btn btn--ghost" onClick={onRestart}>
              Start over
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
