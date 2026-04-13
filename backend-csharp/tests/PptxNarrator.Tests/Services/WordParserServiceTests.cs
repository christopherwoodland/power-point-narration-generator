using System.IO;
using System.IO.Compression;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace PptxNarrator.Tests.Services;

public class WordParserServiceTests
{
    private readonly WordParserService _sut = new(NullLogger<WordParserService>.Instance);

    [Fact]
    public void ExtractSlides_WithHeading1Delimiters_ReturnsSlidesGroupedByHeading()
    {
        var docx = CreateDocxWithHeadings(
        [
            ("Welcome slide", "Heading1", null),
            ("Body text for slide 1", "Normal", null),
            ("Second Slide Title", "Heading1", null),
            ("Body text for slide 2", "Normal", null),
        ]);

        var result = _sut.ExtractSlides(docx);

        result.Should().HaveCount(2);
        result[0].Title.Should().Be("Welcome slide");
        result[0].Text.Should().Contain("Body text for slide 1");
        result[1].Title.Should().Be("Second Slide Title");
        result[1].Text.Should().Contain("Body text for slide 2");
    }

    [Fact]
    public void ExtractSlides_WithSlideNPrefix_ReturnsGroupedSlides()
    {
        var docx = CreateDocxWithHeadings(
        [
            ("Slide 1 - Introduction", "Normal", null),
            ("This is the intro text.", "Normal", null),
            ("Slide 2 - Details", "Normal", null),
            ("These are the details.", "Normal", null),
        ]);

        var result = _sut.ExtractSlides(docx);

        result.Should().HaveCount(2);
        result[0].Title.Should().Be("Slide 1 - Introduction");
        result[0].Text.Should().Be("This is the intro text.");
        result[1].Title.Should().Be("Slide 2 - Details");
    }

    [Fact]
    public void ExtractSlides_EmptyDocument_ReturnsEmptyList()
    {
        var docx = CreateDocxWithHeadings([]);

        var result = _sut.ExtractSlides(docx);

        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractSlides_SlideWithNoBodyText_IncludesEmptyText()
    {
        var docx = CreateDocxWithHeadings(
        [
            ("Slide 1 Header", "Heading1", null),
            ("Slide 2 Header", "Heading1", null),
        ]);

        var result = _sut.ExtractSlides(docx);

        result.Should().HaveCount(2);
        result[0].Text.Should().BeEmpty();
    }

    [Fact]
    public void ExtractSlides_HeadingTakesPrecedenceOverSlideNPrefix()
    {
        // When Heading1 styles exist, "Slide N" prefix should NOT be treated as delimiter
        var docx = CreateDocxWithHeadings(
        [
            ("My Heading", "Heading1", null),
            ("Slide 2 - This is body text, not a delimiter", "Normal", null),
        ]);

        var result = _sut.ExtractSlides(docx);

        result.Should().HaveCount(1);
        result[0].Text.Should().Contain("Slide 2");
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>Creates a .docx byte array in memory with the given paragraphs.</summary>
    private static byte[] CreateDocxWithHeadings(
        IEnumerable<(string Text, string Style, string? Dummy)> paragraphs)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body());

            foreach (var (text, style, _) in paragraphs)
            {
                if (string.IsNullOrWhiteSpace(text)) continue;

                var para = new Paragraph();
                if (style != "Normal")
                {
                    para.AppendChild(new ParagraphProperties(
                        new ParagraphStyleId { Val = style }));
                }
                para.AppendChild(new Run(new Text(text)));
                mainPart.Document.Body!.AppendChild(para);
            }

            mainPart.Document.Save();
        }
        return ms.ToArray();
    }
}
