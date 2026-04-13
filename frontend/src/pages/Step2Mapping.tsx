import type { WizardState } from '../types';
import SlideMappingTable from '../components/SlideMappingTable';

interface Props {
  state: WizardState;
  onMappingChange: (mapping: Record<number, number>) => void;
  onBack: () => void;
  onNext: () => void;
}

export default function Step2Mapping({ state, onMappingChange, onBack, onNext }: Props) {
  const { parsedSlides, pptxSlideCount, aiMode, slideMapping } = state;
  const diff = parsedSlides.length - pptxSlideCount;

  return (
    <div className="panel" data-testid="step-2">
      <div className="panel-header">
        <h2 className="panel-title">
          {aiMode ? 'Review slides for AI generation' : 'Review slide mapping'}
        </h2>
        <p className="panel-subtitle">
          {aiMode
            ? 'AI will build one slide per script section.'
            : 'Verify or adjust which script section maps to each PPTX slide.'}
        </p>
      </div>

      <div className="counts-bar">
        <span className="count-chip">
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
            <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
            <polyline points="14 2 14 8 20 8" />
          </svg>
          Script slides: <strong>{parsedSlides.length}</strong>
        </span>
        {!aiMode && (
          <span className="count-chip">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <rect x="2" y="3" width="20" height="14" rx="2" />
              <line x1="8" y1="21" x2="16" y2="21" />
              <line x1="12" y1="17" x2="12" y2="21" />
            </svg>
            PPTX slides: <strong>{pptxSlideCount}</strong>
          </span>
        )}
      </div>

      {!aiMode && diff !== 0 && (
        <div className="alert alert--warn" role="alert" data-testid="mismatch-banner">
          {diff > 0
            ? `Your script has ${diff} more section(s) than the PowerPoint. Unmapped sections will be skipped.`
            : `Your PowerPoint has ${Math.abs(diff)} more slide(s) than the script.`}
        </div>
      )}

      <div className="table-wrap">
        <SlideMappingTable
          slides={parsedSlides}
          pptxSlideCount={pptxSlideCount}
          aiMode={aiMode}
          mapping={slideMapping}
          onChange={onMappingChange}
        />
      </div>

      <div className="panel-actions panel-actions--split">
        <button className="btn btn--ghost" onClick={onBack}><span aria-hidden="true">← </span>Back</button>
        <button
          className="btn btn--primary"
          data-testid="btn-next-step2"
          onClick={onNext}
        >
          {aiMode
            ? <><span aria-hidden="true">❖ </span>Generate AI Presentation<span aria-hidden="true"> →</span></>
            : <>Generate Narrated PPTX<span aria-hidden="true"> →</span></>}
        </button>
      </div>
    </div>
  );
}
