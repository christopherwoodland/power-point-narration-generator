using System.Collections.Concurrent;
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

    private sealed record SlideSynthesisRequest(int SourceSlideIndex, int TargetSlideIndex, string Text);
    private sealed record SlideSynthesisFailure(int SourceSlideIndex, int TargetSlideIndex, string Message);

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
        upload_files_message = _opts.UploadFilesMessage,
        tts_mode = _opts.AzureTtsMode,
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

        List<SlideSynthesisRequest> requests;
        try
        {
            requests = BuildSynthesisRequests(mapping, slides, pptxSlideCount);
        }
        catch (ArgumentException ex)
        {
            return UnprocessableEntity(new { detail = ex.Message });
        }

        byte[]?[] slideAudio;
        try
        {
            slideAudio = await SynthesizeSlidesAsync(requests, voice, pptxSlideCount, progressTotal: pptxSlideCount, onProgress: null, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return StatusCode(502, new { detail = ex.Message });
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
            var responseWriteLock = new SemaphoreSlim(1, 1);

            void OnProgress(int slideNum, int total, string phase)
            {
                responseWriteLock.Wait(ct);
                try
                {
                    Response.WriteAsync(JsonSerializer.Serialize(new
                    {
                        type = "progress",
                        slide = slideNum,
                        total,
                        phase,
                        message = $"{phase} slide {slideNum} of {total}…"
                    }) + "\n", ct).GetAwaiter().GetResult();
                    Response.Body.FlushAsync(ct).GetAwaiter().GetResult();
                }
                finally
                {
                    responseWriteLock.Release();
                }
            };

            async Task SendTtsProgressAsync(int slideNum, int total)
            {
                await responseWriteLock.WaitAsync(ct);
                try
                {
                    await Response.WriteAsync(JsonSerializer.Serialize(new
                    {
                        type = "progress",
                        slide = slideNum,
                        total,
                        phase = "tts",
                        message = $"tts slide {slideNum} of {total}…"
                    }) + "\n", ct);
                    await Response.Body.FlushAsync(ct);
                }
                finally
                {
                    responseWriteLock.Release();
                }
            }

            var pptxBytes = await _aiGenerator.BuildPresentationAsync(slides, OnProgress, ct);

            var requests = BuildSequentialSynthesisRequests(slides);
            var slideAudio = await SynthesizeSlidesAsync(
                requests,
                voice,
                slides.Count,
                slides.Count,
                SendTtsProgressAsync,
                ct);

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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Use CancellationToken.None so a previously-cancelled ct doesn't prevent
            // the error event from being flushed to the client.
            try
            {
                await Response.WriteAsync(
                    System.Text.Json.JsonSerializer.Serialize(new { type = "error", message = ex.Message }) + "\n",
                    CancellationToken.None);
                await Response.Body.FlushAsync(CancellationToken.None);
            }
            catch { /* best-effort — connection may already be gone */ }
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

    private static List<SlideSynthesisRequest> BuildSynthesisRequests(
        IReadOnlyDictionary<int, int> mapping,
        IReadOnlyList<SlideInfo> slides,
        int pptxSlideCount)
    {
        var requests = new List<SlideSynthesisRequest>();
        var claimedTargets = new HashSet<int>();

        foreach (var (wordIdx, pptxIdx) in mapping.OrderBy(pair => pair.Key))
        {
            if (wordIdx >= slides.Count || pptxIdx >= pptxSlideCount)
                continue;

            var text = slides[wordIdx].Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(text))
                continue;

            if (!claimedTargets.Add(pptxIdx))
                throw new ArgumentException(
                    $"Multiple script slides map to PowerPoint slide {pptxIdx + 1}. Each PowerPoint slide can only have one narration track.");

            requests.Add(new SlideSynthesisRequest(wordIdx, pptxIdx, text));
        }

        return requests;
    }

    private static List<SlideSynthesisRequest> BuildSequentialSynthesisRequests(IReadOnlyList<SlideInfo> slides)
    {
        var requests = new List<SlideSynthesisRequest>();

        for (int i = 0; i < slides.Count; i++)
        {
            var text = slides[i].Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(text))
                continue;

            requests.Add(new SlideSynthesisRequest(i, i, text));
        }

        return requests;
    }

    private async Task<byte[]?[]> SynthesizeSlidesAsync(
        IReadOnlyList<SlideSynthesisRequest> requests,
        string voice,
        int outputSlideCount,
        int progressTotal,
        Func<int, int, Task>? onProgress,
        CancellationToken ct)
    {
        var slideAudio = new byte[]?[outputSlideCount];
        if (requests.Count == 0)
            return slideAudio;

        var failures = new ConcurrentQueue<SlideSynthesisFailure>();

        await Parallel.ForEachAsync(
            requests,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = _opts.TtsMaxParallelism,
                CancellationToken = ct,
            },
            async (request, itemCt) =>
            {
                try
                {
                    _log.LogInformation("[TTS] Synthesising slide {W}→{P}", request.SourceSlideIndex + 1, request.TargetSlideIndex + 1);

                    var translated = await _translator.TranslateForVoiceAsync(request.Text, voice, itemCt);
                    slideAudio[request.TargetSlideIndex] = await _tts.SynthesizeToMp3Async(translated, voice, itemCt);

                    if (onProgress is not null)
                        await onProgress(request.SourceSlideIndex + 1, progressTotal);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failures.Enqueue(new SlideSynthesisFailure(
                        request.SourceSlideIndex,
                        request.TargetSlideIndex,
                        ex.Message));

                    _log.LogError(ex, "[TTS] Failed for slide {W}→{P}", request.SourceSlideIndex + 1, request.TargetSlideIndex + 1);
                }
            });

        if (failures.IsEmpty)
            return slideAudio;

        var failure = failures.OrderBy(item => item.SourceSlideIndex).First();
        throw new InvalidOperationException($"TTS failed for slide {failure.SourceSlideIndex + 1}: {failure.Message}");
    }

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
