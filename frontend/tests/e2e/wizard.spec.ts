import { test, expect, type Page } from '@playwright/test';
import path from 'path';

// ── Mock API helpers ───────────────────────────────────────────────────────

async function mockConfig(page: Page, overrides: Record<string, unknown> = {}) {
  await page.route('/api/config', route =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        enable_quality_check: true,
        enable_ai_mode: true,
        enable_video_export: true,
        banner_message: '',
        ...overrides,
      }),
    })
  );
}

async function mockParse(page: Page, slides = [
  { title: 'Slide 1', text: 'Body text for slide one.' },
  { title: 'Slide 2', text: 'Body text for slide two.' },
], pptxSlideCount = 2) {
  await page.route('/api/parse', route =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        slides,
        pptxSlideCount,
        wordSlideCount: slides.length,
        aiMode: false,
      }),
    })
  );
}

async function mockProcess(page: Page) {
  await page.route('/api/process', route =>
    route.fulfill({
      status: 200,
      contentType: 'application/vnd.openxmlformats-officedocument.presentationml.presentation',
      body: Buffer.from('FAKE_PPTX_BYTES'),
    })
  );
}

async function mockQualityCheck(page: Page) {
  await page.route('/api/quality-check', route =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        results: [
          { slide_num: 1, title: 'Slide 1', confidence: 0.95, issues: [] },
          { slide_num: 2, title: 'Slide 2', confidence: 0.72, issues: ['Some words were unclear'] },
        ],
      }),
    })
  );
}

// Create minimal fake files for upload
function fakeDocx(): Buffer { return Buffer.from('PK\x03\x04FAKE_DOCX'); }
function fakePptx(): Buffer { return Buffer.from('PK\x03\x04FAKE_PPTX'); }

// ── Tests ──────────────────────────────────────────────────────────────────

test.describe('Step 1 — Upload', () => {
  test.beforeEach(async ({ page }) => {
    await mockConfig(page);
    await page.goto('/');
  });

  test('shows the app header and Step 1 panel', async ({ page }) => {
    await expect(page.locator('.header-title')).toContainText('PowerPoint Narration Generator');
    await expect(page.getByTestId('step-1')).toBeVisible();
  });

  test('Next button is disabled when no files are selected', async ({ page }) => {
    await expect(page.getByTestId('btn-next-step1')).toBeDisabled();
  });

  test('Next button enables after both files are selected', async ({ page }) => {
    await mockParse(page);

    // Upload script file
    const scriptInput = page.locator('[data-testid="script-upload"] input[type="file"]');
    await scriptInput.setInputFiles({
      name: 'script.docx',
      mimeType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
      buffer: fakeDocx(),
    });

    // Upload PPTX
    const pptxInput = page.locator('[data-testid="pptx-upload"] input[type="file"]');
    await pptxInput.setInputFiles({
      name: 'deck.pptx',
      mimeType: 'application/vnd.openxmlformats-officedocument.presentationml.presentation',
      buffer: fakePptx(),
    });

    await expect(page.getByTestId('btn-next-step1')).toBeEnabled();
  });

  test('AI mode toggle hides required PPTX constraint', async ({ page }) => {
    const toggle = page.getByTestId('ai-mode-toggle');
    await toggle.check();

    // With AI mode on, only script is needed
    const scriptInput = page.locator('[data-testid="script-upload"] input[type="file"]');
    await scriptInput.setInputFiles({
      name: 'script.docx',
      mimeType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
      buffer: fakeDocx(),
    });

    await expect(page.getByTestId('btn-next-step1')).toBeEnabled();
  });

  test('shows AI mode toggle when enable_ai_mode is true', async ({ page }) => {
    await expect(page.getByTestId('ai-mode-toggle')).toBeVisible();
  });

  test('hides AI mode toggle when enable_ai_mode is false', async ({ page }) => {
    await mockConfig(page, { enable_ai_mode: false });
    await page.goto('/');
    await expect(page.getByTestId('ai-mode-toggle')).not.toBeVisible();
  });

  test('voice selector is present', async ({ page }) => {
    await expect(page.locator('#voice-select')).toBeVisible();
  });

  test('shows parse error when API returns error', async ({ page }) => {
    await page.route('/api/parse', route =>
      route.fulfill({
        status: 422,
        contentType: 'application/json',
        body: JSON.stringify({ detail: 'File appears corrupted.' }),
      })
    );

    const scriptInput = page.locator('[data-testid="script-upload"] input[type="file"]');
    await scriptInput.setInputFiles({
      name: 'bad.docx',
      mimeType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
      buffer: Buffer.from('NOT_VALID'),
    });
    const pptxInput = page.locator('[data-testid="pptx-upload"] input[type="file"]');
    await pptxInput.setInputFiles({
      name: 'deck.pptx',
      mimeType: 'application/vnd.openxmlformats-officedocument.presentationml.presentation',
      buffer: fakePptx(),
    });

    await page.getByTestId('btn-next-step1').click();
    await expect(page.getByTestId('parse-error')).toContainText('File appears corrupted.');
  });
});

