using Microsoft.AspNetCore.Mvc;

namespace PptxNarrator.Api.Controllers;

[ApiController]
[Route("api")]
public class NarrationController : ControllerBase
{
    private static readonly byte[] OldDocMagic = [0xD0, 0xCF, 0x11, 0xE0];
    private static readonly byte[] EcmaMagic = [0x50, 0x4B, 0x03, 0x04];

    private readonly IWordParserService _wordParser;
    private readonly IPptxScriptParserService _pptxParser;
    private readonly ITtsService _tts;
    private readonly ITranslatorService _translator;
    private readonly IPptxBuilderService _pptxBuilder;
    private readonly IAiPptxGeneratorService _aiGenerator;
    private readonly IQualityCheckerService _qualityChecker;
    private readonly IVideoExporterService _videoExporter;
    private readonly AppOptions _opts;
    private readonly ILogger<NarrationController> _log;

    public NarrationController(
        IWordParserService wordParser,
        IPptxScriptParserService pptxParser,
        ITtsService tts,
        ITranslatorService translator,
        IPptxBuilderService pptxBuilder,
        IAiPptxGeneratorService aiGenerator,
        IQualityCheckerService qualityChecker,
        IVideoExporterService videoExporter,
        IOptions<AppOptions> opts,
        ILogger<NarrationController> log)
    {
        _wordParser = wordParser;
        _pptxParser = pptxParser;
        _tts = tts;
        _translator = translator;
        _pptxBuilder = pptxBuilder;
        _aiGenerator = aiGenerator;
        _qualityChecker = qualityChecker;
        _videoExporter = videoExporter;
        _opts = opts.Value;
        _log = log;
    }

    // ── GET /api/config ───────────────────────────────────────────────────

    [HttpGet("config")]
    public IActionResult GetConfig() => Ok(new
    {
        enable_quality_check = _opts.EnableQualityCheck,
        enable_ai_mode = _opts.EnableAiMode,
        enable_video_export = _opts.EnableVideoExport,
        banner_message = _opts.AppBannerMessage,
    });

    // ── POST /api/parse ───────────────────────────────────────────────────

    /// <summary>Step 1: parse script, return slide list.</summary>
    [HttpPost("parse")]
    [RequestSizeLimit(52_428_800)] // 50 MB
    public async Task<IActionResult> Parse(
        IFormFile script,
        IFormFile? pptx = null,
        [FromForm(Name = "ai_mode")] string aiModeStr = "false")
    {
        var scriptBytes = await ReadFormFileAsync(script);
        var aiMode = aiModeStr.Equals("true", StringComparison.OrdinalIgnoreCase);

        IReadOnlyList<SlideInfo> slides;
        try
        {
            slides = ParseScript(script.FileName, scriptBytes);
        }
        catch (ArgumentException ex)
        {
            return UnprocessableEntity(new { detail = ex.Message });
        }

        if (aiMode || pptx is null || string.IsNullOrEmpty(pptx.FileName))
        {
            return Ok(new ParseResponse(slides, slides.Count, slides.Count, true));
        }

        var pptxBytes = await ReadFormFileAsync(pptx);
        int pptxSlideCount = CountPptxSlides(pptxBytes);

        return Ok(new ParseResponse(slides, pptxSlideCount, slides.Count, false));
    }

    // ── POST /api/process ─────────────────────────────────────────────────

    /// <summary>Full pipeline: parse → TTS → embed audio → return PPTX.</summary>
    [HttpPost("process")]
    [RequestSizeLimit(104_857_600)] // 100 MB
    public async Task<IActionResult> Process(
        IFormFile script,
        IFormFile pptx,
        [FromForm] string voice = "en-US-JennyNeural",
        [FromForm(Name = "slide_mapping")] string slideMappingJson = "{}",
        CancellationToken ct = default)
    {
        var scriptBytes = await ReadFormFileAsync(script);
        var pptxBytes = await ReadFormFileAsync(pptx);

        IReadOnlyList<SlideInfo> slides;
        try { slides = ParseScript(script.FileName, scriptBytes); }
        catch (ArgumentException ex) { return UnprocessableEntity(new { detail = ex.Message }); }

        int pptxSlideCount = CountPptxSlides(pptxBytes);

        Dictionary<int, int> mapping;
        try { mapping = JsonSerializer.Deserialize<Dictionary<int, int>>(slideMappingJson) ?? []; }
        catch { mapping = []; }

        if (mapping.Count == 0)
            for (int i = 0; i < Math.Min(slides.Count, pptxSlideCount); i++)
                mapping[i] = i;

        // Per-PPTX-slide audio (null = no audio)
        var slideAudio = new byte[]?[pptxSlideCount];

        foreach (var (wordIdx, pptxIdx) in mapping)
        {
            if (wordIdx >= slides.Count || pptxIdx >= pptxSlideCount) continue;
            var text = slides[wordIdx].Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(text)) continue;

            _log.LogInformation("[Process] Synthesising slide {W}→{P}", wordIdx + 1, pptxIdx + 1);
            try
            {
                var translated = await _translator.TranslateForVoiceAsync(text, voice, ct);
                slideAudio[pptxIdx] = await _tts.SynthesizeToMp3Async(translated, voice, ct);
            }
            catch (Exception ex)
            {
                return StatusCode(502, new { detail = $"TTS failed for slide {wordIdx + 1}: {ex.Message}" });
            }
        }

        _log.LogInformation("[Process] Embedding audio — {N} slides have audio",
            slideAudio.Count(a => a is not null));

        var result = _pptxBuilder.EmbedAudio(pptxBytes, slideAudio);

        return File(result,
            "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            "narrated_presentation.pptx");
    }

    // ── POST /api/generate-ai ─────────────────────────────────────────────

