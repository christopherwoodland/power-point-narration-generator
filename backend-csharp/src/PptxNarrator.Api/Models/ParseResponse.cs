namespace PptxNarrator.Api.Models;

public record ParseResponse(
    IReadOnlyList<SlideInfo> Slides,
    int PptxSlideCount,
    int WordSlideCount,
    bool AiMode
);
