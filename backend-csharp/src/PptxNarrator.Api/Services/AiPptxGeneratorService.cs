using System.Text.Json.Serialization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using A = DocumentFormat.OpenXml.Drawing;

namespace PptxNarrator.Api.Services;

/// <summary>
/// AI-powered PowerPoint generator.
/// For each slide: calls GPT to produce structured JSON (title, bullets, image_prompt),
/// then optionally generates a DALL-E image, and builds a clean PPTX using Open XML SDK.
/// </summary>
public sealed class AiPptxGeneratorService : IAiPptxGeneratorService
{
    private const string CogScope = "https://cognitiveservices.azure.com/.default";

    // Minimal OOXML theme required by every SlideMaster.
    // Includes 12 required colour slots, a basic font scheme, and a format scheme.
    private const string MinimalThemeXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<a:theme xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" name=\"Office Theme\">" +
        "<a:themeElements>" +
        "<a:clrScheme name=\"Office\">" +
        "<a:dk1><a:sysClr val=\"windowText\" lastClr=\"000000\"/></a:dk1>" +
        "<a:lt1><a:sysClr val=\"window\" lastClr=\"FFFFFF\"/></a:lt1>" +
        "<a:dk2><a:srgbClr val=\"44546A\"/></a:dk2>" +
        "<a:lt2><a:srgbClr val=\"E7E6E6\"/></a:lt2>" +
        "<a:accent1><a:srgbClr val=\"4472C4\"/></a:accent1>" +
        "<a:accent2><a:srgbClr val=\"ED7D31\"/></a:accent2>" +
        "<a:accent3><a:srgbClr val=\"A9D18E\"/></a:accent3>" +
        "<a:accent4><a:srgbClr val=\"FFC000\"/></a:accent4>" +
        "<a:accent5><a:srgbClr val=\"5A96C8\"/></a:accent5>" +
        "<a:accent6><a:srgbClr val=\"70AD47\"/></a:accent6>" +
        "<a:hlink><a:srgbClr val=\"0563C1\"/></a:hlink>" +
        "<a:folHlink><a:srgbClr val=\"954F72\"/></a:folHlink>" +
        "</a:clrScheme>" +
        "<a:fontScheme name=\"Office\">" +
        "<a:majorFont><a:latin typeface=\"Calibri Light\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:majorFont>" +
        "<a:minorFont><a:latin typeface=\"Calibri\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:minorFont>" +
        "</a:fontScheme>" +
        "<a:fmtScheme name=\"Office\">" +
        "<a:fillStyleLst>" +
        "<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>" +
        "<a:gradFill rotWithShape=\"1\"><a:gsLst>" +
        "<a:gs pos=\"0\"><a:schemeClr val=\"phClr\"><a:lumMod val=\"110000\"/><a:satMod val=\"105000\"/><a:tint val=\"67000\"/></a:schemeClr></a:gs>" +
        "<a:gs pos=\"50000\"><a:schemeClr val=\"phClr\"><a:lumMod val=\"105000\"/><a:satMod val=\"103000\"/><a:tint val=\"73000\"/></a:schemeClr></a:gs>" +
        "<a:gs pos=\"100000\"><a:schemeClr val=\"phClr\"><a:lumMod val=\"105000\"/><a:satMod val=\"109000\"/><a:tint val=\"81000\"/></a:schemeClr></a:gs>" +
        "</a:gsLst><a:lin ang=\"5400000\" scaled=\"0\"/></a:gradFill>" +
        "<a:gradFill rotWithShape=\"1\"><a:gsLst>" +
        "<a:gs pos=\"0\"><a:schemeClr val=\"phClr\"><a:satMod val=\"103000\"/><a:lumMod val=\"102000\"/><a:tint val=\"94000\"/></a:schemeClr></a:gs>" +
        "<a:gs pos=\"100000\"><a:schemeClr val=\"phClr\"><a:lumMod val=\"99000\"/><a:satMod val=\"120000\"/><a:shade val=\"78000\"/></a:schemeClr></a:gs>" +
        "</a:gsLst><a:lin ang=\"5400000\" scaled=\"0\"/></a:gradFill>" +
        "</a:fillStyleLst>" +
        "<a:lnStyleLst>" +
        "<a:ln w=\"6350\" cap=\"flat\" cmpd=\"sng\" algn=\"ctr\"><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:prstDash val=\"solid\"/><a:miter lim=\"800000\"/></a:ln>" +
        "<a:ln w=\"12700\" cap=\"flat\" cmpd=\"sng\" algn=\"ctr\"><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:prstDash val=\"solid\"/><a:miter lim=\"800000\"/></a:ln>" +
        "<a:ln w=\"19050\" cap=\"flat\" cmpd=\"sng\" algn=\"ctr\"><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:prstDash val=\"solid\"/><a:miter lim=\"800000\"/></a:ln>" +
        "</a:lnStyleLst>" +
        "<a:effectStyleLst>" +
        "<a:effectStyle><a:effectLst/></a:effectStyle>" +
        "<a:effectStyle><a:effectLst/></a:effectStyle>" +
        "<a:effectStyle><a:effectLst><a:outerShdw blurRad=\"57150\" dist=\"19050\" dir=\"5400000\" algn=\"ctr\" rotWithShape=\"0\"><a:srgbClr val=\"000000\"><a:alpha val=\"63000\"/></a:srgbClr></a:outerShdw></a:effectLst></a:effectStyle>" +
        "</a:effectStyleLst>" +
        "<a:bgFillStyleLst>" +
        "<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>" +
        "<a:solidFill><a:schemeClr val=\"phClr\"><a:tint val=\"95000\"/><a:satMod val=\"170000\"/></a:schemeClr></a:solidFill>" +
        "<a:gradFill rotWithShape=\"1\"><a:gsLst>" +
        "<a:gs pos=\"0\"><a:schemeClr val=\"phClr\"><a:tint val=\"93000\"/><a:satMod val=\"150000\"/><a:shade val=\"98000\"/><a:lumMod val=\"102000\"/></a:schemeClr></a:gs>" +
        "<a:gs pos=\"100000\"><a:schemeClr val=\"phClr\"><a:shade val=\"63000\"/><a:satMod val=\"120000\"/></a:schemeClr></a:gs>" +
        "</a:gsLst><a:lin ang=\"5400000\" scaled=\"0\"/></a:gradFill>" +
        "</a:bgFillStyleLst>" +
        "</a:fmtScheme>" +
        "</a:themeElements>" +
        "</a:theme>";

