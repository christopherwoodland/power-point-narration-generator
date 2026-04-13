import { useEffect, useRef, useState } from 'react';
import type { AppConfig, SlideInfo, WizardState } from '../types';
import { parseScript } from '../api/narrationApi';
import FileUploadCard from '../components/FileUploadCard';
import VoiceSelector from '../components/VoiceSelector';

interface Props {
  state: WizardState;
  config: AppConfig;
  onChange: (patch: Partial<WizardState>) => void;
  onNext: (slides: SlideInfo[], pptxCount: number, mapping: Record<number, number>) => void;
}

export default function Step1Upload({ state, config, onChange, onNext }: Props) {
  const panelRef = useRef<HTMLDivElement>(null);
  useEffect(() => { panelRef.current?.focus(); }, []);

  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  const canProceed = state.aiMode ? !!state.scriptFile : !!(state.scriptFile && state.pptxFile);

  const handleNext = async () => {
    if (!state.scriptFile) return;
    setError('');
    setLoading(true);

    const fd = new FormData();
    fd.append('script', state.scriptFile);
    if (!state.aiMode && state.pptxFile) fd.append('pptx', state.pptxFile);
    fd.append('ai_mode', state.aiMode ? 'true' : 'false');

    try {
      const data = await parseScript(fd);
      const defaultMapping: Record<number, number> = {};
      const n = Math.min(data.slides.length, data.pptxSlideCount);
      for (let i = 0; i < n; i++) defaultMapping[i] = i;
      onNext(data.slides, data.pptxSlideCount, defaultMapping);
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'Parse failed');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div ref={panelRef} tabIndex={-1} className="panel" data-testid="step-1">
      <div className="panel-header">
        <h2 className="panel-title">Upload your files</h2>
        <p className="panel-subtitle">
          Provide a narration script and (optionally) a PowerPoint to narrate.
        </p>
      </div>

      {config.enable_ai_mode && (
        <label className="toggle-row">
          <div className="toggle-wrap">
            <input
              type="checkbox"
              className="toggle-input"
              data-testid="ai-mode-toggle"
              checked={state.aiMode}
              onChange={e => onChange({ aiMode: e.target.checked, pptxFile: null })}
            />
            <span className="toggle-track" />
          </div>
          <span className="toggle-label">
            <strong>Generate presentation with AI</strong>
            <span className="toggle-hint">
              AI builds slides + images from your script — no PPTX needed
            </span>
          </span>
        </label>
      )}

      <div className="upload-grid">
        <FileUploadCard
          label="Narration Script"
          hint=".docx or .pptx — use Heading 1 as slide delimiters"
          accept=".docx,.pptx"
          file={state.scriptFile}
          testId="script-upload"
          onFile={f => onChange({ scriptFile: f })}
        />
        <FileUploadCard
          label={state.aiMode ? 'PowerPoint (optional)' : 'PowerPoint Presentation'}
          hint={state.aiMode
            ? 'AI will generate slides — or upload your own template'
            : '.pptx — the presentation to narrate'}
          accept=".pptx"
          file={state.pptxFile}
          disabled={state.aiMode && !state.pptxFile}
          testId="pptx-upload"
          onFile={f => onChange({ pptxFile: f })}
        />
      </div>

      <VoiceSelector
        value={state.voice}
        onChange={v => onChange({ voice: v })}
        disabled={loading}
      />

      {error && (
        <div className="alert alert--error" role="alert" data-testid="parse-error">
          {error}
        </div>
      )}

      <div className="panel-actions">
        <button
          className="btn btn--primary"
          disabled={!canProceed || loading}
          aria-busy={loading}
          data-testid="btn-next-step1"
          onClick={handleNext}
        >
          {loading ? (
            <>
              <span className="spinner" aria-hidden="true" /> Parsing…
            </>
          ) : (
            <>Next: Review Slides<span aria-hidden="true"> →</span></>
          )}
        </button>
      </div>
    </div>
  );
}
