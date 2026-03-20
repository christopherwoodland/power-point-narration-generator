# Run the PowerPoint Narration Generator locally
# Usage: .\run.ps1
# Then open http://localhost:8000 in your browser

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$venv = Join-Path $root ".venv\Scripts"
$uvicorn = Join-Path $venv "uvicorn.exe"
$backend = Join-Path $root "backend"

if (-not (Test-Path $uvicorn)) {
    Write-Error "Virtual environment not found. Run: python -m venv .venv && .\.venv\Scripts\pip install -r requirements.txt"
    exit 1
}

Write-Host ""
Write-Host "  PowerPoint Narration Generator"  -ForegroundColor Cyan
Write-Host "  ================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Server: http://localhost:8000"    -ForegroundColor Green
Write-Host "  Press Ctrl+C to stop."
Write-Host ""

$env:AZURE_SPEECH_RESOURCE_NAME = "bhs-development-public-foundry-r"
$env:AZURE_SPEECH_REGION        = "eastus2"

& $uvicorn main:app --host 127.0.0.1 --port 8000 --app-dir $backend --reload
