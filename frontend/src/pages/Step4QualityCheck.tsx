import { useState } from 'react';
import type { QualityCheckResult, WizardState } from '../types';
import { runQualityCheck } from '../api/narrationApi';
import QualityResults from '../components/QualityResults';
import FileUploadCard from '../components/FileUploadCard';
import VoiceSelector from '../components/VoiceSelector';

interface Props {
  state: WizardState;
  onBack: () => void;
  onRestart: () => void;
}

export default function Step4QualityCheck({ state, onBack, onRestart }: Props) {
  const [qcScript, setQcScript] = useState<File | null>(state.scriptFile);
  const [qcPptx, setQcPptx] = useState<File | null>(null);
  const [qcVoice, setQcVoice] = useState(state.voice);
  const [running, setRunning] = useState(false);
  const [runLabel, setRunLabel] = useState('');
  const [results, setResults] = useState<QualityCheckResult[] | null>(null);
  const [error, setError] = useState('');

  const canRun = !!(qcScript && qcPptx) && !running;

  const handleRun = async () => {
    if (!qcScript || !qcPptx) return;
    setError('');
    setResults(null);
    setRunning(true);
    setRunLabel('Transcribing audio and comparing to script…');

    const fd = new FormData();
    fd.append('script', qcScript);
    fd.append('pptx', qcPptx);
    fd.append('voice', qcVoice);

    try {
      const data = await runQualityCheck(fd);
      setResults(data);
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'Quality check failed');
    } finally {
      setRunning(false);
      setRunLabel('');
    }
  };

  return (
    <div className="panel" data-testid="step-4">
      <div className="panel-header">
        <h2 className="panel-title">Quality Check</h2>
        <p className="panel-subtitle">
          Compare the embedded audio transcriptions against your original script using Azure STT + GPT.
        </p>
      </div>

      <div className="upload-grid">
        <FileUploadCard
          label="Original Script"
          hint=".docx or .pptx narration script"
          accept=".docx,.pptx"
          file={qcScript}
          testId="qc-script-upload"
          onFile={setQcScript}
        />
        <FileUploadCard
          label="Narrated PPTX"
          hint=".pptx file with embedded audio"
          accept=".pptx"
          file={qcPptx}
          testId="qc-pptx-upload"
          onFile={setQcPptx}
        />
      </div>

      <VoiceSelector value={qcVoice} onChange={setQcVoice} disabled={running} />

      {error && (
        <div className="alert alert--error" role="alert" data-testid="qc-error">
          {error}
        </div>
      )}

      {running && (
        <div className="qc-running" data-testid="qc-running" aria-live="polite">
          <span className="spinner" aria-hidden="true" />
          {runLabel}
        </div>
      )}

      {results && <QualityResults results={results} />}

      <div className="panel-actions panel-actions--split">
        <button className="btn btn--ghost" onClick={onBack}>← Back</button>
        <div className="btn-group">
          <button
            className="btn btn--primary"
            disabled={!canRun}
            data-testid="btn-run-qc"
            onClick={handleRun}
          >
            {running ? (
              <><span className="spinner" aria-hidden="true" /> Running…</>
            ) : 'Run Quality Check'}
          </button>
          <button className="btn btn--ghost" onClick={onRestart}>Start over</button>
        </div>
      </div>
    </div>
  );
}
