#!/usr/bin/env bash
# .devcontainer/post-create.sh
# Runs after the container is created and the workspace is mounted.
# Sets up project dependencies so the dev environment is ready to use.
set -euo pipefail

WORKSPACE_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$WORKSPACE_ROOT"

echo ""
echo "╔═══════════════════════════════════════════════════╗"
echo "║  PowerPoint Narrator — Dev Container Setup        ║"
echo "╚═══════════════════════════════════════════════════╝"
echo ""

# ── .env ──────────────────────────────────────────────────────────────────────
if [ ! -f ".env" ]; then
    echo ">>> Copying .env.example → .env (fill in your Azure resource names)"
    cp .env.example .env
else
    echo ">>> .env already exists — skipping copy"
fi

# ── .NET restore ──────────────────────────────────────────────────────────────
echo ""
echo ">>> Restoring .NET packages..."
cd backend-csharp
dotnet restore PptxNarrator.sln --verbosity minimal
cd "$WORKSPACE_ROOT"

# ── npm install ───────────────────────────────────────────────────────────────
echo ""
echo ">>> Installing frontend npm packages..."
cd frontend
npm install --prefer-offline --silent
cd "$WORKSPACE_ROOT"

# ── Playwright browsers ───────────────────────────────────────────────────────
echo ""
echo ">>> Installing Playwright browsers (Chromium only)..."
cd frontend
npx playwright install chromium --with-deps 2>/dev/null || \
    echo "    Playwright install skipped (non-fatal)"
cd "$WORKSPACE_ROOT"

# ── summary ───────────────────────────────────────────────────────────────────
echo ""
echo "╔═══════════════════════════════════════════════════╗"
echo "║  Setup complete!                                  ║"
echo "║                                                   ║"
echo "║  Next steps:                                      ║"
echo "║  1. az login  (authenticate with Azure)           ║"
echo "║  2. Edit .env with your Azure resource names      ║"
echo "║  3. bash scripts/run.sh  (or pwsh scripts/run.ps1)║"
echo "║                                                   ║"
echo "║  Frontend → http://localhost:3000                 ║"
echo "║  Backend  → http://localhost:8080/swagger         ║"
echo "╚═══════════════════════════════════════════════════╝"
echo ""