    /// <summary>AI generation pipeline — streams newline-delimited JSON progress events.</summary>
    [HttpPost("generate-ai")]
    [RequestSizeLimit(52_428_800)]
    public async Task GenerateAi(
        IFormFile script,
        [FromForm] string voice = "en-US-JennyNeural",
        CancellationToken ct = default)
    {
        if (!_opts.EnableAiMode)
        {
            Response.StatusCode = 403;
            await Response.WriteAsync("{\"type\":\"error\",\"message\":\"AI mode is disabled.\"}\n", ct);
            return;
        }

        var scriptBytes = await ReadFormFileAsync(script);
        IReadOnlyList<SlideInfo> slides;
        try { slides = ParseScript(script.FileName, scriptBytes); }
        catch (ArgumentException ex)
        {
            Response.StatusCode = 422;
            await Response.WriteAsync($"{{\"type\":\"error\",\"message\":{JsonSerializer.Serialize(ex.Message)}}}\n", ct);
            return;
        }

        Response.ContentType = "application/x-ndjson";

        async Task Send(object obj) =>
            await Response.WriteAsync(JsonSerializer.Serialize(obj) + "\n", ct);

        try
        {
            void OnProgress(int slideNum, int total, string phase) =>
                Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    type = "progress",
                    slide = slideNum,
                    total,
                    phase,
                    message = $"{phase} slide {slideNum} of {total}…"
                }) + "\n", ct).GetAwaiter().GetResult();

            var pptxBytes = await _aiGenerator.BuildPresentationAsync(slides, OnProgress, ct);

            var slideAudio = new byte[]?[slides.Count];
            for (int i = 0; i < slides.Count; i++)
            {
                var text = slides[i].Text?.Trim() ?? "";
                if (string.IsNullOrEmpty(text)) continue;

                OnProgress(i + 1, slides.Count, "tts");
                try
                {
                    var translated = await _translator.TranslateForVoiceAsync(text, voice, ct);
                    slideAudio[i] = await _tts.SynthesizeToMp3Async(translated, voice, ct);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "TTS failed for slide {N}", i + 1);
                }
            }

            var result = _pptxBuilder.EmbedAudio(pptxBytes, slideAudio);
            await Send(new { type = "done", pptx = Convert.ToBase64String(result) });
        }
        catch (Exception ex)
        {
            await Send(new { type = "error", message = ex.Message });
        }
    }

    // ── POST /api/export-video ────────────────────────────────────────────

    /// <summary>Converts a narrated PPTX to MP4 — streams newline-delimited JSON progress events.</summary>
    [HttpPost("export-video")]
    [RequestSizeLimit(104_857_600)]
    public async Task ExportVideo(IFormFile pptx, CancellationToken ct = default)
    {
        if (!_opts.EnableVideoExport)
        {
            Response.StatusCode = 403;
            await Response.WriteAsync("{\"type\":\"error\",\"message\":\"Video export is disabled.\"}\n", ct);
            return;
        }

        var pptxBytes = await ReadFormFileAsync(pptx);
        Response.ContentType = "application/x-ndjson";

        try
        {
            await foreach (var ev in _videoExporter.ExportVideoAsync(pptxBytes, ct))
            {
                await Response.WriteAsync(
                    System.Text.Json.JsonSerializer.Serialize(ev,
                        new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }) + "\n",
                    ct);
                await Response.Body.FlushAsync(ct);
            }
        }
        catch (Exception ex)
        {
            await Response.WriteAsync(
                System.Text.Json.JsonSerializer.Serialize(new { type = "error", message = ex.Message }) + "\n", ct);
        }
    }

    // ── POST /api/quality-check ───────────────────────────────────────────

    [HttpPost("quality-check")]
    [RequestSizeLimit(104_857_600)]
    public async Task<IActionResult> QualityCheck(
        IFormFile script,
        IFormFile pptx,
        [FromForm] string voice = "en-US-JennyNeural",
        CancellationToken ct = default)
    {
        if (!_opts.EnableQualityCheck)
            return StatusCode(403, new { detail = "Quality check is disabled." });

        var scriptBytes = await ReadFormFileAsync(script);
        var pptxBytes = await ReadFormFileAsync(pptx);

        IReadOnlyList<SlideInfo> slides;
        try { slides = ParseScript(script.FileName, scriptBytes); }
        catch (ArgumentException ex) { return UnprocessableEntity(new { detail = ex.Message }); }

        try
        {
            var results = await _qualityChecker.RunAsync(pptxBytes, slides, voice, ct);
            return Ok(new { results });
        }
        catch (Exception ex)
        {
            return StatusCode(502, new { detail = $"Quality check failed: {ex.Message}" });
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private IReadOnlyList<SlideInfo> ParseScript(string filename, byte[] bytes)
    {
        try
        {
            return filename.EndsWith(".pptx", StringComparison.OrdinalIgnoreCase)
                ? _pptxParser.ExtractSlides(bytes)
                : _wordParser.ExtractSlides(bytes);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            if (bytes.Length >= 4 && bytes[..4].SequenceEqual(OldDocMagic))
                throw new ArgumentException(
                    $"'{filename}' appears to be a legacy .doc file. Please save as .docx and re-upload.");
            throw new ArgumentException($"Failed to parse '{filename}': {ex.Message}");
        }
    }

    private static int CountPptxSlides(byte[] pptxBytes)
    {
        using var ms = new MemoryStream(pptxBytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        int count = 0;
        while (zip.GetEntry($"ppt/slides/slide{count + 1}.xml") is not null) count++;
        return count;
    }

    private static async Task<byte[]> ReadFormFileAsync(IFormFile file)
    {
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        return ms.ToArray();
    }
}
