using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace PptxNarrator.Api.Services;

/// <summary>
/// Parses a Word (.docx) document into per-slide text blocks.
/// Delimiter strategy:
///   1. Paragraphs styled "Heading1" (Office XML style ID).
///   2. Fallback: paragraphs whose text starts with "Slide N" (case-insensitive).
/// </summary>
public sealed class WordParserService : IWordParserService
{
    private readonly ILogger<WordParserService> _log;

    public WordParserService(ILogger<WordParserService> log) => _log = log;

    private static readonly Regex SlideRe =
        new(@"^slide\s+\d+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IReadOnlyList<SlideInfo> ExtractSlides(byte[] docxBytes)
    {
        using var ms = new MemoryStream(docxBytes);
        using var doc = WordprocessingDocument.Open(ms, isEditable: false);

        var body = doc.MainDocumentPart?.Document.Body;
        if (body is null) return [];

        var paragraphs = body.Elements<Paragraph>().ToList();

        bool hasHeading1 = paragraphs.Any(p =>
            IsHeading1(p.ParagraphProperties?.ParagraphStyleId?.Val?.Value));

        var slides = new List<SlideInfo>();
        string? currentTitle = null;
        var lines = new List<string>();

        void Flush()
        {
            if (currentTitle is not null)
                slides.Add(new SlideInfo(currentTitle, string.Join("\n", lines).Trim()));
        }

        foreach (var para in paragraphs)
        {
            var text = para.InnerText.Trim();
            if (string.IsNullOrEmpty(text)) continue;

            bool isDelimiter = hasHeading1
                ? IsHeading1(para.ParagraphProperties?.ParagraphStyleId?.Val?.Value)
                : SlideRe.IsMatch(text);

            if (isDelimiter)
            {
                Flush();
                currentTitle = text;
                lines = [];
            }
            else if (currentTitle is not null)
            {
                lines.Add(text);
            }
        }

        Flush();
        _log.LogInformation("Extracted {SlideCount} slides from Word document", slides.Count);
        return slides;
    }

    private static bool IsHeading1(string? styleId) =>
        styleId is not null &&
        (styleId.Equals("Heading1", StringComparison.OrdinalIgnoreCase) ||
         styleId.Equals("heading1", StringComparison.OrdinalIgnoreCase) ||
         styleId.Equals("Heading 1", StringComparison.OrdinalIgnoreCase));
}
