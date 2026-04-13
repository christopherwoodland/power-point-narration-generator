namespace PptxNarrator.Api.Services.Interfaces;

public interface ISttService
{
    /// <summary>Transcribe MP3 bytes to text using Azure Speech STT.</summary>
    Task<string> TranscribeAsync(byte[] mp3Bytes, string languageLocale = "en-US", CancellationToken ct = default);
}
