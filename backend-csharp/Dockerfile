# ── Build stage ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Restore dependencies first (layer cache friendly)
COPY PptxNarrator.sln ./
COPY src/PptxNarrator.Api/PptxNarrator.Api.csproj ./src/PptxNarrator.Api/
RUN dotnet restore src/PptxNarrator.Api/PptxNarrator.Api.csproj

# Copy source and publish
COPY src/ ./src/
RUN dotnet publish src/PptxNarrator.Api/PptxNarrator.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app \
    -p:UseAppHost=false

# ── Runtime stage ───────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Run as non-root
RUN groupadd --system --gid 1001 appgroup \
    && useradd --system --uid 1001 --gid appgroup --no-create-home appuser

# ffmpeg is required by VideoExporterService for encoding MP4 clips
# libreoffice is required by VideoExporterService for headless PPTX→PDF rendering on Linux
# poppler-utils provides pdftoppm for PDF→PNG slide extraction
# azure-cli is required by DefaultAzureCredential (AzureCliCredential) for local dev
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl apt-transport-https gnupg lsb-release ffmpeg \
       libreoffice-impress libreoffice-calc libreoffice-writer poppler-utils \
    && curl -sLS https://packages.microsoft.com/keys/microsoft.asc \
       | gpg --dearmor > /etc/apt/trusted.gpg.d/microsoft.gpg \
    && echo "deb [arch=$(dpkg --print-architecture)] https://packages.microsoft.com/repos/azure-cli/ $(lsb_release -cs) main" \
       > /etc/apt/sources.list.d/azure-cli.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends azure-cli \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app .
COPY entrypoint.sh /entrypoint.sh

# Create writable azure-config dir; the entrypoint copies read-only host
# credentials from /azure-config-ro into /azure-config at container start.
RUN chmod +x /entrypoint.sh \
    && mkdir -p /azure-config \
    && chown -R appuser:appgroup /app /azure-config
USER appuser

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV AZURE_CONFIG_DIR=/azure-config
# LibreOffice needs a writable HOME for its user profile when running headless
ENV HOME=/tmp
EXPOSE 8080

ENTRYPOINT ["/entrypoint.sh", "dotnet", "PptxNarrator.Api.dll"]
