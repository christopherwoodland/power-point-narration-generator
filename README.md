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
| Dev Container | VS Code Dev Container (Ubuntu 24.04 + .NET 10 + Node 20 + Azure CLI + FFmpeg) |
| IaC | Azure Bicep (Container Apps) |

---

## Dev Container (recommended)

The fastest way to get a fully configured environment is to open the repo in a [VS Code Dev Container](https://code.visualstudio.com/docs/devcontainers/containers).

**Prerequisites:** Docker Desktop + the VS Code [Dev Containers extension](https://marketplace.visualstudio.com/items?itemName=ms-vscode-remote.remote-containers).

```
1. Open the repo in VS Code
2. Command Palette → "Dev Containers: Reopen in Container"
3. Wait ~3 min for the first build (subsequent opens are instant)
```

The container automatically provides:
- .NET 10 SDK
- Node.js 20 LTS + npm
- Azure CLI (+ Bicep)
- FFmpeg
- Docker CLI (Docker-outside-of-Docker)
- All recommended VS Code extensions
- `npm install` + `dotnet restore` + `.env` creation run automatically

After the container starts, run `az login` and then `bash scripts/run.sh` (or `pwsh scripts/run.ps1`) to start both services.

---

## Prerequisites (without Dev Container)

| Requirement | Notes |
|-------------|-------|
| [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) | `dotnet --version` should show `10.x` |
| [Node.js 20+](https://nodejs.org/) | `node --version` |
| [Azure CLI](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli) | Run `az login` before starting |
| Azure Cognitive Services resource | Speech TTS/STT endpoint |
| Azure OpenAI resource | Required for AI mode only |
| [FFmpeg](https://ffmpeg.org/download.html) on PATH | Required for video export only |
| Docker Desktop | For Docker Compose / container workflow only |

> **Auth note:** All Azure connections use `DefaultAzureCredential` — no API keys anywhere.
> - **Native dev (`run.ps1`):** `az login` on the host is sufficient.
> - **Docker Compose (`run-docker.ps1`):** Requires a service principal (`AZURE_CLIENT_ID` + `AZURE_CLIENT_SECRET` in `.env`) because the Windows DPAPI-encrypted token cache cannot be read by Linux containers. See the [Running with Docker Compose](#running-with-docker-compose) section.
> - **Production (Container Apps):** Managed Identity — no secrets or env vars needed.

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

**Windows (PowerShell):**
```powershell
.\scripts\run.ps1
```

**Linux / macOS / Dev Container:**
```bash
bash scripts/run.sh
```

Then open **http://localhost:3000** in your browser.
Swagger UI is available at **http://localhost:8080/swagger**.

---

## Running with Docker Compose

> **Note:** `run.ps1` / `run.sh` (native .NET + Vite) is the simpler path for day-to-day local development — it uses your host `az login` session directly and requires no extra setup.
> Use Docker Compose when you want to test the containerised deployment (closer to production).

### Why Docker requires a service principal

Windows encrypts the Azure CLI token cache (`msal_token_cache.bin`) with DPAPI — a Windows-only mechanism that Linux containers cannot decrypt. This means the host `az login` session **cannot** be shared with the container. Instead, the container uses `EnvironmentCredential` (client ID + secret) which `DefaultAzureCredential` picks up automatically.

### One-time setup

**1. Use the existing service principal (already has the required roles):**

| Setting | Value |
|---------|-------|
| `AZURE_TENANT_ID` | `16b3c013-d300-468d-ac64-7eda0820b6d3` |
| `AZURE_CLIENT_ID` | `98e8135d-5ca5-4015-b0ac-825ae189de20` |
| `AZURE_CLIENT_SECRET` | *(generate a new secret — see below)* |

The SP already has these roles on `bhs-development-public-foundry-r`:
- `Cognitive Services User` — required for the Speech STS `/issueToken` exchange
- `Cognitive Services Speech User`
- `Cognitive Services OpenAI User`

**2. If the secret has expired, create a new one** (tenant policy limits lifetime, check the max allowed):

```powershell
az ad app credential reset `
  --id 98e8135d-5ca5-4015-b0ac-825ae189de20 `
  --display-name "pptx-narrator-local" `
  --end-date <YYYY-MM-DD>   # within the tenant policy limit
```

Copy the `password` field from the output.

**3. Add the credentials to `.env`:**

```env
AZURE_TENANT_ID=16b3c013-d300-468d-ac64-7eda0820b6d3
AZURE_CLIENT_ID=98e8135d-5ca5-4015-b0ac-825ae189de20
AZURE_CLIENT_SECRET=<password from above>
```

### Start

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
├── .devcontainer/                 # VS Code Dev Container configuration
│   ├── devcontainer.json          # Container definition, features, extensions
│   ├── on-create.sh               # System packages (ffmpeg, etc.)
│   └── post-create.sh             # npm install, dotnet restore, .env copy
├── .env.example                   # Configuration template
├── .gitignore
├── docker-compose.yml             # Local Docker Compose (C# + React/Nginx)
├── README.md
│
├── scripts/                       # Development and deployment scripts
│   ├── run.ps1                    # Start C# backend + Vite frontend (PowerShell / Windows)
│   ├── run.sh                     # Start C# backend + Vite frontend (Bash / Linux / Dev Container)
│   ├── run-docker.ps1             # Build and run via Docker Compose
│   └── deploy.ps1                 # Build images, push to ACR, deploy Bicep
│
├── backend-csharp/                # ASP.NET Core 10 backend
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

Ensure the frontend dev server is running (`bash scripts/run.sh` or `.\scripts\run.ps1` on Windows, or `npm run dev` in `frontend/`), then:

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

## Deployment to Azure Container Apps (Production)

### Azure services required

The app runs as two Azure Container Apps (frontend + backend) and uses managed identity only (no API keys).

| Service | Required | Purpose |
|--------|----------|---------|
| Resource Group | Yes | Deployment scope for all resources |
| Azure Container Registry (ACR) | Yes | Stores frontend/backend container images |
| Azure Container Apps Environment | Yes (created by Bicep) | Shared runtime environment for both apps |
| Log Analytics Workspace | Yes (created by Bicep) | Container Apps logs and diagnostics |
| Azure Container App (backend) | Yes (created by Bicep) | ASP.NET Core API workload |
| Azure Container App (frontend) | Yes (created by Bicep) | React + Nginx UI workload |
| User-assigned Managed Identities | Yes (created by Bicep) | Backend/frontend identity and ACR pull auth |
| Azure AI / Cognitive Services resource | Yes | Speech TTS/STT and OpenAI endpoint access |
| Azure OpenAI deployments | If AI mode enabled | Chat + image generation (for Step 3 AI mode) |
| Azure Document Intelligence | Optional | Better PPTX text extraction for sparse slides |
| Application Insights | Optional | Application telemetry |

### Azure roles required

- Deployer identity: Contributor on the target resource group (and permission to push to ACR).
- Backend managed identity: at minimum Cognitive Services User on your Azure AI/Cognitive Services resource.
- Backend managed identity: if OpenAI calls return 403 in your tenant, also assign Cognitive Services OpenAI User.

Note: AcrPull role assignments for frontend/backend managed identities are created automatically by the Bicep modules.

### 1. One-time setup

Sign in and select the tenant/subscription:

```powershell
az login --tenant 16b3c013-d300-468d-ac64-7eda0820b6d3
az account set --subscription <your-subscription-id-or-name>
```

Create the resource group and ACR if needed:

```powershell
az group create -n <rg-name> -l eastus2
az acr create -g <rg-name> -n <acr-name> --sku Standard
```

### 2. Configure IaC parameters

Update `infra/parameters.json` for your environment:

- `location`
- `environmentName` (for example: `prod`)
- `azureSpeechResourceName`
- `azureSpeechRegion`
- `azureOpenAiEndpoint`
- `azureOpenAiDeployment`
- `azureImageDeployment`
- Optional: `azureDocIntelEndpoint`, `appBannerMessage`

The deployment script injects `containerRegistryName`, `backendImage`, and `frontendImage` automatically.

### 3. Deploy

Local Docker build + push:

```powershell
.\scripts\deploy.ps1 -ResourceGroup <rg-name> -AcrName <acr-name>
```

ACR cloud build (no local Docker required):

```powershell
.\scripts\deploy.ps1 -ResourceGroup <rg-name> -AcrName <acr-name> -UseAcrBuild
```

The script will:
1. Build and push both images to ACR.
2. Deploy `infra/main.bicep` with image references.
3. Provision Container Apps environment, frontend app, backend app, and managed identities.

### 4. Post-deploy configuration behavior

`scripts/deploy.ps1` now automates the two critical post-deploy steps:

1. Sets frontend `BACKEND_URL` to the deployed backend URL.
2. Attempts to assign `Cognitive Services User` to the backend managed identity.

If your Azure AI/Cognitive Services account is in a different resource group or has a different name than `infra/parameters.json`, pass these optional parameters:

```powershell
.\scripts\deploy.ps1 -ResourceGroup <rg-name> -AcrName <acr-name> -AiResourceGroup <ai-rg> -AiResourceName <ai-resource-name>
```

If your deployer identity cannot create role assignments (`Microsoft.Authorization/roleAssignments/write`), the script will warn and continue. In that case, assign roles manually:

```powershell
$backendPrincipalId = az identity show `
  --resource-group <rg-name> `
  --name narrator-<environmentName>-backend-id `
  --query principalId -o tsv

az role assignment create `
  --assignee-object-id $backendPrincipalId `
  --assignee-principal-type ServicePrincipal `
  --role "Cognitive Services User" `
  --scope /subscriptions/<sub-id>/resourceGroups/<ai-rg>/providers/Microsoft.CognitiveServices/accounts/<ai-resource-name>
```

Optional fallback for stricter OpenAI RBAC:

```powershell
az role assignment create `
  --assignee-object-id $backendPrincipalId `
  --assignee-principal-type ServicePrincipal `
  --role "Cognitive Services OpenAI User" `
  --scope /subscriptions/<sub-id>/resourceGroups/<ai-rg>/providers/Microsoft.CognitiveServices/accounts/<ai-resource-name>
```

### 5. Validate production deployment

```powershell
az containerapp show -g <rg-name> -n narrator-<environmentName>-frontend --query properties.configuration.ingress.fqdn -o tsv
az containerapp show -g <rg-name> -n narrator-<environmentName>-backend --query properties.configuration.ingress.fqdn -o tsv
```

Then verify:
- Frontend URL loads the wizard.
- Backend health endpoint returns OK: `https://<backend-fqdn>/healthz`
- Generate flow succeeds (including AI mode if enabled).

### Production hardening recommendations

- Restrict backend CORS to your frontend domain(s) instead of `*`.
- Add custom domains + TLS certificates for frontend/backend ingress.
- Put Azure Front Door or Application Gateway (WAF) in front of public ingress.
- Send diagnostics to Application Insights and keep Log Analytics retention aligned to policy.
- Consider private networking for Azure AI resources if your org requires no public endpoints.

### Additional production checks

- Ensure backend and frontend images are pinned to immutable tags (avoid relying only on `latest`).
- Decide whether `ENABLE_VIDEO_EXPORT=true` is appropriate for Linux Container Apps (PowerPoint COM is Windows-only).
- Verify `azureSpeechResourceName`/`azureOpenAiEndpoint` point to the correct production Azure AI account.
- Confirm scale settings in Bicep (`minReplicas`, `maxReplicas`, `concurrentRequests`) match your expected traffic.
- Restrict backend ingress/CORS and consider internal-only backend ingress behind Front Door/App Gateway.

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
