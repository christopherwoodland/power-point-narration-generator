export interface SlideInfo {
  title: string;
  text: string;
}

export interface ParseResponse {
  slides: SlideInfo[];
  pptxSlideCount: number;
  wordSlideCount: number;
  aiMode: boolean;
}

export interface QualityCheckResult {
  slide_num: number;
  title: string;
  confidence: number;
  issues: string[];
}

export interface AppConfig {
  enable_quality_check: boolean;
  enable_ai_mode: boolean;
  enable_video_export: boolean;
  banner_message: string;
}

export interface ProgressEvent {
  type: 'progress' | 'done' | 'error';
  slide?: number;
  total?: number;
  phase?: string;
  message?: string;
  pptx?: string;   // base64
  mp4?: string;    // base64
}

export type WizardStep = 1 | 2 | 3 | 4;

export interface WizardState {
  step: WizardStep;
  scriptFile: File | null;
  pptxFile: File | null;
  voice: string;
  aiMode: boolean;
  parsedSlides: SlideInfo[];
  pptxSlideCount: number;
  slideMapping: Record<number, number>;
  resultBytes: Uint8Array | null;
  config: AppConfig;
}
