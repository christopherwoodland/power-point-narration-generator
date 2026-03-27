"""
Azure Translator client using DefaultAzureCredential (no API keys).

Uses the Cognitive Services resource-specific endpoint:
  POST https://<resource>.cognitiveservices.azure.com/translator/text/v3.0/translate

The same AAD token used for TTS STS exchange works here too.

Pass the full TTS voice name (e.g. "fr-FR-DeniseNeural") to translate_for_voice(),
which extracts the locale and translates accordingly.
English locales (en-*) are passed through unchanged.
"""
import json
import os
import httpx
from azure_credential import get_credential

COGNITIVE_SERVICES_SCOPE = "https://cognitiveservices.azure.com/.default"

_resource_name = os.environ.get(
    "AZURE_SPEECH_RESOURCE_NAME", "bhs-development-public-foundry-r"
).strip()

_TRANSLATOR_URL = (
    f"https://{_resource_name}.cognitiveservices.azure.com"
    f"/translator/text/v3.0/translate"
)

# Map TTS locale → Azure Translator language code
# https://learn.microsoft.com/azure/ai-services/translator/language-support
_LOCALE_TO_TRANSLATOR: dict[str, str] = {
    "fr-FR": "fr",
    "es-ES": "es",
    "es-MX": "es",
    "de-DE": "de",
    "it-IT": "it",
    "pt-BR": "pt-br",
    "pt-PT": "pt-pt",
    "ja-JP": "ja",
    "zh-CN": "zh-Hans",
    "zh-TW": "zh-Hant",
    "ko-KR": "ko",
    "nl-NL": "nl",
    "pl-PL": "pl",
    "ru-RU": "ru",
    "ar-SA": "ar",
    "hi-IN": "hi",
    "sv-SE": "sv",
    "nb-NO": "nb",
    "da-DK": "da",
    "fi-FI": "fi",
    "tr-TR": "tr",
    "cs-CZ": "cs",
    "hu-HU": "hu",
}


def _locale_from_voice(voice: str) -> str:
    """Extract locale from a voice name like 'fr-FR-DeniseNeural' → 'fr-FR'."""
    parts = voice.split("-")
    if len(parts) >= 2:
        return f"{parts[0]}-{parts[1]}"
    return voice


def translate_for_voice(text: str, voice: str) -> str:
    """
    Translate text to the language implied by the TTS voice name.
    Returns the original text unchanged for English voices (en-*).
    """
    if not text.strip():
        return text

    locale = _locale_from_voice(voice)
    if locale.lower().startswith("en"):
        return text  # No translation needed

    lang_code = _LOCALE_TO_TRANSLATOR.get(locale)
    if not lang_code:
        # Fallback: use first part of locale (e.g. "fr" from "fr-XX")
        lang_code = locale.split("-")[0].lower()

    print(f"[Translate] {locale}  lang_code={lang_code}  chars={len(text)}", flush=True)

    aad_token = get_credential().get_token(COGNITIVE_SERVICES_SCOPE).token

    response = httpx.post(
        _TRANSLATOR_URL,
        params={"api-version": "3.0", "to": lang_code},
        headers={
            "Authorization": f"Bearer {aad_token}",
            "Content-Type": "application/json",
        },
        content=json.dumps([{"text": text}]),
        timeout=30,
    )
    response.raise_for_status()

    translated = response.json()[0]["translations"][0]["text"]
    print(f"[Translate] Done — {len(translated)} chars", flush=True)
    return translated
