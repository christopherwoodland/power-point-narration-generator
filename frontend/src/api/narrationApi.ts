import type { AppConfig, ParseResponse, QualityCheckResult } from '../types';

const BASE = '';

export async function fetchConfig(): Promise<AppConfig> {
  const res = await fetch(`${BASE}/api/config`);
  if (!res.ok) {
    return {
      enable_quality_check: true,
      enable_ai_mode: true,
      enable_video_export: true,
      banner_message: '',
    };
  }
  return res.json();
}

export async function parseScript(formData: FormData): Promise<ParseResponse> {
  const res = await fetch(`${BASE}/api/parse`, { method: 'POST', body: formData });
  if (!res.ok) {
    const err = await res.json().catch(() => ({ detail: res.statusText }));
    const detail = Array.isArray(err.detail)
      ? err.detail.map((e: { loc?: string[]; msg: string }) => `${e.loc?.join('.')} — ${e.msg}`).join('; ')
      : err.detail ?? res.statusText;
    throw new Error(detail);
  }
  return res.json();
}

export async function processNarration(formData: FormData): Promise<Blob> {
  const res = await fetch(`${BASE}/api/process`, { method: 'POST', body: formData });
  if (!res.ok) {
    const err = await res.json().catch(() => ({ detail: res.statusText }));
    throw new Error(err.detail ?? res.statusText);
  }
  return res.blob();
}

export async function* streamAiGeneration(
  formData: FormData
): AsyncGenerator<import('../types').ProgressEvent> {
  const res = await fetch(`${BASE}/api/generate-ai`, { method: 'POST', body: formData });
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
  formData: FormData
): AsyncGenerator<import('../types').ProgressEvent> {
  const res = await fetch(`${BASE}/api/export-video`, { method: 'POST', body: formData });
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
  formData: FormData
): Promise<QualityCheckResult[]> {
  const res = await fetch(`${BASE}/api/quality-check`, { method: 'POST', body: formData });
  if (!res.ok) {
    const err = await res.json().catch(() => ({ detail: res.statusText }));
    throw new Error(err.detail ?? res.statusText);
  }
  const data = await res.json();
  return data.results;
}
