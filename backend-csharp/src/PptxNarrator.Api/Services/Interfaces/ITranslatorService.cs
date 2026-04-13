namespace PptxNarrator.Api.Services.Interfaces;

public interface ITranslatorService
{
    /// <summary>
    /// Translate <paramref name="text"/> to the language implied by the TTS voice name.
    /// English voices are passed through unchanged.
    /// </summary>
    Task<string> TranslateForVoiceAsync(string text, string voice, CancellationToken ct = default);
}
