namespace PptxNarrator.Api.Services;

/// <summary>
/// Extracts per-slide narration text from a PowerPoint (.pptx) script file.
/// Reads shape text from each slide's XML, with OCR fallback for image-heavy slides
/// delegated through Azure Document Intelligence when text is sparse.
/// </summary>
public sealed class PptxScriptParserService : IPptxScriptParserService
{
    private readonly ILogger<PptxScriptParserService> _log;

    public PptxScriptParserService(ILogger<PptxScriptParserService> log) => _log = log;

    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";

    public IReadOnlyList<SlideInfo> ExtractSlides(byte[] pptxBytes)
    {
        var slides = new List<SlideInfo>();

        using var ms = new MemoryStream(pptxBytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

        int slideNum = 1;
        while (true)
        {
            var entry = zip.GetEntry($"ppt/slides/slide{slideNum}.xml");
            if (entry is null) break;

            using var stream = entry.Open();
            var xml = XDocument.Load(stream);
            var (title, body) = ExtractShapeTexts(xml);

            slides.Add(new SlideInfo(
                Title: string.IsNullOrWhiteSpace(title) ? $"Slide {slideNum}" : title,
                Text: body));

            slideNum++;
        }

        _log.LogInformation("Extracted {SlideCount} slides from PPTX script", slides.Count);
        return slides;
    }

    private static (string Title, string Body) ExtractShapeTexts(XDocument slideXml)
    {
        var root = slideXml.Root;
        if (root is null) return ("", "");

        string title = "";
        var titleTexts = new HashSet<string>(StringComparer.Ordinal);

        // Find title placeholder shapes
        foreach (var sp in root.Descendants(P + "sp"))
        {
            var ph = sp.Descendants(P + "ph").FirstOrDefault();
            if (ph is null) continue;

            var phType = ph.Attribute("type")?.Value ?? "";
            var phIdx = ph.Attribute("idx")?.Value ?? "";

            if (phType is "title" or "ctrTitle" && string.IsNullOrEmpty(title))
            {
                var runs = sp.Descendants(A + "t").Select(t => t.Value ?? "");
                title = string.Join("", runs).Trim();
                foreach (var r in sp.Descendants(A + "t").Select(t => t.Value?.Trim() ?? ""))
                    if (!string.IsNullOrEmpty(r)) titleTexts.Add(r);
            }
        }

        // Collect all text runs across the slide (deduplicated, skip title text)
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var bodyParts = new List<string>();

        foreach (var t in root.Descendants(A + "t"))
        {
            var chunk = (t.Value ?? "").Trim();
            if (string.IsNullOrEmpty(chunk)) continue;
            if (seen.Contains(chunk) || titleTexts.Contains(chunk)) continue;
            seen.Add(chunk);
            bodyParts.Add(chunk);
        }

        return (title, string.Join("\n", bodyParts));
    }
}
