# ── Stage 1: Build deps ──────────────────────────────────────────────────────
FROM python:3.13-slim AS builder

WORKDIR /build

# Install build tools for any packages that need them
RUN apt-get update && apt-get install -y --no-install-recommends \
    gcc \
 && rm -rf /var/lib/apt/lists/*

COPY requirements.txt .
RUN pip install --no-cache-dir --prefix=/install -r requirements.txt


# ── Stage 2: Runtime ─────────────────────────────────────────────────────────
FROM python:3.13-slim

# Non-root user for security
RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser

WORKDIR /app

# Copy installed packages from builder
COPY --from=builder /install /usr/local

# Copy application code
COPY backend/ ./backend/
COPY frontend/ ./frontend/

# Run from backend directory so local imports (word_parser, tts_client, etc.) resolve
WORKDIR /app/backend

# Drop to non-root
USER appuser

EXPOSE 8000

# DefaultAzureCredential picks up managed identity automatically in Container Apps.
# Override AZURE_TTS_ENDPOINT if your Speech resource is in a different region.
ENV AZURE_TTS_ENDPOINT="https://eastus2.tts.speech.microsoft.com/cognitiveservices/v1"

CMD ["uvicorn", "main:app", "--host", "0.0.0.0", "--port", "8000", "--workers", "2"]
