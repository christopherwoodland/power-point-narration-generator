"""
Diagnostic script: synthesize a short phrase to MP3 (24kHz), save it to disk,
then submit it to Azure STT and print the full response.

Run from the project root:
  $env:AZURE_SPEECH_RESOURCE_NAME = "bhs-development-public-foundry-r"
  $env:AZURE_SPEECH_REGION = "eastus2"
  .\.venv\Scripts\python.exe backend/test_stt.py

Also accepts a --file argument to test an existing MP3 file:
  .\.venv\Scripts\python.exe backend/test_stt.py --file path\to\audio.mp3
"""
import sys
import os
import time
import argparse
import pathlib
import httpx
from azure.identity import DefaultAzureCredential

# Ensure imports work when run directly
sys.path.insert(0, str(pathlib.Path(__file__).parent))
import tts_client as tts
import stt_client as stt

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--file", help="Path to an existing MP3 to test (skips TTS step)")
    parser.add_argument("--text", default="Welcome to this training course on flexible work arrangements. Audio quality check in progress.", help="Text to synthesize")
    args = parser.parse_args()

    if args.file:
        mp3_path = pathlib.Path(args.file)
        print(f"[TEST] Reading MP3 from: {mp3_path}")
        mp3_bytes = mp3_path.read_bytes()
        print(f"[TEST] File size: {len(mp3_bytes):,} bytes")
    else:
        print("[TEST] Synthesizing test phrase via Azure TTS...")
        print(f"[TEST] Text: {args.text}")
        mp3_bytes = tts.synthesize_to_mp3(args.text, voice="en-US-JennyNeural")
        print(f"[TEST] TTS produced {len(mp3_bytes):,} bytes")

        # Save to disk for inspection
        out_path = pathlib.Path(__file__).parent / "test_audio.mp3"
        out_path.write_bytes(mp3_bytes)
        print(f"[TEST] Saved to: {out_path}")

    # Check MP3 header
    if mp3_bytes[:3] == b'ID3':
        print(f"[TEST] MP3 has ID3 tag header (normal)")
    elif mp3_bytes[0:2] in (b'\xff\xfb', b'\xff\xfa', b'\xff\xf3', b'\xff\xf2'):
        print(f"[TEST] MP3 starts with sync frame (no ID3 tag)")
    else:
        print(f"[TEST] WARNING: Unexpected MP3 header bytes: {mp3_bytes[:4].hex()}")

    # Try to read sample rate from MP3 frame header (first MPEG frame)
    _print_mp3_info(mp3_bytes)

    # Run STT
    print("\n[TEST] Submitting to Azure STT...")
    region = os.environ.get("AZURE_SPEECH_REGION", "eastus2")
    stt_url = f"https://{region}.stt.speech.microsoft.com/speech/recognition/conversation/cognitiveservices/v1"

    speech_token = tts._get_speech_token()

    resp = httpx.post(
        stt_url,
        params={"language": "en-US", "format": "detailed", "profanity": "raw"},
        headers={
            "Authorization": f"Bearer {speech_token}",
            "Content-Type": "audio/mpeg",
            "Accept": "application/json",
        },
        content=mp3_bytes,
        timeout=120,
    )

    print(f"[TEST] HTTP {resp.status_code}")
    print(f"[TEST] Full response body:")
    print(resp.text)

    if resp.status_code == 200:
        body = resp.json()
        status = body.get("RecognitionStatus", "")
        nbest = body.get("NBest", [])
        display = nbest[0].get("Display", "") if nbest else ""
        duration_ticks = body.get("Duration", 0)
        duration_sec = duration_ticks / 10_000_000
        print(f"\n[TEST] Status:       {status}")
        print(f"[TEST] Duration:     {duration_sec:.2f}s  (raw ticks: {duration_ticks})")
        print(f"[TEST] Transcription: '{display}'")
    else:
        print(f"[TEST] Error: {resp.text}")


def _print_mp3_info(data: bytes):
    """
    Scan for the first MPEG audio frame header and print sample rate / bitrate.
    MPEG frame sync = 0xffe0 in first 11 bits.
    """
    SAMPLE_RATES = {
        (0b11, 0b00): 44100,
        (0b11, 0b01): 48000,
        (0b11, 0b10): 32000,
        (0b10, 0b00): 22050,
        (0b10, 0b01): 24000,
        (0b10, 0b10): 16000,
        (0b01, 0b00): 11025,
        (0b01, 0b01): 12000,
        (0b01, 0b10): 8000,
    }
    BITRATES_V1_L3 = [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0]
    BITRATES_V2_L3 = [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0]

    # Skip ID3 tag if present
    offset = 0
    if data[:3] == b'ID3':
        # ID3v2 header: 10 bytes, size in bytes 6-9 (syncsafe integer)
        sz = ((data[6] & 0x7f) << 21 | (data[7] & 0x7f) << 14 |
              (data[8] & 0x7f) << 7 | (data[9] & 0x7f))
        offset = 10 + sz

    for i in range(offset, min(offset + 4096, len(data) - 4)):
        b0, b1, b2, b3 = data[i], data[i+1], data[i+2], data[i+3]
        if b0 == 0xff and (b1 & 0xe0) == 0xe0:
            version  = (b1 >> 3) & 0b11  # 0b11=v1, 0b10=v2, 0b01=v2.5
            layer    = (b1 >> 1) & 0b11  # 0b01=L3
            bitrate_idx = (b2 >> 4) & 0x0f
            sr_idx   = (b2 >> 2) & 0b11
            channels = (b3 >> 6) & 0b11

            sr = SAMPLE_RATES.get((version, sr_idx), 0)
            br_table = BITRATES_V1_L3 if version == 0b11 else BITRATES_V2_L3
            br = br_table[bitrate_idx] if bitrate_idx < len(br_table) else 0

            ch_str = "Mono" if channels == 0b11 else "Stereo/Joint"
            ver_str = {0b11: "MPEG1", 0b10: "MPEG2", 0b01: "MPEG2.5"}.get(version, f"v{version}")
            print(f"[TEST] MP3 frame info: {ver_str} Layer{'IV'[layer-1:layer]} "
                  f"{sr}Hz  {br}kbps  {ch_str}")
            return

    print("[TEST] Could not find MPEG frame header to determine sample rate")


if __name__ == "__main__":
    main()