test.describe('Step 2 — Slide Mapping', () => {
  async function goToStep2(page: Page) {
    await mockConfig(page);
    await mockParse(page);
    await page.goto('/');

    const scriptInput = page.locator('[data-testid="script-upload"] input[type="file"]');
    await scriptInput.setInputFiles({
      name: 'script.docx',
      mimeType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
      buffer: fakeDocx(),
    });
    const pptxInput = page.locator('[data-testid="pptx-upload"] input[type="file"]');
    await pptxInput.setInputFiles({
      name: 'deck.pptx',
      mimeType: 'application/vnd.openxmlformats-officedocument.presentationml.presentation',
      buffer: fakePptx(),
    });

    await page.getByTestId('btn-next-step1').click();
    await expect(page.getByTestId('step-2')).toBeVisible();
  }

  test('shows slide mapping table with correct rows', async ({ page }) => {
    await goToStep2(page);
    const rows = page.locator('.mapping-table tbody tr');
    await expect(rows).toHaveCount(2);
  });

  test('shows mismatch banner when counts differ', async ({ page }) => {
    await mockConfig(page);
    await mockParse(page, [
      { title: 'Slide 1', text: 'Text 1' },
      { title: 'Slide 2', text: 'Text 2' },
      { title: 'Slide 3', text: 'Text 3' },
    ], 2);
    await page.goto('/');

    const scriptInput = page.locator('[data-testid="script-upload"] input[type="file"]');
    await scriptInput.setInputFiles({
      name: 'script.docx',
      mimeType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
      buffer: fakeDocx(),
    });
    const pptxInput = page.locator('[data-testid="pptx-upload"] input[type="file"]');
    await pptxInput.setInputFiles({
      name: 'deck.pptx',
      mimeType: 'application/vnd.openxmlformats-officedocument.presentationml.presentation',
      buffer: fakePptx(),
    });

    await page.getByTestId('btn-next-step1').click();
    await expect(page.getByTestId('mismatch-banner')).toBeVisible();
  });

  test('back button returns to step 1', async ({ page }) => {
    await goToStep2(page);
    await page.locator('button', { hasText: '← Back' }).click();
    await expect(page.getByTestId('step-1')).toBeVisible();
  });

  test('can proceed to step 3', async ({ page }) => {
    await goToStep2(page);
    await mockProcess(page);
    await page.getByTestId('btn-next-step2').click();
    await expect(page.getByTestId('step-3')).toBeVisible();
  });
});

