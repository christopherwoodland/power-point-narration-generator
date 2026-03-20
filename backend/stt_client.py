"""
Azure Speech-to-Text client using DefaultAzureCredential (no API keys).

Reuses the same STS token exchange flow as tts_client.py:
  AAD token → STS token → STT REST endpoint

STT REST endpoint:
  POST https://<region>.stt.speech.microsoft.com/speech/recognition/conversation/cognitiveservices/v1
  Content-Type: audio/mpeg  (for MP3)
  Language specified via ?language=<locale> query param
"""
import os
import httpx
from tts_client import _get_speech_token   # reuse the cached STS token

_region = os.environ.get("AZURE_SPEECH_REGION", "eastus2").strip()
_STT_URL = f"https://{_region}.stt.speech.microsoft.com/speech/recognition/conversation/cognitiveservices/v1"


def transcribe_mp3(mp3_bytes: bytes, locale: str = "en-US") -> str:
    """
    Transcribes an MP3 audio file to text using Azure Speech REST API.
    Returns the recognised text string, or empty string on silence/failure.

    locale: BCP-47 locale matching the spoken language e.g. 'en-US', 'fr-FR'.
    """
    if not mp3_bytes:
        return ""

    speech_token = _get_speech_token()

    print(f"[STT] Transcribing {len(mp3_bytes):,} bytes  locale={locale}", flush=True)

    response = httpx.post(
        _STT_URL,
        params={
            "language": locale,
            "format": "detailed",
        },
        headers={
            "Authorization": f"Bearer {speech_token}",
            "Content-Type": "audio/mpeg",
            "Accept": "application/json",
        },
        content=mp3_bytes,
        timeout=120,
    )

    if response.status_code == 200:
        body = response.json()
        # "detailed" format has RecognitionStatus and NBest list
        status = body.get("RecognitionStatus", "")
        if status == "Success" and body.get("NBest"):
            text = body["NBest"][0].get("Display", "")
            print(f"[STT] OK — '{text[:80]}...'", flush=True)
            return text
        print(f"[STT] Status={status} (no speech recognised)", flush=True)
        return ""
    else:
        print(f"[STT] HTTP {response.status_code}: {response.text[:200]}", flush=True)
        response.raise_for_status()
        return ""
