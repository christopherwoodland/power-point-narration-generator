namespace PptxNarrator.Api.Services.Interfaces;

public delegate void ProgressCallback(int slideNum, int total, string phase);

public interface IAiPptxGeneratorService
{
    /// <summary>Build a complete PPTX from scratch using GPT structuring and DALL-E images.</summary>
    Task<byte[]> BuildPresentationAsync(
        IReadOnlyList<SlideInfo> slides,
        ProgressCallback? onProgress = null,
        CancellationToken ct = default);
}
