using System.Xml;

namespace PptxNarrator.Api.Services;

/// <summary>
/// Quality check agent: extracts audio from narrated PPTX, transcribes via STT,
/// then uses GPT to compare transcriptions against original script text.
/// </summary>
public sealed class QualityCheckerService : IQualityCheckerService
{
    private const string CogScope = "https://cognitiveservices.azure.com/.default";
    private const string AudioRelType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/audio";

    private static readonly XNamespace RelNs =
        "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace ANs =
        "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace PNs =
        "http://schemas.openxmlformats.org/presentationml/2006/main";

    private readonly ISttService _stt;
    private readonly IHttpClientFactory _http;
    private readonly TokenCredential _credential;
    private readonly AppOptions _opts;
    private readonly ILogger<QualityCheckerService> _log;

    public QualityCheckerService(ISttService stt, IHttpClientFactory http,
        TokenCredential credential, IOptions<AppOptions> opts,
        ILogger<QualityCheckerService> log)
    {
        _stt = stt;
        _http = http;
        _credential = credential;
        _opts = opts.Value;
        _log = log;
    }

    public async Task<IReadOnlyList<QualityCheckResult>> RunAsync(
        byte[] pptxBytes,
        IReadOnlyList<SlideInfo> scriptSlides,
        string voice = "en-US-JennyNeural",
        CancellationToken ct = default)
    {
        var locale = TranslatorService.LocaleFromVoice(voice);
        var slideData = ExtractSlidesFromPptx(pptxBytes);
        var results = new List<QualityCheckResult>();

        for (int i = 0; i < scriptSlides.Count; i++)
        {
            var scriptText = scriptSlides[i].Text?.Trim() ?? "";
            if (!slideData.TryGetValue(i + 1, out var data) || data.Audio is null)
            {
                results.Add(new QualityCheckResult(i + 1, scriptSlides[i].Title, 1.0, []));
                continue;
            }

            _log.LogInformation("[QA] Transcribing slide {N}", i + 1);
            string transcription;
            try
            {
                transcription = await _stt.TranscribeAsync(data.Audio, locale, ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "[QA] STT failed for slide {N}", i + 1);
                results.Add(new QualityCheckResult(i + 1, data.Title, 0.0,
                    ["STT transcription failed: " + ex.Message]));
                continue;
            }

            _log.LogInformation("[QA] Comparing slide {N} via GPT", i + 1);
            var analysis = await CompareWithGptAsync(scriptText, transcription, i + 1, ct);
            results.Add(new QualityCheckResult(i + 1, data.Title, analysis.Confidence, analysis.Issues));
        }

        return results;
    }

    private record GptAnalysis(double Confidence, IReadOnlyList<string> Issues);

    private async Task<GptAnalysis> CompareWithGptAsync(
        string script, string transcription, int slideNum, CancellationToken ct)
    {
        const string system = """
            You are a narration quality checker. Compare an original script against an
            audio transcription of that script. Respond ONLY with valid JSON:
            {
              "confidence": <0.0-1.0>,
              "issues": ["<issue 1>", ...]
            }
            confidence = 1.0 means perfect match. Issues should be concise and actionable.
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
                new { role = "user", content = $"Slide {slideNum}\n\nOriginal:\n{script}\n\nTranscription:\n{transcription}" }
            },
            temperature = 0.2,
            max_completion_tokens = 400,
            response_format = new { type = "json_object" }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Add("Authorization", $"Bearer {aad.Token}");
        req.Content = new StringContent(
            JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var resp = await _http.CreateClient("openai").SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var content = doc.RootElement
            .GetProperty("choices")[0].GetProperty("message").GetProperty("content")
            .GetString() ?? "{}";

        using var result = JsonDocument.Parse(content);
        var confidence = result.RootElement.TryGetProperty("confidence", out var c)
            ? c.GetDouble() : 1.0;
        var issues = result.RootElement.TryGetProperty("issues", out var issArr)
            ? issArr.EnumerateArray().Select(v => v.GetString() ?? "").ToList()
            : new List<string>();

        return new GptAnalysis(confidence, issues);
    }

    // ── ZIP extraction helpers ────────────────────────────────────────────

    private record SlideData(string Title, byte[]? Audio);

    private static Dictionary<int, SlideData> ExtractSlidesFromPptx(byte[] pptxBytes)
    {
        var result = new Dictionary<int, SlideData>();

        using var ms = new MemoryStream(pptxBytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var members = zip.Entries.Select(e => e.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        int slideNum = 1;
        while (true)
        {
            var slidePath = $"ppt/slides/slide{slideNum}.xml";
            if (!members.Contains(slidePath)) break;

            var slideEntry = zip.GetEntry(slidePath)!;
            using var ss = slideEntry.Open();
            var slideDoc = XDocument.Load(ss);
            var title = GetSlideTitle(slideDoc);
            var audio = ExtractAudio(zip, slideNum, members);

            result[slideNum] = new SlideData(
                string.IsNullOrEmpty(title) ? $"Slide {slideNum}" : title, audio);
            slideNum++;
        }

        return result;
    }

    private static string GetSlideTitle(XDocument doc)
    {
        foreach (var sp in doc.Descendants(PNs + "sp"))
        {
            var ph = sp.Descendants(PNs + "ph").FirstOrDefault();
            if (ph is null) continue;
            var phType = ph.Attribute("type")?.Value ?? "";
            if (phType is "title" or "ctrTitle")
            {
                var text = string.Concat(sp.Descendants(ANs + "t").Select(t => t.Value ?? "")).Trim();
                if (!string.IsNullOrEmpty(text)) return text;
            }
        }
        return "";
    }

    private static byte[]? ExtractAudio(ZipArchive zip, int slideNum, HashSet<string> members)
    {
        var relsPath = $"ppt/slides/_rels/slide{slideNum}.xml.rels";
        if (!members.Contains(relsPath)) return null;

        var relsEntry = zip.GetEntry(relsPath)!;
        using var rs = relsEntry.Open();
        var relsDoc = XDocument.Load(rs);

        var audioRel = relsDoc.Root?.Elements(RelNs + "Relationship")
            .FirstOrDefault(r => r.Attribute("Type")?.Value == AudioRelType);
        if (audioRel is null) return null;

        var target = audioRel.Attribute("Target")?.Value;
        if (string.IsNullOrEmpty(target)) return null;

        var mediaPath = ResolveRelTarget(target, slideNum);
        if (!members.Contains(mediaPath)) return null;

        var mediaEntry = zip.GetEntry(mediaPath)!;
        using var ms = mediaEntry.Open();
        using var buf = new MemoryStream();
        ms.CopyTo(buf);
        return buf.ToArray();
    }

    private static string ResolveRelTarget(string target, int slideNum)
    {
        if (target.StartsWith('/')) return target.TrimStart('/');
        var parts = $"ppt/slides/{target}".Replace('\\', '/').Split('/');
        var resolved = new List<string>();
        foreach (var p in parts)
        {
            if (p == "..") { if (resolved.Count > 0) resolved.RemoveAt(resolved.Count - 1); }
            else if (p is not ("" or ".")) resolved.Add(p);
        }
        return string.Join("/", resolved);
    }
}