    private readonly IHttpClientFactory _http;
    private readonly TokenCredential _credential;
    private readonly AppOptions _opts;
    private readonly ILogger<AiPptxGeneratorService> _log;

    public AiPptxGeneratorService(IHttpClientFactory http, TokenCredential credential,
        IOptions<AppOptions> opts, ILogger<AiPptxGeneratorService> log)
    {
        _http = http;
        _credential = credential;
        _opts = opts.Value;
        _log = log;
    }

    public async Task<byte[]> BuildPresentationAsync(
        IReadOnlyList<SlideInfo> slides,
        ProgressCallback? onProgress = null,
        CancellationToken ct = default)
    {
        // Structure all slides via GPT
        var structured = new List<SlideStructure>();
        for (int i = 0; i < slides.Count; i++)
        {
            onProgress?.Invoke(i + 1, slides.Count, "structure");
            var s = await StructureSlideAsync(slides[i].Text, i + 1, ct);
            structured.Add(s);
        }

        // Generate images
        var images = new Dictionary<int, byte[]>();
        for (int i = 0; i < structured.Count; i++)
        {
            if (!structured[i].IncludeImage || string.IsNullOrEmpty(structured[i].ImagePrompt))
                continue;
            onProgress?.Invoke(i + 1, slides.Count, "image");
            try
            {
                var img = await GenerateImageAsync(structured[i].ImagePrompt, ct);
                if (img is not null) images[i] = img;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Image generation failed for slide {N}", i + 1);
            }
        }

        // Build PPTX
        onProgress?.Invoke(0, slides.Count, "build");
        return BuildPptx(structured, images);
    }

