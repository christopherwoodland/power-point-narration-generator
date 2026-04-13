# PowerPoint Narration Generator

An internal tool that generates narrated PowerPoint presentations from Word or PPTX scripts using Azure AI services.
Upload your script and an existing PowerPoint deck, map slides, and the tool synthesises natural-sounding narration audio per slide — embedding it directly into the PPTX file with auto-play on slide entry.

---

## Architecture

```
┌─────────────────────┐     HTTP (Vite proxy)      ┌──────────────────────────────┐
│  React / TypeScript  │ ─────────────────────────► │  ASP.NET Core 10 (C#)        │
│  Vite frontend       │                             │  PptxNarrator.Api            │
│  localhost:3000      │                             │  localhost:8080              │
└─────────────────────┘                             └──────────┬───────────────────┘
                                                               │ DefaultAzureCredential
                                          ┌────────────────────┼─────────────────────┐
                                          │                    │                     │
                                   Azure Speech          Azure OpenAI         Azure Translator
                                   (TTS / STT)        (GPT-4o + DALL-E 3)    (language adapt)
```

### Key technologies

| Layer | Technology |
|-------|-----------|
| Frontend | React 18 + TypeScript + Vite 5 |
| Backend | ASP.NET Core 10, .NET 10 |
| Auth | `DefaultAzureCredential` (no API keys) |
| TTS | Azure Cognitive Services Speech REST API |
| TTS voices | Standard Neural, MAI (multilingual), Dragon HD |
| AI slides | Azure OpenAI (GPT-4o + DALL-E 3) |
| Translation | Azure Translator |
| Quality check | Azure Speech STT round-trip |
| Video export | FFmpeg + PowerPoint COM (Windows) or LibreOffice (Linux) |
| Observability | Structured logging via `ILogger<T>`; optional Application Insights |
| Containers | Docker + Docker Compose |
| IaC | Azure Bicep (Container Apps) |

---

## Prerequisites