test.describe('Step 3 — Generate', () => {
  async function goToStep3(page: Page) {
    await mockConfig(page);
    await mockParse(page);
    await mockProcess(page);
    await page.goto('/');

    const scriptInput = page.locator('[data-testid="script-upload"] input[type="file"]');
    await scriptInput.setInputFiles({
      name: 'script.docx',
      mimeType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
      buffer: fakeDocx(),
    });
    const pptxInput = page.locator('[data-testid="pptx-upload"] input[type="file"]');
    await pptxInput.setInputFiles({
      name: 'deck.pptx',
      mimeType: 'application/vnd.openxmlformats-officedocument.presentationml.presentation',
      buffer: fakePptx(),
    });

    await page.getByTestId('btn-next-step1').click();
    await page.getByTestId('btn-next-step2').click();
    await expect(page.getByTestId('step-3')).toBeVisible();
  }

  test('shows done state and download link after generation', async ({ page }) => {
    await goToStep3(page);
    await expect(page.getByTestId('done-wrap')).toBeVisible({ timeout: 10_000 });
    await expect(page.getByTestId('download-link')).toBeVisible();
  });

  test('shows quality check button when enabled', async ({ page }) => {
    await goToStep3(page);
    await expect(page.getByTestId('done-wrap')).toBeVisible({ timeout: 10_000 });
    await expect(page.getByTestId('btn-quality-check')).toBeVisible();
  });

  test('hides quality check button when disabled', async ({ page }) => {
    await mockConfig(page, { enable_quality_check: false });
    // Re-navigate with feature off
    await mockParse(page);
    await mockProcess(page);
    await page.goto('/');

    const scriptInput = page.locator('[data-testid="script-upload"] input[type="file"]');
    await scriptInput.setInputFiles({
      name: 'script.docx',
      mimeType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
      buffer: fakeDocx(),
    });
    const pptxInput = page.locator('[data-testid="pptx-upload"] input[type="file"]');
    await pptxInput.setInputFiles({
      name: 'deck.pptx',
      mimeType: 'application/vnd.openxmlformats-officedocument.presentationml.presentation',
      buffer: fakePptx(),
    });

    await page.getByTestId('btn-next-step1').click();
    await page.getByTestId('btn-next-step2').click();
    await expect(page.getByTestId('done-wrap')).toBeVisible({ timeout: 10_000 });
    await expect(page.getByTestId('btn-quality-check')).not.toBeVisible();
  });

  test('shows error when generation fails', async ({ page }) => {
    await mockConfig(page);
    await mockParse(page);
    await page.route('/api/process', route =>
      route.fulfill({
        status: 502,
        contentType: 'application/json',
        body: JSON.stringify({ detail: 'TTS service unavailable' }),
      })
    );
    await page.goto('/');

    const scriptInput = page.locator('[data-testid="script-upload"] input[type="file"]');
    await scriptInput.setInputFiles({
      name: 'script.docx', mimeType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
      buffer: fakeDocx(),
    });
    const pptxInput = page.locator('[data-testid="pptx-upload"] input[type="file"]');
    await pptxInput.setInputFiles({
      name: 'deck.pptx', mimeType: 'application/vnd.openxmlformats-officedocument.presentationml.presentation',
      buffer: fakePptx(),
    });

    await page.getByTestId('btn-next-step1').click();
    await page.getByTestId('btn-next-step2').click();
    await expect(page.getByTestId('gen-error')).toBeVisible({ timeout: 10_000 });
  });

  test('shows Export as MP4 button when video export is enabled', async ({ page }) => {
    await goToStep3(page);
    await expect(page.getByTestId('done-wrap')).toBeVisible({ timeout: 10_000 });
    await expect(page.getByTestId('btn-export-video')).toBeVisible();
  });

  test('hides Export as MP4 button when video export is disabled', async ({ page }) => {
    await mockConfig(page, { enable_video_export: false });
    await mockParse(page);
    await mockProcess(page);
    await page.goto('/');

    const scriptInput = page.locator('[data-testid="script-upload"] input[type="file"]');
    await scriptInput.setInputFiles({
      name: 'script.docx', mimeType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
      buffer: fakeDocx(),
    });
    const pptxInput = page.locator('[data-testid="pptx-upload"] input[type="file"]');
    await pptxInput.setInputFiles({
      name: 'deck.pptx', mimeType: 'application/vnd.openxmlformats-officedocument.presentationml.presentation',
      buffer: fakePptx(),
    });

    await page.getByTestId('btn-next-step1').click();
    await page.getByTestId('btn-next-step2').click();
    await expect(page.getByTestId('done-wrap')).toBeVisible({ timeout: 10_000 });
    await expect(page.getByTestId('btn-export-video')).not.toBeVisible();
  });

  test('video export completes and shows download link', async ({ page }) => {
    // Minimal valid base64 MP4 (not playable, but sufficient for the URL object test)
    const fakeBase64Mp4 = Buffer.from('FAKE_MP4').toString('base64');
    const ndjsonResponse =
      '{"type":"progress","slide":0,"total":0,"message":"Rendering slides..."}\n' +
      `{"type":"done","mp4":"${fakeBase64Mp4}"}\n`;

    await mockConfig(page);
    await mockParse(page);
    await mockProcess(page);
    await page.route('/api/export-video', route =>
      route.fulfill({
        status: 200,
        contentType: 'application/x-ndjson',
        body: ndjsonResponse,
      })
    );
    await page.goto('/');

    const scriptInput = page.locator('[data-testid="script-upload"] input[type="file"]');
    await scriptInput.setInputFiles({
      name: 'script.docx', mimeType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
      buffer: fakeDocx(),
    });
    const pptxInput = page.locator('[data-testid="pptx-upload"] input[type="file"]');
    await pptxInput.setInputFiles({
      name: 'deck.pptx', mimeType: 'application/vnd.openxmlformats-officedocument.presentationml.presentation',
      buffer: fakePptx(),
    });

    await page.getByTestId('btn-next-step1').click();
    await page.getByTestId('btn-next-step2').click();
    await expect(page.getByTestId('done-wrap')).toBeVisible({ timeout: 10_000 });

    await page.getByTestId('btn-export-video').click();
    await expect(page.getByTestId('download-video-link')).toBeVisible({ timeout: 15_000 });
  });
});

