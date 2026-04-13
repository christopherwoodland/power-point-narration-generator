namespace PptxNarrator.Api.Services.Interfaces;

public interface IWordParserService
{
    /// <summary>Extract per-slide text blocks from a .docx file.</summary>
    IReadOnlyList<SlideInfo> ExtractSlides(byte[] docxBytes);
}
