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

# ffmpeg is required by SttService for audio conversion
RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app .

RUN chown -R appuser:appgroup /app
USER appuser

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

ENTRYPOINT ["dotnet", "PptxNarrator.Api.dll"]