| Requirement | Notes |
|-------------|-------|
| [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) | `dotnet --version` should show `10.x` |
| [Node.js 20+](https://nodejs.org/) | `node --version` |
| [Azure CLI](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli) | Run `az login` before starting |
| Azure Cognitive Services resource | Speech TTS/STT endpoint |
| Azure OpenAI resource | Required for AI mode only |
| [FFmpeg](https://ffmpeg.org/download.html) on PATH | Required for video export only |
| Docker Desktop | For Docker Compose / container workflow only |

> **Auth note:** All Azure connections use `DefaultAzureCredential`. For local dev, `az login` is sufficient. In production (Container Apps) use Managed Identity — no secrets or API keys are used.

---

## Quick Start

### 1. Clone and configure

```powershell
git clone https://github.com/christopherwoodland/powerpoint-add-tool.git
cd powerpoint-add-tool
cp .env.example .env        # edit with your Azure resource names
```

### 2. Log in to Azure

```powershell
az login --tenant 16b3c013-d300-468d-ac64-7eda0820b6d3
```

### 3. Build the backend

```powershell
cd backend-csharp/src/PptxNarrator.Api
dotnet build
cd ../../..
```

### 4. Start both services

```powershell
.\scripts\run.ps1
```

Then open **http://localhost:3000** in your browser.  
Swagger UI is available at **http://localhost:8080/swagger**.

---

## Running with Docker Compose

Builds both images and starts them together:

```powershell
.\scripts\run-docker.ps1

# Or skip the build step:
.\scripts\run-docker.ps1 -NoBuild

# Run in background:
.\scripts\run-docker.ps1 -Detach
```

| URL | Service |
|-----|---------|
| http://localhost:3000 | React frontend |
| http://localhost:8080 | C# backend / Swagger |

---

## Configuration

Copy `.env.example` to `.env` and fill in your values. All settings are optional unless noted:

```env
# ── Feature flags ──────────────────────────────────────────────────────────────
ENABLE_QUALITY_CHECK=true       # Run STT quality check on generated audio
ENABLE_AI_MODE=true             # AI slide generation (requires Azure OpenAI)
ENABLE_VIDEO_EXPORT=true        # Export narrated PPTX to MP4 (requires ffmpeg)

# ── Azure Speech (required) ────────────────────────────────────────────────────
AZURE_SPEECH_RESOURCE_NAME=your-speech-resource-name
AZURE_SPEECH_REGION=eastus2

# ── Azure OpenAI (required for AI mode) ───────────────────────────────────────
AZURE_OPENAI_ENDPOINT=https://your-resource.openai.azure.com/
AZURE_OPENAI_DEPLOYMENT=gpt-4o
AZURE_IMAGE_DEPLOYMENT=dall-e-3

# ── Azure Translator (optional – enables voice-language matching) ──────────────
AZURE_TRANSLATOR_ENDPOINT=https://api.cognitive.microsofttranslator.com/

# ── Azure Document Intelligence (optional – richer PPTX parsing) ──────────────
AZURE_DOC_INTEL_ENDPOINT=https://your-resource.cognitiveservices.azure.com/

# ── Application Insights (optional – structured telemetry) ───────────────────
# Set this to enable telemetry. Leave blank to run without App Insights.
APPLICATIONINSIGHTS_CONNECTION_STRING=

# ── UI ─────────────────────────────────────────────────────────────────────────
APP_BANNER_MESSAGE=Internal use only – not for external sharing
```

---

## Workflow

The 4-step wizard guides you through:

1. **Upload** — provide a Word (`.docx`) or PPTX (`.pptx`) script, your target PowerPoint deck, choose a voice, and optionally enable AI mode.
2. **Map slides** — verify the script-to-slide mapping (reorder if needed).
3. **Generate** — watch real-time progress as audio is synthesised per slide and embedded, then download the narrated `.pptx`. Optionally export to `.mp4`.
4. **Quality check** — optional STT-based pass that estimates comprehension confidence per slide and flags any unclear words.

---

## Project Structure

```
powerpoint-add-tool/
├── .env.example                   # Configuration template
├── .gitignore
├── docker-compose.yml             # Local Docker Compose (C# + React/Nginx)
├── README.md
│
├── scripts/                       # Development and deployment scripts
│   ├── run.ps1                    # Start C# backend + Vite frontend (dev)
│   ├── run-docker.ps1             # Build and run via Docker Compose
│   └── deploy.ps1                 # Build images, push to ACR, deploy Bicep
│
├── backend-csharp/                # ASP.NET Core 8 backend
│   ├── Dockerfile
│   ├── PptxNarrator.sln
│   ├── src/PptxNarrator.Api/
│   │   ├── Controllers/           # NarrationController (all API endpoints)
│   │   ├── Services/              # TTS, STT, Translator, PptxBuilder, etc.
│   │   ├── Models/                # DTOs (SlideInfo, ProgressEvent, etc.)
│   │   └── Configuration/        # AppOptions (feature flags, Azure settings)
│   └── tests/PptxNarrator.Tests/ # xUnit unit tests
│       ├── Controllers/
│       └── Services/
│
├── frontend/                      # React + TypeScript + Vite frontend
│   ├── Dockerfile                 # Nginx production image
│   ├── src/
│   │   ├── api/narrationApi.ts    # All fetch calls to the C# backend
│   │   ├── pages/                 # Step1Upload, Step2Mapping, Step3Generate, Step4QualityCheck
│   │   ├── components/            # Shared UI components
│   │   └── types.ts               # Shared TypeScript types
│   └── tests/e2e/                 # Playwright end-to-end tests
│       ├── playwright.config.ts
│       └── wizard.spec.ts
│
└── infra/                         # Azure Bicep IaC
    ├── main.bicep
    ├── parameters.json
    └── modules/                   # Container Apps environment + app modules
```

---

## Running Tests

### C# unit tests (xUnit)

```powershell
cd backend-csharp
dotnet test --no-build -v normal
```

Covers:
- `NarrationControllerTests` — config, parse, process endpoints
- `PptxBuilderServiceTests` — audio embedding, timing XML, content types
- `TtsServiceTests` — SSML construction, HTTP mocking
- `WordParserServiceTests` — Word document parsing

### Playwright end-to-end tests

Ensure the frontend dev server is running (`.\scripts\run.ps1` or `npm run dev` in `frontend/`), then:

```powershell
cd frontend
npx playwright test
```

Or to run headlessly in CI:

```powershell
cd frontend
npx playwright test --reporter=html
```

The HTML report is written to `frontend/playwright-report/`.

Covers:
- Step 1 — file upload, AI mode toggle, voice selector, parse error handling
- Step 2 — slide mapping table, mismatch banner, navigation
- Step 3 — generation success/failure, download link, video export button, MP4 download
- Step 4 — quality check upload and results table

---

## Deployment to Azure

### Prerequisites

- An Azure resource group
- An Azure Container Registry (ACR)
- `az login` with Contributor access

### Deploy

```powershell
.\scripts\deploy.ps1 -ResourceGroup my-rg -AcrName myacr
```

Use ACR cloud build (no local Docker required):

```powershell
.\scripts\deploy.ps1 -ResourceGroup my-rg -AcrName myacr -UseAcrBuild
```

This will:
1. Build and push both Docker images to ACR
2. Deploy `infra/main.bicep` via `az deployment group create`
3. Provision Azure Container Apps environment, backend app, and frontend app

IaC parameters can be customised in `infra/parameters.json`.

---

## API Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/api/config` | Feature flags and banner message |
| `POST` | `/api/parse` | Parse script (Word/PPTX), return slide list |
| `POST` | `/api/process` | Full pipeline: TTS → embed audio → return PPTX |
| `POST` | `/api/generate-ai` | AI mode: GPT structuring + DALL-E images + TTS (NDJSON stream) |
| `POST` | `/api/export-video` | Convert narrated PPTX to MP4 (NDJSON stream) |
| `POST` | `/api/quality-check` | STT-based quality check on embedded audio |
| `GET` | `/healthz` | Health check |
| `GET` | `/swagger` | Swagger UI (development only) |

---

## Accessibility

The frontend meets **WCAG 2.1 Level AA / Section 508** standards:

| Area | Implementation |
|------|---------------|
| Skip navigation | "Skip to main content" link visible on keyboard focus |
| Headings | `<h1>` in header; each wizard step has `<h2>` |
| Colour contrast | All text ≥ 4.5:1 on background (WCAG AA) |
| Focus indicators | 2px violet `outline` on all interactive elements |
| ARIA landmarks | `<header>`, `<nav>` (step indicator), `<main id="main-content">` |
| Progressbar | `role="progressbar"`, `aria-valuenow/min/max`, `aria-label` |
| Live regions | Step 3 heading, video export progress use `aria-live="polite"` |
| Tables | `<th scope="col">` on all column headers |
| Toggle switch | Hidden `<input type="checkbox">` shows focus ring on visible track |
| File upload | Custom `role="button"` card updates `aria-label` with selected filename |
| Loading states | Buttons set `aria-busy="true"` while requests are in-flight |
| Icon-only content | Decorative SVGs use `aria-hidden="true"`; functional icons have text alternatives |
| Symbols in labels | Unicode arrows/symbols wrapped in `<span aria-hidden="true">` |

---

## Troubleshooting

**Backend won't start / auth errors**  
Run `az login --tenant 16b3c013-d300-468d-ac64-7eda0820b6d3`. If `AZURE_TENANT_ID` is not set, `DefaultAzureCredential` may pick up a different tenant token.

**No telemetry in Application Insights**  
Set `APPLICATIONINSIGHTS_CONNECTION_STRING` in your environment or `appsettings.json`. When left blank, the app runs without App Insights — no error is raised. Structured logs always go to the console/host logger regardless.

**PPTX opens with a repair dialog**  
This was a known issue now fixed. Make sure you are running a build from after the `PptxBuilderService` and `AiPptxGeneratorService` fixes (Content_Types, timing XML, theme part).

**Video export fails**  
Ensure `ffmpeg` is installed and on your PATH. On Windows, PowerPoint must be installed for slide rendering via COM. On Linux/Mac, install `libreoffice` and `poppler-utils` (`pdftoppm`).

**AI mode fails**  
Set `AZURE_OPENAI_ENDPOINT` and confirm your deployment names match `AZURE_OPENAI_DEPLOYMENT` (default `gpt-4o`) and `AZURE_IMAGE_DEPLOYMENT` (default `dall-e-3`).
