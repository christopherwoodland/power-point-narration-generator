# PowerPoint Narration Generator

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Code of Conduct](https://img.shields.io/badge/code%20of%20conduct-microsoft-blue)](CODE_OF_CONDUCT.md)

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
> - **Native dev (`run.ps1` / `run.sh`) — recommended for local work:** `az login` on the host is sufficient. The backend process runs under your user account, which is on a managed (compliant) device and satisfies any tenant Conditional Access policies.
> - **Docker Compose (`run-docker.ps1`) — limited:** Linux containers are **unmanaged devices** from Entra ID's perspective. If your tenant has a Conditional Access policy that requires the device to be Microsoft-managed/compliant (most Microsoft-internal tenants do — including the Microsoft Non-Production tenant `16b3c013-d300-468d-ac64-7eda0820b6d3` that this app targets by default), **all interactive sign-in flows from inside the container will fail** with: `Your sign-in was successful but your admin requires the device requesting access to be managed by <tenant> to access this resource.` This applies to: in-container `az login` (device code or browser), service-principal credentials whose CA policy also requires a managed device, and any attempt to bind-mount the host `~/.azure` cache (the Windows MSAL cache is DPAPI-encrypted and unreadable on Linux). **For these tenants, use the native dev path locally** and reserve Docker Compose for tenants without device-compliance CA, or for testing the container build itself. See [Running with Docker Compose](#running-with-docker-compose) for details.
> - **Production (Container Apps):** Managed Identity — no secrets, no env vars, no Conditional Access device check (the platform's IMDS endpoint is exempt). This always works.

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

> **Note:** `run.ps1` / `run.sh` (native .NET + Vite) is the **recommended** path for day-to-day local development — it uses your host `az login` session directly, satisfies tenant device-compliance Conditional Access policies, and requires no extra setup.
> Docker Compose is useful for testing the containerised deployment shape, but local Azure auth from inside the container is **not supported** in tenants that enforce a managed-device CA policy (see below).

### Why Docker auth is hard locally

Three separate problems combine to make `DefaultAzureCredential` painful inside a local Linux container on a Windows host:

1. **Windows DPAPI token cache.** The host's `~/.azure/msal_token_cache.bin` is encrypted with DPAPI (a Windows-only mechanism tied to your Windows user). Linux containers cannot decrypt it, so bind-mounting `~/.azure` into the container does **not** share your `az login` session.
2. **Conditional Access "managed device" policy.** The Microsoft Non-Production tenant (and most Microsoft-internal tenants) require the signing-in device to be Entra-joined and compliant. A Linux container is neither, so any interactive auth started from inside the container — `az login --use-device-code`, browser flow, etc. — fails with: `Your sign-in was successful but your admin requires the device requesting access to be managed by <tenant> to access this resource.`
3. **Service-principal CA blocking.** Service-principal client-secret auth (`AZURE_CLIENT_ID` + `AZURE_CLIENT_SECRET`) is not subject to device CA, but many tenants apply additional CA policies to service principals (workload-identity CA, secret-rotation policy, IP restrictions, etc.) and may block them too. SP credentials also expire and must be rotated.

**Bottom line:** for the default tenant this repo targets, Docker Compose locally cannot reach Azure AI services. Use `scripts/run.ps1` (Windows) or `scripts/run.sh` (Linux/macOS/Dev Container) instead — your host's `az login` already works.

If your target tenant does **not** enforce device-compliance CA and you have a working service principal, you can still use Docker Compose:

### One-time setup

**1. Create a service principal and grant it the required roles:**

```powershell
$sp = az ad sp create-for-rbac --name pptx-narrator-local --skip-assignment | ConvertFrom-Json
$aiResourceId = az cognitiveservices account show `
  --name <your-ai-resource-name> `
  --resource-group <your-ai-rg> `
  --query id -o tsv

az role assignment create --assignee $sp.appId --role "Cognitive Services User"        --scope $aiResourceId
az role assignment create --assignee $sp.appId --role "Cognitive Services Speech User" --scope $aiResourceId
az role assignment create --assignee $sp.appId --role "Cognitive Services OpenAI User" --scope $aiResourceId   # only if AI mode
```

Copy `appId`, `password`, and `tenant` from the `$sp` output.

**2. Add the credentials to `.env`:**

```env
AZURE_TENANT_ID=<tenant>
AZURE_CLIENT_ID=<appId>
AZURE_CLIENT_SECRET=<password>
```

> Tenant policy may cap the secret lifetime. Rotate with `az ad app credential reset --id <appId> --display-name pptx-narrator-local --end-date <YYYY-MM-DD>`.

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

Copy `.env.example` to `.env` and fill in your values. The full set of supported environment variables is listed below — only the **Azure Speech** group is strictly required for the default flow; everything else is optional or has a sensible default.

### Backend environment variables

| Variable | Default | Purpose |
|----------|---------|---------|
| `ENABLE_QUALITY_CHECK` | `false` | Toggle Step 4 (STT round-trip quality check). |
| `ENABLE_AI_MODE` | `false` | Toggle AI slide generation (Step 1 AI mode). Requires Azure OpenAI. |
| `ENABLE_VIDEO_EXPORT` | `false` | Toggle MP4 export. Requires `ffmpeg` (and PowerPoint COM on Windows / LibreOffice on Linux). |
| `DEFAULT_SINGLE_PPTX_MODE` | `false` | When `true`, Step 1 defaults to the single-PPTX flow where one PowerPoint is used as both the narration source and the presentation target. |
| `CORS_ALLOWED_ORIGINS` | `*` | Comma-separated allowed origins for the backend API. Set to your frontend URL in production. |
| `AZURE_SPEECH_RESOURCE_NAME` | `bhs-development-public-foundry-r` | Cognitive Services / Foundry resource name used for TTS + STT. |
| `AZURE_SPEECH_REGION` | `eastus2` | Region of the Speech resource. |
| `AZURE_TTS_MODE` | `standard` | `standard` = regional Azure Speech; `mai` = Foundry MAI-Voice-1 endpoint. |
| `AZURE_TTS_MAX_PARALLELISM` | `4` | Max number of slides synthesized concurrently. Start with `3` or `4`; higher values can improve throughput but may increase `429` throttling. |
| `AZURE_VOICE_ENDPOINT` | *(blank)* | Foundry resource base URL. Used only when `AZURE_TTS_MODE=mai`. |
| `AZURE_OPENAI_ENDPOINT` | `https://bhs-development-public-foundry-r.cognitiveservices.azure.com` | Azure OpenAI / Foundry chat endpoint. Required for AI mode. |
| `AZURE_OPENAI_DEPLOYMENT` | `gpt-5.2` | Chat deployment name. |
| `AZURE_IMAGE_ENDPOINT` | *(blank)* | Separate endpoint for image generation (MAI on `services.ai.azure.com`). |
| `AZURE_IMAGE_DEPLOYMENT` | `MAI-Image-2e` | Image deployment name. |
| `AZURE_DOC_INTEL_ENDPOINT` | `https://bhs-development-public-foundry-r.cognitiveservices.azure.com/` | Optional Document Intelligence endpoint for OCR fallback in PPTX parsing. Leave blank to skip. |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | *(blank)* | Enables Application Insights telemetry when set. App Insights is skipped when blank — no error. |
| `APP_BANNER_MESSAGE` | *(blank)* | Optional banner text rendered at the top of the UI. |
| `UPLOAD_FILES_MESSAGE` | `Provide a narration script and (optionally) a PowerPoint to narrate.` | Optional Step 1 helper text shown under "Upload your files". |
| `AZURE_TENANT_ID` | *(blank)* | Pins `DefaultAzureCredential` to a specific tenant. Recommended in multi-tenant environments. |
| `AZURE_CLIENT_ID` | *(blank)* | **Local Docker only** — service principal client ID for `EnvironmentCredential`. Leave blank in ACA (uses Managed Identity). |
| `AZURE_CLIENT_SECRET` | *(blank)* | **Local Docker only** — service principal secret. Leave blank in ACA. |
| `UI_BRANDING_PATH` | `<content root>/ui-branding.json` | File path for persisted UI branding settings (colors, logo, app name, voice list). In Docker/ACA, set to `/data/ui-branding.json` (mounted volume). |

### Frontend environment variables

| Variable | Default | Purpose |
|----------|---------|---------|
| `VITE_API_URL` | *(blank)* | Build-time API base URL baked into the Vite bundle. Leave blank for local dev (Vite proxy / Nginx forward `/api/*` to the backend). Set in CI/CD when building the production image, e.g. `https://narrator-prod-backend.<region>.azurecontainerapps.io`. |
| `BACKEND_URL` | `http://backend:8080` | Runtime backend URL used by the Nginx container to proxy `/api/*`. Set automatically by `scripts/deploy.ps1` to the deployed backend FQDN. |

> **Note on Azure Translator:** The Translator client reuses the Speech / Cognitive Services resource and does not require its own endpoint variable.

> **Note on TTS completion semantics:** generation now uses bounded parallel TTS. A run succeeds only when every required slide with narration text completes translation and TTS successfully. If any required slide fails, the request fails instead of returning a partially narrated deck.

> **Note on transient Azure failures:** TTS, translation, and Speech token exchange use a small built-in retry policy for transient `408`, `429`, and `5xx` responses. `Retry-After` is honored when Azure provides it.

> **Note on `appsettings.json`:** All backend values can also be set under the `App:` section of `backend-csharp/src/PptxNarrator.Api/appsettings.json` (see `appsettings.example.json`). Environment variables always win over `appsettings.json`.

> **Note on UI branding:** App name, logo, colors, and voice restrictions are system-wide settings stored in `ui-branding.json` (configurable via `UI_BRANDING_PATH`). Changes made in the Admin panel apply to all users immediately. In Azure Container Apps, the file is persisted on an Azure File Share mounted at `/data`. In local dev, it's written next to the backend `.csproj`.

---

## Workflow

The 4-step wizard guides you through:

1. **Upload** — provide a Word (`.docx`) or PPTX (`.pptx`) script and a target PowerPoint deck, or enable the single-PPTX flow to use one PowerPoint as both the narration source and the presentation target. Choose a voice, and optionally enable AI mode.
2. **Map slides** — verify the script-to-slide mapping (reorder if needed).
3. **Generate** — watch real-time progress as audio is synthesized in bounded parallel per slide and embedded, then download the narrated `.pptx`. Generation only completes successfully when all required slide narrations succeed. Optionally export to `.mp4`.
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
- `TtsServiceTests` — SSML construction, HTTP mocking, transient retry behavior
- `TranslatorServiceTests` — locale handling and transient retry behavior
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
| Azure Storage Account + File Share | Yes (created by Bicep) | Persists system-wide UI branding settings across restarts |
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
- `azureTtsMaxParallelism` (start with `4`)
- `azureOpenAiEndpoint`
- `azureOpenAiDeployment`
- `azureImageDeployment`
- `backendExternalIngress` (set `false` to make backend internal-only)
- `backendCorsAllowedOrigins` (set explicit origins instead of `*`)
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

Override TTS concurrency for a one-off deployment without editing `infra/parameters.json`:

```powershell
.\scripts\deploy.ps1 -ResourceGroup <rg-name> -AcrName <acr-name> -TtsMaxParallelism 6
```

The script will:
1. Build and push both images to ACR.
2. Deploy `infra/main.bicep` with image references.
3. Provision Container Apps environment, frontend app, backend app, and managed identities.
4. Pass `azureTtsMaxParallelism` through to the backend Container App as `AZURE_TTS_MAX_PARALLELISM`.

### 4. Post-deploy configuration behavior

`scripts/deploy.ps1` now automates the two critical post-deploy steps:

1. Sets frontend `BACKEND_URL` to the deployed backend URL.
2. Attempts to assign `Cognitive Services User` to the backend managed identity.

If your Azure AI/Cognitive Services account is in a different resource group or has a different name than `infra/parameters.json`, pass these optional parameters:

```powershell
.\scripts\deploy.ps1 -ResourceGroup <rg-name> -AcrName <acr-name> -AiResourceGroup <ai-rg> -AiResourceName <ai-resource-name>
```

For production, prefer a stable `azureTtsMaxParallelism` value over the highest possible value. Higher concurrency can reduce narration time on large decks, but it also increases the chance of `429` throttling from Speech or Translator. Because generation now uses all-or-nothing completion semantics, a conservative value like `4` is usually the right starting point.

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
- Set `backendExternalIngress=false` in `infra/parameters.json` if backend should not be publicly reachable.
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

**`Your sign-in was successful but your admin requires the device requesting access to be managed by <tenant>...`**
You are running the backend inside a local Docker container on a tenant that enforces a Conditional Access "compliant device" policy. Linux containers are unmanaged devices, so all interactive Azure sign-in flows from inside the container will fail with this error — including `az login --use-device-code`, browser flows, and any token requested by `DefaultAzureCredential` chain entries that depend on them. **Fix:** run the backend natively on your (managed) host instead — `scripts/run.ps1` on Windows or `scripts/run.sh` on Linux/macOS. The host's `az login` session satisfies the device check. Production deployments on Azure Container Apps are not affected because Managed Identity bypasses CA entirely.

**`ClientSecretCredential authentication failed ... AADSTS7000215: Invalid client secret provided`**
The service principal's client secret has expired or was rotated. Either rotate the secret (`az ad app credential reset --id <client-id>`) and update `.env`, or — recommended — switch to native dev (`scripts/run.ps1`) and clear `AZURE_CLIENT_ID` / `AZURE_CLIENT_SECRET` from `.env` so `DefaultAzureCredential` falls through to `AzureCliCredential`.

**No telemetry in Application Insights**
Set `APPLICATIONINSIGHTS_CONNECTION_STRING` in your environment or `appsettings.json`. When left blank, the app runs without App Insights — no error is raised. Structured logs always go to the console/host logger regardless.

**PPTX opens with a repair dialog**
This was a known issue now fixed. Make sure you are running a build from after the `PptxBuilderService` and `AiPptxGeneratorService` fixes (Content_Types, timing XML, theme part).

**Video export fails**
Ensure `ffmpeg` is installed and on your PATH. On Windows, PowerPoint must be installed for slide rendering via COM. On Linux/Mac, install `libreoffice` and `poppler-utils` (`pdftoppm`).

**AI mode fails**
Set `AZURE_OPENAI_ENDPOINT` and confirm your deployment names match `AZURE_OPENAI_DEPLOYMENT` (default `gpt-4o`) and `AZURE_IMAGE_DEPLOYMENT` (default `dall-e-3`).

---

## Contributing

Contributions are welcome! Please read [CONTRIBUTING.md](CONTRIBUTING.md) for the pull-request workflow, coding conventions, and how to run tests locally.

This project requires contributors to sign a [Contributor License Agreement (CLA)](https://cla.opensource.microsoft.com) before merging pull requests.

---

## Code of Conduct

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/).
See [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) or contact [opencode@microsoft.com](mailto:opencode@microsoft.com) for more information.

---

## Security

To report a security vulnerability, please follow the responsible disclosure process described in [SECURITY.md](SECURITY.md) — do **not** open a public GitHub issue.

---

## License

Copyright (c) Microsoft Corporation. All rights reserved.

Licensed under the [MIT](LICENSE) license.