    // ── GPT structuring ───────────────────────────────────────────────────

    private record SlideStructure(
        string Title, IReadOnlyList<string> Bullets,
        bool IncludeImage, string ImagePrompt);

    private async Task<SlideStructure> StructureSlideAsync(string narration, int slideNum, CancellationToken ct)
    {
        const string system = """
            You are a professional presentation designer.
            Given a narration script for one slide, return JSON:
            {
              "title": "<max 12 words>",
              "bullets": ["<point 1>","<point 2>","<point 3>"],
              "include_image": <bool>,
              "image_prompt": "<DALL-E prompt or empty>"
            }
            Rules: 2-4 bullets, include_image=true for content slides.
            Respond ONLY with valid JSON, no markdown fences.
            """;

        var tokenCtx = new TokenRequestContext(new[] { CogScope });
        var aad = await _credential.GetTokenAsync(tokenCtx, ct);

        var url = $"{_opts.AzureOpenAiEndpoint.TrimEnd('/')}/openai/deployments/{_opts.AzureOpenAiDeployment}" +
                  $"/chat/completions?api-version={_opts.ChatApiVersion}";

        var payload = new
        {
            messages = new[]
            {
                new { role = "system", content = system },
                new { role = "user", content = $"Slide {slideNum} narration:\n\n{narration}" }
            },
            temperature = 0.4,
            max_completion_tokens = 600,
            response_format = new { type = "json_object" }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Add("Authorization", $"Bearer {aad.Token}");
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var resp = await _http.CreateClient("openai").SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var content = doc.RootElement
            .GetProperty("choices")[0].GetProperty("message").GetProperty("content")
            .GetString() ?? "{}";

        using var result = JsonDocument.Parse(content);
        var title = result.RootElement.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
        var bullets = result.RootElement.TryGetProperty("bullets", out var b)
            ? b.EnumerateArray().Select(v => v.GetString() ?? "").ToList()
            : new List<string>();
        var includeImage = result.RootElement.TryGetProperty("include_image", out var ii) && ii.GetBoolean();
        var imagePrompt = result.RootElement.TryGetProperty("image_prompt", out var ip) ? ip.GetString() ?? "" : "";

        return new SlideStructure(title, bullets, includeImage, imagePrompt);
    }

    private async Task<byte[]?> GenerateImageAsync(string prompt, CancellationToken ct)
    {
        var tokenCtx = new TokenRequestContext(new[] { CogScope });
        var aad = await _credential.GetTokenAsync(tokenCtx, ct);

        string url;
        object payload;

        if (!string.IsNullOrWhiteSpace(_opts.AzureImageEndpoint))
        {
            // MAI endpoint: /mai/v1/images/generations — model specified in body
            url = $"{_opts.AzureImageEndpoint.TrimEnd('/')}/mai/v1/images/generations";
            payload = new
            {
                prompt,
                width = 1024,
                height = 1024,
                n = 1,
                model = _opts.AzureImageDeployment
            };
        }
        else
        {
            // Azure OpenAI endpoint: /openai/deployments/{model}/images/generations
            url = $"{_opts.AzureOpenAiEndpoint.TrimEnd('/')}/openai/deployments/{_opts.AzureImageDeployment}" +
                  $"/images/generations?api-version={_opts.ImageApiVersion}";
            payload = new { prompt, size = "1024x1024", response_format = "b64_json" };
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Add("Authorization", $"Bearer {aad.Token}");
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var resp = await _http.CreateClient("openai").SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var b64 = doc.RootElement.GetProperty("data")[0].GetProperty("b64_json").GetString();
        return b64 is not null ? Convert.FromBase64String(b64) : null;
    }

    // ── PPTX builder ──────────────────────────────────────────────────────

    private static byte[] BuildPptx(IReadOnlyList<SlideStructure> slides, Dictionary<int, byte[]> images)
    {
        var ms = new MemoryStream();
        using (var prs = PresentationDocument.Create(ms, PresentationDocumentType.Presentation, true))
        {
            var presPart = prs.AddPresentationPart();
            presPart.Presentation = new Presentation();

            var slideIdList = new SlideIdList();
            presPart.Presentation.AppendChild(slideIdList);
            presPart.Presentation.AppendChild(new SlideSize { Cx = (int)9144000, Cy = (int)5143500 });
            presPart.Presentation.AppendChild(new NotesSize { Cx = 6858000L, Cy = 9144000L });

            // Add slide master (minimal)
            var masterPart = presPart.AddNewPart<SlideMasterPart>();
            masterPart.SlideMaster = BuildMinimalSlideMaster();

            // Theme — required by SlideMaster; without it PowerPoint repairs the file.
            var themePart = masterPart.AddNewPart<ThemePart>();
            using (var ts = themePart.GetStream(System.IO.FileMode.Create, System.IO.FileAccess.Write))
            {
                var themeBytes = Encoding.UTF8.GetBytes(MinimalThemeXml);
                ts.Write(themeBytes, 0, themeBytes.Length);
            }

            var layoutPart = masterPart.AddNewPart<SlideLayoutPart>();
            layoutPart.SlideLayout = BuildMinimalSlideLayout();

            // Layout must have a back-reference relationship to its SlideMaster;
            // without it PowerPoint repairs the file.
            layoutPart.AddPart(masterPart, "rId1");

            masterPart.SlideMaster.SlideLayoutIdList = new SlideLayoutIdList(
                new SlideLayoutId { Id = 2049U, RelationshipId = masterPart.GetIdOfPart(layoutPart) });
            masterPart.SlideMaster.Save();
            layoutPart.SlideLayout.Save();

            var masterRid = presPart.GetIdOfPart(masterPart);
            presPart.Presentation.AppendChild(new SlideMasterIdList(
                new SlideMasterId { Id = 2147483648U, RelationshipId = masterRid }));

            uint slideId = 256u;
            for (int i = 0; i < slides.Count; i++)
            {
                var slidePart = presPart.AddNewPart<SlidePart>();
                slidePart.Slide = BuildSlide(slides[i], i < images.Count ? images.GetValueOrDefault(i) : null);

                var layoutRid = slidePart.CreateRelationshipToPart(layoutPart);
                slidePart.Slide.Save();

                slideIdList.AppendChild(new SlideId
                {
                    Id = slideId++,
                    RelationshipId = presPart.GetIdOfPart(slidePart)
                });
            }

            presPart.Presentation.Save();
        }
        return ms.ToArray();
    }

    private static Slide BuildSlide(SlideStructure s, byte[]? imageBytes)
    {
        var slide = new Slide();
        var cSld = new CommonSlideData();
        var spTree = new ShapeTree(
            new NonVisualGroupShapeProperties(
                new NonVisualDrawingProperties { Id = 1, Name = "" },
                new NonVisualGroupShapeDrawingProperties(),
                new ApplicationNonVisualDrawingProperties()),
            new GroupShapeProperties(new A.TransformGroup()));
        cSld.AppendChild(spTree);
        slide.AppendChild(cSld);
        slide.AppendChild(new ColorMapOverride(new A.MasterColorMapping()));

        // Title
        spTree.AppendChild(CreateTextShape(2, "Title", s.Title,
            x: 457200, y: 274638, cx: 8229600, cy: 1143000, fontSize: 3600, bold: true));

        // Bullets
        spTree.AppendChild(CreateBulletShape(3, s.Bullets,
            x: 457200, y: 1600200,
            cx: imageBytes is null ? 8229600 : 4572000,
            cy: 3200400));

        return slide;
    }

    private static Shape CreateTextShape(uint id, string name, string text,
        long x, long y, long cx, long cy, int fontSize, bool bold)
    {
        var sp = new Shape();
        sp.AppendChild(new NonVisualShapeProperties(
            new NonVisualDrawingProperties { Id = id, Name = name },
            new NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
            new ApplicationNonVisualDrawingProperties(new PlaceholderShape { Type = PlaceholderValues.Title })));

        sp.AppendChild(new ShapeProperties(
            new A.Transform2D(
                new A.Offset { X = x, Y = y },
                new A.Extents { Cx = cx, Cy = cy }),
            new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }));

        var txBody = new TextBody(new A.BodyProperties(), new A.ListStyle(),
            new A.Paragraph(new A.Run(
                new A.RunProperties { FontSize = fontSize, Bold = bold },
                new A.Text(text))));
        sp.AppendChild(txBody);
        return sp;
    }

    private static Shape CreateBulletShape(uint id, IReadOnlyList<string> bullets,
        long x, long y, long cx, long cy)
    {
        var sp = new Shape();
        sp.AppendChild(new NonVisualShapeProperties(
            new NonVisualDrawingProperties { Id = id, Name = "Content" },
            new NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
            new ApplicationNonVisualDrawingProperties(new PlaceholderShape { Index = 1U })));

        sp.AppendChild(new ShapeProperties(
            new A.Transform2D(
                new A.Offset { X = x, Y = y },
                new A.Extents { Cx = cx, Cy = cy }),
            new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }));

        var txBody = new TextBody(new A.BodyProperties(), new A.ListStyle());
        foreach (var bullet in bullets)
        {
            txBody.AppendChild(new A.Paragraph(
                new A.ParagraphProperties { Level = 0 },
                new A.Run(new A.RunProperties { FontSize = 2000 }, new A.Text(bullet))));
        }
        sp.AppendChild(txBody);
        return sp;
    }