test.describe('Step 4 — Quality Check', () => {
  test('runs quality check and shows results', async ({ page }) => {
    await mockConfig(page);
    await mockParse(page);
    await mockProcess(page);
    await mockQualityCheck(page);
    await page.goto('/');

    const scriptInput = page.locator('[data-testid="script-upload"] input[type="file"]');
    await scriptInput.setInputFiles({
      name: 'script.docx', mimeType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
      buffer: fakeDocx(),
    });
    const pptxInput = page.locator('[data-testid="pptx-upload"] input[type="file"]');
    await pptxInput.setInputFiles({
      name: 'deck.pptx', mimeType: 'application/vnd.openxmlformats-officedocument.presentationml.presentation',
      buffer: fakePptx(),
    });

    await page.getByTestId('btn-next-step1').click();
    await page.getByTestId('btn-next-step2').click();
    await expect(page.getByTestId('done-wrap')).toBeVisible({ timeout: 10_000 });
    await page.getByTestId('btn-quality-check').click();
    await expect(page.getByTestId('step-4')).toBeVisible();

    // Upload narrated PPTX for QC
    const qcPptxInput = page.locator('[data-testid="qc-pptx-upload"] input[type="file"]');
    await qcPptxInput.setInputFiles({
      name: 'narrated.pptx', mimeType: 'application/vnd.openxmlformats-officedocument.presentationml.presentation',
      buffer: fakePptx(),
    });

    await page.getByTestId('btn-run-qc').click();
    await expect(page.locator('.qc-results')).toBeVisible({ timeout: 15_000 });
    await expect(page.locator('.qc-table tbody tr')).toHaveCount(2);
  });
});
