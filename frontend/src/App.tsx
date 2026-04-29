import { useEffect, useState } from 'react';
import type { AppConfig, WizardState, WizardStep } from './types';
import { fetchConfig } from './api/narrationApi';
import Header from './components/Header';
import StepIndicator from './components/StepIndicator';
import Step1Upload from './pages/Step1Upload';
import Step2Mapping from './pages/Step2Mapping';
import Step3Generate from './pages/Step3Generate';
import Step4QualityCheck from './pages/Step4QualityCheck';

const DEFAULT_CONFIG: AppConfig = {
  enable_quality_check: true,
  enable_ai_mode: true,
  enable_video_export: true,
  default_single_pptx_mode: false,
  banner_message: '',
  upload_files_message: 'Provide a narration script and (optionally) a PowerPoint to narrate.',
  tts_mode: 'standard',
};

const DEFAULT_STATE: WizardState = {
  step: 1,
  scriptFile: null,
  pptxFile: null,
  voice: 'en-US-Grant:MAI-Voice-1',
  aiMode: false,
  parsedSlides: [],
  pptxSlideCount: 0,
  slideMapping: {},
  resultBytes: null,
  config: DEFAULT_CONFIG,
};

export default function App() {
  const [state, setState] = useState<WizardState>(DEFAULT_STATE);
  const [config, setConfig] = useState<AppConfig>(DEFAULT_CONFIG);

  useEffect(() => {
    fetchConfig().then(cfg => {
      setConfig(cfg);
      setState(s => ({ ...s, config: cfg }));
    });
  }, []);

  const goTo = (step: WizardStep) => setState(s => ({ ...s, step }));

  const reset = () => setState({ ...DEFAULT_STATE, config });

  return (
    <div className="app-shell">
      <a href="#main-content" className="skip-nav">Skip to main content</a>
      {config.banner_message && (
        <div className="env-banner" role="status">{config.banner_message}</div>
      )}
      <Header />
      <StepIndicator current={state.step} showQualityCheck={config.enable_quality_check} generateDone={state.resultBytes !== null} />

      <main id="main-content" className="main-content">
        {state.step === 1 && (
          <Step1Upload
            state={state}
            config={config}
            onChange={patch => setState(s => ({ ...s, ...patch }))}
            onNext={(slides, pptxCount, mapping) =>
              setState(s => ({
                ...s,
                step: 2,
                parsedSlides: slides,
                pptxSlideCount: pptxCount,
                slideMapping: mapping,
              }))
            }
          />
        )}
        {state.step === 2 && (
          <Step2Mapping
            state={state}
            onMappingChange={mapping => setState(s => ({ ...s, slideMapping: mapping }))}
            onSlideTextChange={(idx, text) =>
              setState(s => ({
                ...s,
                parsedSlides: s.parsedSlides.map((slide, i) =>
                  i === idx ? { ...slide, text } : slide
                ),
              }))
            }
            onBack={() => goTo(1)}
            onNext={() => goTo(3)}
          />
        )}
        {state.step === 3 && (
          <Step3Generate
            state={state}
            config={config}
            onResultReady={bytes => setState(s => ({ ...s, resultBytes: bytes }))}
            onQualityCheck={() => goTo(4)}
            onRestart={reset}
          />
        )}
        {state.step === 4 && (
          <Step4QualityCheck
            state={state}
            onBack={() => goTo(3)}
            onRestart={reset}
          />
        )}
      </main>
    </div>
  );
}
