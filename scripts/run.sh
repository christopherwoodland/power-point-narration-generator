#!/usr/bin/env bash
# scripts/run.sh — Start the PptxNarrator development environment inside Linux/macOS/devcontainer
# Usage: bash scripts/run.sh
#
# Feature flags (default: all enabled):
#   ENABLE_QUALITY_CHECK=false ENABLE_AI_MODE=false bash scripts/run.sh
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

BACKEND_PROJECT="backend-csharp/src/PptxNarrator.Api"
FRONTEND_DIR="frontend"
BACKEND_PORT="${BACKEND_PORT:-8080}"
FRONTEND_PORT="${FRONTEND_PORT:-3000}"

# ── Helpers ────────────────────────────────────────────────────────────────────
blue()    { printf '\033[0;36m%s\033[0m\n' "$*"; }
yellow()  { printf '\033[0;33m%s\033[0m\n' "$*"; }
green()   { printf '\033[0;32m%s\033[0m\n' "$*"; }
red()     { printf '\033[0;31m%s\033[0m\n' "$*"; }

require() {
  if ! command -v "$1" &>/dev/null; then
    red "Required command '$1' not found. Please install it and retry."
    exit 1
  fi
}

# ── Pre-flight ─────────────────────────────────────────────────────────────────
require dotnet
require npm
require node

# ── .env ───────────────────────────────────────────────────────────────────────
ENV_FILE="$ROOT/.env"
if [ -f "$ENV_FILE" ]; then
  # Export non-comment, non-blank lines
  set -o allexport
  # shellcheck disable=SC1090
  source "$ENV_FILE"
  set +o allexport
fi

# ── Feature flags (apply defaults) ────────────────────────────────────────────
export ENABLE_QUALITY_CHECK="${ENABLE_QUALITY_CHECK:-true}"
export ENABLE_AI_MODE="${ENABLE_AI_MODE:-true}"
export ENABLE_VIDEO_EXPORT="${ENABLE_VIDEO_EXPORT:-true}"
export AZURE_SPEECH_RESOURCE_NAME="${AZURE_SPEECH_RESOURCE_NAME:-bhs-development-public-foundry-r}"
export AZURE_SPEECH_REGION="${AZURE_SPEECH_REGION:-eastus2}"
export AZURE_TENANT_ID="${AZURE_TENANT_ID:-16b3c013-d300-468d-ac64-7eda0820b6d3}"

blue ""
blue "  PowerPoint Narration Generator"
blue "  ==============================="
printf '  %-30s %s\n' "Backend  →" "http://localhost:$BACKEND_PORT"
printf '  %-30s %s\n' "Frontend →" "http://localhost:$FRONTEND_PORT"
printf '  %-30s %s\n' "Swagger  →" "http://localhost:$BACKEND_PORT/swagger"
blue ""

for flag in ENABLE_QUALITY_CHECK ENABLE_AI_MODE ENABLE_VIDEO_EXPORT; do
  val="${!flag}"
  if [ "$val" = "false" ]; then
    yellow "  $flag = $val"
  else
    green  "  $flag = $val"
  fi
done

# ── npm install (once) ─────────────────────────────────────────────────────────
FRONTEND_PATH="$ROOT/$FRONTEND_DIR"
if [ ! -d "$FRONTEND_PATH/node_modules" ]; then
  echo ""
  yellow "  Installing npm dependencies…"
  ( cd "$FRONTEND_PATH" && npm install )
fi

# ── Trap for clean teardown ────────────────────────────────────────────────────
BACKEND_PID=""
FRONTEND_PID=""

cleanup() {
  echo ""
  green "  Stopping services…"
  [ -n "$BACKEND_PID"  ] && kill "$BACKEND_PID"  2>/dev/null || true
  [ -n "$FRONTEND_PID" ] && kill "$FRONTEND_PID" 2>/dev/null || true
  wait 2>/dev/null || true
  green "  Services stopped."
}
trap cleanup EXIT INT TERM

# ── Backend ────────────────────────────────────────────────────────────────────
BACKEND_PATH="$ROOT/$BACKEND_PROJECT"
if [ ! -d "$BACKEND_PATH" ]; then
  red "C# backend project not found at '$BACKEND_PATH'."
  exit 1
fi

(
  cd "$BACKEND_PATH"
  export ASPNETCORE_URLS="http://+:$BACKEND_PORT"
  while IFS= read -r line; do printf '[backend]  %s\n' "$line"; done < <(dotnet run --no-launch-profile 2>&1)
) &
BACKEND_PID=$!

# ── Frontend ───────────────────────────────────────────────────────────────────
(
  cd "$FRONTEND_PATH"
  while IFS= read -r line; do printf '[frontend] %s\n' "$line"; done < <(npm run dev -- --port "$FRONTEND_PORT" 2>&1)
) &
FRONTEND_PID=$!

echo ""
yellow "  Press Ctrl+C to stop all services."

# Wait for either process to exit
wait -n 2>/dev/null || wait
