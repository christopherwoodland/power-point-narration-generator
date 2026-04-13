namespace PptxNarrator.Api.Models;

public record QualityCheckResult(
    int SlideNum,
    string Title,
    double Confidence,
    IReadOnlyList<string> Issues
);
