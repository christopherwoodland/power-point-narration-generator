namespace PptxNarrator.Api.Services.Interfaces;

public interface IQualityCheckerService
{
    /// <summary>
    /// For each slide in the narrated PPTX:
    ///   1. Extract embedded MP3.
    ///   2. Transcribe via Azure STT.
    ///   3. Compare with original script using GPT.
    /// Returns per-slide confidence scores and issue lists.
    /// </summary>
    Task<IReadOnlyList<QualityCheckResult>> RunAsync(
        byte[] pptxBytes,
        IReadOnlyList<SlideInfo> scriptSlides,
        string voice = "en-US-JennyNeural",
        CancellationToken ct = default);
}
