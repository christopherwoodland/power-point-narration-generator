"""
Azure Speech-to-Text client using DefaultAzureCredential (no API keys).

Reuses the same STS token exchange flow as tts_client.py:
  AAD token → STS token → STT REST endpoint

STT REST endpoint:
  POST https://<region>.stt.speech.microsoft.com/speech/recognition/conversation/cognitiveservices/v1
  Content-Type: audio/wav; codecs=audio/pcm; samplerate=16000

Audio is first converted from MP3 → 16kHz 16-bit mono PCM WAV via ffmpeg,
which is the format Azure STT REST API most reliably supports.
"""
import os
import subprocess
import tempfile
import httpx
from tts_client import _get_speech_token   # reuse the cached STS token

_region = os.environ.get("AZURE_SPEECH_REGION", "eastus2").strip()
_STT_URL = f"https://{_region}.stt.speech.microsoft.com/speech/recognition/conversation/cognitiveservices/v1"


def _mp3_to_wav_pcm(mp3_bytes: bytes) -> bytes:
    """
    Convert MP3 bytes to 16kHz 16-bit mono PCM WAV bytes using ffmpeg.
    ffmpeg is required to be on PATH.
    """
    with tempfile.TemporaryDirectory() as tmp:
        in_path  = os.path.join(tmp, "audio.mp3")
        out_path = os.path.join(tmp, "audio.wav")

        with open(in_path, "wb") as f:
            f.write(mp3_bytes)

        result = subprocess.run(
            [
                "ffmpeg", "-y",
                "-i", in_path,
                "-ar", "16000",   # 16kHz sample rate
                "-ac", "1",       # mono
                "-acodec", "pcm_s16le",   # 16-bit PCM
                out_path,
            ],
            capture_output=True,
            timeout=30,
        )
        if result.returncode != 0:
            raise RuntimeError(
                f"ffmpeg conversion failed: {result.stderr.decode(errors='replace')[:300]}"
            )

        with open(out_path, "rb") as f:
            return f.read()


_CHUNK_SECONDS = 55  # Azure STT REST API limit is 60s; stay safely under it


def _split_wav_chunks(wav_bytes: bytes) -> list[bytes]:
    """
    Split a WAV file into chunks of at most _CHUNK_SECONDS each using ffmpeg.
    Returns a list of WAV byte strings (may be a single-element list for short audio).
    """
    with tempfile.TemporaryDirectory() as tmp:
        in_path = os.path.join(tmp, "input.wav")
        out_pattern = os.path.join(tmp, "chunk_%03d.wav")

        with open(in_path, "wb") as f:
            f.write(wav_bytes)

        result = subprocess.run(
            [
                "ffmpeg", "-y",
                "-i", in_path,
                "-f", "segment",
                "-segment_time", str(_CHUNK_SECONDS),
                "-c", "copy",
                out_pattern,
            ],
            capture_output=True,
            timeout=60,
        )
        if result.returncode != 0:
            raise RuntimeError(
                f"ffmpeg segment failed: {result.stderr.decode(errors='replace')[:300]}"
            )

        chunks = []
        i = 0
        while True:
            chunk_path = os.path.join(tmp, f"chunk_{i:03d}.wav")
            if not os.path.exists(chunk_path):
                break
            with open(chunk_path, "rb") as f:
                chunks.append(f.read())
            i += 1
        return chunks


def _transcribe_wav_chunk(wav_bytes: bytes, locale: str, chunk_idx: int) -> str:
    """Send a single WAV PCM chunk to Azure STT REST and return the Display text."""
    speech_token = _get_speech_token()

    response = httpx.post(
        _STT_URL,
        params={"language": locale, "format": "detailed", "profanity": "raw"},
        headers={
            "Authorization": f"Bearer {speech_token}",
            "Content-Type": "audio/wav; codecs=audio/pcm; samplerate=16000",
            "Accept": "application/json",
        },
        content=wav_bytes,
        timeout=120,
    )

    if response.status_code == 200:
        body = response.json()
        status = body.get("RecognitionStatus", "")
        duration_s = body.get("Duration", 0) / 10_000_000
        print(f"[STT] Chunk {chunk_idx}: Status={status}  Duration={duration_s:.1f}s", flush=True)
        if status == "Success" and body.get("NBest"):
            return body["NBest"][0].get("Display", "")
        return ""
    else:
        print(f"[STT] Chunk {chunk_idx}: HTTP {response.status_code}: {response.text[:400]}", flush=True)
        response.raise_for_status()
        return ""


def transcribe_mp3(mp3_bytes: bytes, locale: str = "en-US") -> str:
    """
    Transcribes an MP3 audio file to text using Azure Speech REST API.
    Converts MP3 to 16kHz WAV PCM first, then splits into ≤55s chunks to
    stay within the Azure STT REST API 60-second per-request limit.
    Returns the full recognised text, or empty string on silence/failure.

    locale: BCP-47 locale matching the spoken language e.g. 'en-US', 'fr-FR'.
    """
    if not mp3_bytes:
        return ""

    print(f"[STT] Converting {len(mp3_bytes):,} bytes MP3 to 16kHz WAV via ffmpeg...", flush=True)
    try:
        wav_bytes = _mp3_to_wav_pcm(mp3_bytes)
    except Exception as exc:
        print(f"[STT] ffmpeg conversion failed: {exc}", flush=True)
        return ""
    print(f"[STT] WAV PCM size: {len(wav_bytes):,} bytes — splitting into {_CHUNK_SECONDS}s chunks", flush=True)

    try:
        chunks = _split_wav_chunks(wav_bytes)
    except Exception as exc:
        print(f"[STT] Chunk split failed: {exc}", flush=True)
        return ""

    print(f"[STT] {len(chunks)} chunk(s) to transcribe  locale={locale}", flush=True)

    parts = []
    for i, chunk in enumerate(chunks):
        try:
            text = _transcribe_wav_chunk(chunk, locale, i)
            if text:
                parts.append(text)
        except Exception as exc:
            print(f"[STT] Chunk {i} error: {exc}", flush=True)

    full_text = " ".join(parts)
    print(f"[STT] OK — '{full_text[:100]}'", flush=True)
    return full_text
