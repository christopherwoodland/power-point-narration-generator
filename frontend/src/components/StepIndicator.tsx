import type { WizardStep } from '../types';

const ALL_STEPS = [
  { num: 1 as WizardStep, label: 'Upload' },
  { num: 2 as WizardStep, label: 'Review' },
  { num: 3 as WizardStep, label: 'Generate' },
  { num: 4 as WizardStep, label: 'Quality Check' },
];

interface Props {
  current: WizardStep;
  showQualityCheck?: boolean;
  generateDone?: boolean;
}

export default function StepIndicator({ current, showQualityCheck = true, generateDone = false }: Props) {
  const STEPS = showQualityCheck ? ALL_STEPS : ALL_STEPS.filter(s => s.num !== 4);
  return (
    <nav className="step-indicator" aria-label="Wizard steps">
      <ol className="step-list">
        {STEPS.map((step, idx) => {
          let status =
            step.num < current ? 'done' :
            step.num === current ? 'active' : 'pending';
          // Mark Generate step as done when generation is complete
          if (step.num === 3 && current === 3 && generateDone) status = 'done';
          return (
            <li
              key={step.num}
              className={`step-item step-${status}`}
              aria-current={status === 'active' ? 'step' : undefined}
            >
              <div className={`step-circle${status === 'done' ? ' step-circle--green' : ''}`} aria-hidden="true">
                {status === 'done' ? (
                  <svg width="14" height="14" viewBox="0 0 14 14" fill="none">
                    <path d="M2.5 7l3 3 6-6" stroke="currentColor" strokeWidth="2"
                      strokeLinecap="round" strokeLinejoin="round" />
                  </svg>
                ) : step.num}
              </div>
              <span className="step-label">
                {step.label}
                <span className="sr-only">
                  {status === 'done' ? ' – completed' : status === 'active' ? ' – current step' : ''}
                </span>
              </span>
              {idx < STEPS.length - 1 && <div className="step-connector" aria-hidden="true" />}
            </li>
          );
        })}
      </ol>
    </nav>
  );
}
