namespace PptxNarrator.Api.Services.Interfaces;

public interface IPptxScriptParserService
{
    /// <summary>Extract per-slide narration text from a .pptx script file.</summary>
    IReadOnlyList<SlideInfo> ExtractSlides(byte[] pptxBytes);
}