    private static SlideMaster BuildMinimalSlideMaster() =>
        new(new CommonSlideData(
                new ShapeTree(
                    new NonVisualGroupShapeProperties(
                        new NonVisualDrawingProperties { Id = 1, Name = "" },
                        new NonVisualGroupShapeDrawingProperties(),
                        new ApplicationNonVisualDrawingProperties()),
                    new GroupShapeProperties(new A.TransformGroup()))),
            new ColorMap
            {
                Background1 = A.ColorSchemeIndexValues.Light1,
                Text1 = A.ColorSchemeIndexValues.Dark1,
                Background2 = A.ColorSchemeIndexValues.Light2,
                Text2 = A.ColorSchemeIndexValues.Dark2,
                Accent1 = A.ColorSchemeIndexValues.Accent1,
                Accent2 = A.ColorSchemeIndexValues.Accent2,
                Accent3 = A.ColorSchemeIndexValues.Accent3,
                Accent4 = A.ColorSchemeIndexValues.Accent4,
                Accent5 = A.ColorSchemeIndexValues.Accent5,
                Accent6 = A.ColorSchemeIndexValues.Accent6,
                Hyperlink = A.ColorSchemeIndexValues.Hyperlink,
                FollowedHyperlink = A.ColorSchemeIndexValues.FollowedHyperlink
            });

    private static SlideLayout BuildMinimalSlideLayout() =>
        new(new CommonSlideData(
                new ShapeTree(
                    new NonVisualGroupShapeProperties(
                        new NonVisualDrawingProperties { Id = 1, Name = "" },
                        new NonVisualGroupShapeDrawingProperties(),
                        new ApplicationNonVisualDrawingProperties()),
                    new GroupShapeProperties(new A.TransformGroup()))),
            new ColorMapOverride(new A.MasterColorMapping()));
}
