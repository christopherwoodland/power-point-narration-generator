import type { AppConfig, ParseResponse, QualityCheckResult } from '../types';
import type { AdminSettings } from '../context/AdminContext';

const BASE = '';

const CONFIG_FALLBACK: AppConfig = {
  enable_quality_check: true,
  enable_ai_mode: true,
  enable_video_export: true,
  default_single_pptx_mode: false,
  banner_message: '',
  tts_mode: 'standard',
  upload_files_message: '',
  app_name: 'GAO Text to Speech',
  logo_url: '',
  primary_color: '#004d2f',
  primary_color_dark: '#003320',
  primary_color_light: '#e6f4ee',
  accent_color: '#007a4d',
  enabled_voices: [],
};

export async function fetchConfig(): Promise<AppConfig> {
  const res = await fetch(`${BASE}/api/config`);
  if (!res.ok) return CONFIG_FALLBACK;
  return res.json();
}

export async function saveAdminSettings(
  settings: AdminSettings,
): Promise<void> {
  const body = {
    appName: settings.appName,
    logoUrl: settings.logoUrl,
    primaryColor: settings.primaryColor,
    primaryColorDark: settings.primaryColorDark,
    primaryColorLight: settings.primaryColorLight,
    accentColor: settings.accentColor,
    enabledVoices: settings.enabledVoices,
  };
  const res = await fetch(`${BASE}/api/admin/settings`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
  if (!res.ok) throw new Error(`Failed to save settings: ${res.statusText}`);
}

export async function parseScript(formData: FormData): Promise<ParseResponse> {
  const res = await fetch(`${BASE}/api/parse`, {
    method: 'POST',
    body: formData,
  });
  if (!res.ok) {
    const err = await res.json().catch(() => ({ detail: res.statusText }));
    const detail = Array.isArray(err.detail)
      ? err.detail
          .map(
            (e: { loc?: string[]; msg: string }) =>
              `${e.loc?.join('.')} — ${e.msg}`,
          )
          .join('; ')
      : (err.detail ?? res.statusText);
    throw new Error(detail);
  }
  return res.json();
}

export async function processNarration(formData: FormData): Promise<Blob> {
  const res = await fetch(`${BASE}/api/process`, {
    method: 'POST',
    body: formData,
  });
  if (!res.ok) {
    const err = await res.json().catch(() => ({ detail: res.statusText }));
    throw new Error(err.detail ?? res.statusText);
  }
  return res.blob();
}

export async function* streamAiGeneration(
  formData: FormData,
): AsyncGenerator<import('../types').ProgressEvent> {
  const res = await fetch(`${BASE}/api/generate-ai`, {
    method: 'POST',
    body: formData,
  });
  if (!res.ok) throw new Error(`HTTP ${res.status}`);

  const reader = res.body!.getReader();
  const decoder = new TextDecoder();
  let buffer = '';

  while (true) {
    const { done, value } = await reader.read();
    if (done) break;
    buffer += decoder.decode(value, { stream: true });
    const lines = buffer.split('\n');
    buffer = lines.pop() ?? '';
    for (const line of lines) {
      if (line.trim()) yield JSON.parse(line);
    }
  }
}

export async function* streamVideoExport(
  formData: FormData,
): AsyncGenerator<import('../types').ProgressEvent> {
  const res = await fetch(`${BASE}/api/export-video`, {
    method: 'POST',
    body: formData,
  });
  if (!res.ok) throw new Error(`HTTP ${res.status}`);

  const reader = res.body!.getReader();
  const decoder = new TextDecoder();
  let buffer = '';

  while (true) {
    const { done, value } = await reader.read();
    if (done) break;
    buffer += decoder.decode(value, { stream: true });
    const lines = buffer.split('\n');
    buffer = lines.pop() ?? '';
    for (const line of lines) {
      if (line.trim()) yield JSON.parse(line);
    }
  }
}

export async function runQualityCheck(
  formData: FormData,
): Promise<QualityCheckResult[]> {
  const res = await fetch(`${BASE}/api/quality-check`, {
    method: 'POST',
    body: formData,
  });
  if (!res.ok) {
    const err = await res.json().catch(() => ({ detail: res.statusText }));
    throw new Error(err.detail ?? res.statusText);
  }
  const data = await res.json();
  return data.results;
}
