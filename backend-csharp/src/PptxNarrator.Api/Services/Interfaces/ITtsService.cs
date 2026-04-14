namespace PptxNarrator.Api.Services.Interfaces;

public interface ITtsService
{
    /// <summary>Synthesise <paramref name="text"/> to MP3 bytes using Azure Neural TTS.</summary>
    Task<byte[]> SynthesizeToMp3Async(string text, string voice, CancellationToken ct = default);
}
