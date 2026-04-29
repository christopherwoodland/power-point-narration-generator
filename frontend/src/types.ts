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
  default_single_pptx_mode: boolean;
  banner_message: string;
  upload_files_message: string;
  tts_mode: string;
  // System-wide branding (from server)
  app_name: string;
  logo_url: string;
  primary_color: string;
  primary_color_dark: string;
  primary_color_light: string;
  accent_color: string;
  enabled_voices: string[];
}

export interface ProgressEvent {
  type: 'progress' | 'done' | 'error';
  slide?: number;
  total?: number;
  phase?: string;
  message?: string;
  pptx?: string; // base64
  mp4?: string; // base64
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
