"""
Azure Text-to-Speech client using DefaultAzureCredential (no API keys).

Two-step auth flow required by Azure Speech Service:
  1. Get AAD token from DefaultAzureCredential (Cognitive Services scope)
  2. Exchange AAD token for a short-lived Speech STS token via:
       POST https://<resource>.cognitiveservices.azure.com/sts/v1.0/issueToken
  3. Call the REGIONAL TTS endpoint with the STS token:
       https://<region>.tts.speech.microsoft.com/cognitiveservices/v1

NOTE: The custom subdomain endpoint (*.cognitiveservices.azure.com/cognitiveservices/v1)
      returns 404 for TTS. The regional endpoint requires a Speech STS token, not
      a raw AAD Bearer token (which causes 401). The STS exchange is the bridge.

Environment variables:
  AZURE_SPEECH_RESOURCE_NAME  — Speech resource name (required).
                                Used to build the STS exchange URL.
  AZURE_SPEECH_REGION         — Azure region (default: eastus2).
                                Used to build the regional TTS URL.
"""
import os
import time
import httpx
from azure.identity import DefaultAzureCredential

COGNITIVE_SERVICES_SCOPE = "https://cognitiveservices.azure.com/.default"

_resource_name = os.environ.get("AZURE_SPEECH_RESOURCE_NAME", "bhs-development-public-foundry-r").strip()
_region = os.environ.get("AZURE_SPEECH_REGION", "eastus2").strip()

# Step 2: STS token exchange URL
_STS_URL = f"https://{_resource_name}.cognitiveservices.azure.com/sts/v1.0/issueToken"

# Step 3: Regional TTS endpoint
_TTS_URL = f"https://{_region}.tts.speech.microsoft.com/cognitiveservices/v1"

# STS token cache (tokens are valid for ~10 min; refresh at 8 min)
_sts_token: str | None = None
_sts_expiry: float = 0.0


def _get_speech_token() -> str:
    """Exchange a DefaultAzureCredential AAD token for a Speech STS token."""
    global _sts_token, _sts_expiry

    now = time.time()
    if _sts_token and _sts_expiry > now + 120:  # 2-min buffer
        return _sts_token

    # Step 1: get AAD token
    aad_token = DefaultAzureCredential().get_token(COGNITIVE_SERVICES_SCOPE).token

    # Step 2: exchange for Speech STS token
    response = httpx.post(
        _STS_URL,
        headers={
            "Authorization": f"Bearer {aad_token}",
            "Content-Type": "application/x-www-form-urlencoded",
            "Content-Length": "0",
        },
        timeout=15,
    )
    response.raise_for_status()

    _sts_token = response.text
    _sts_expiry = now + 10 * 60  # STS tokens valid ~10 min
    return _sts_token


def _build_ssml(text: str, voice: str = "en-US-JennyNeural") -> str:
    safe = (
        text.replace("&", "&amp;")
            .replace("<", "&lt;")
            .replace(">", "&gt;")
            .replace('"', "&quot;")
            .replace("'", "&apos;")
    )
    return (
        f'<speak version="1.0" xmlns="http://www.w3.org/2001/10/synthesis" '
        f'xml:lang="en-US">'
        f'<voice name="{voice}">{safe}</voice>'
        f"</speak>"
    )


def synthesize_to_mp3(text: str, voice: str = "en-US-JennyNeural") -> bytes:
    """
    Synthesizes text to MP3 audio bytes using Azure Neural TTS.
    Uses DefaultAzureCredential + STS token exchange — no API key required.
    """
    if not text.strip():
        return b""

    print(f"[TTS] Fetching STS token...", flush=True)
    speech_token = _get_speech_token()
    ssml = _build_ssml(text, voice)

    preview = text[:80].replace('\n', ' ')
    print(f"[TTS] POST {_TTS_URL}  voice={voice}  text='{preview}...'", flush=True)

    headers = {
        "Authorization": f"Bearer {speech_token}",
        "Content-Type": "application/ssml+xml",
        "X-Microsoft-OutputFormat": "audio-24khz-48kbitrate-mono-mp3",
        "User-Agent": "powerpoint-add-tool",
    }

    response = httpx.post(_TTS_URL, content=ssml.encode("utf-8"), headers=headers, timeout=60)
    response.raise_for_status()
    audio = response.content
    print(f"[TTS] OK — {len(audio):,} bytes received", flush=True)
    return audio
