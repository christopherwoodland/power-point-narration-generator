using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;
using PptxNarrator.Api.Configuration;
using PptxNarrator.Api.Controllers;
using PptxNarrator.Api.Models;
using PptxNarrator.Api.Services.Interfaces;

namespace PptxNarrator.Tests.Controllers;

public class NarrationControllerTests
{
    private readonly Mock<IWordParserService> _wordParser = new();
    private readonly Mock<IPptxScriptParserService> _pptxParser = new();
    private readonly Mock<ITtsService> _tts = new();
    private readonly Mock<ITranslatorService> _translator = new();
    private readonly Mock<IPptxBuilderService> _pptxBuilder = new();
    private readonly Mock<IAiPptxGeneratorService> _aiGen = new();
    private readonly Mock<IQualityCheckerService> _qc = new();
    private readonly Mock<IVideoExporterService> _videoExporter = new();

    private NarrationController BuildController(AppOptions? opts = null)
    {
        var options = Options.Create(opts ?? new AppOptions());
        return new NarrationController(
            _wordParser.Object, _pptxParser.Object, _tts.Object,
            _translator.Object, _pptxBuilder.Object, _aiGen.Object,
            _qc.Object, _videoExporter.Object, options, NullLogger<NarrationController>.Instance);
    }

    // ── GET /api/config ───────────────────────────────────────────────────

    [Fact]
    public void GetConfig_ReturnsFeatureFlags()
    {
        var ctrl = BuildController(new AppOptions
        {
            EnableQualityCheck = false,
            EnableAiMode = true,
            EnableVideoExport = false,
            UploadFilesMessage = "Custom upload helper text",
        });

        var result = ctrl.GetConfig() as OkObjectResult;

        result.Should().NotBeNull();
        var json = JsonSerializer.Serialize(result!.Value);
        json.Should().Contain("\"enable_quality_check\":false");
        json.Should().Contain("\"enable_ai_mode\":true");
        json.Should().Contain("\"upload_files_message\":\"Custom upload helper text\"");
    }

    // ── POST /api/parse ───────────────────────────────────────────────────

    [Fact]
    public async Task Parse_ValidDocx_ReturnsSlidesFromWordParser()
    {
        var slides = new[] { new SlideInfo("Slide 1", "Body text") };
        _wordParser.Setup(w => w.ExtractSlides(It.IsAny<byte[]>())).Returns(slides);

        var ctrl = BuildController();
        var scriptFile = CreateFormFile("script.docx", "docx content");
        var pptxFile = CreateFormFileWithSlides(2);

        var result = await ctrl.Parse(scriptFile, pptxFile) as OkObjectResult;

        result.Should().NotBeNull();
        var response = result!.Value as ParseResponse;
        response.Should().NotBeNull();
        response!.Slides.Should().HaveCount(1);
        response.PptxSlideCount.Should().Be(2);
    }

    [Fact]
    public async Task Parse_AiModeTrue_ReturnsSlidesCountAsBothCounts()
    {
        var slides = new[] { new SlideInfo("S1", "T1"), new SlideInfo("S2", "T2") };
        _wordParser.Setup(w => w.ExtractSlides(It.IsAny<byte[]>())).Returns(slides);

        var ctrl = BuildController();
        var scriptFile = CreateFormFile("script.docx", "docx content");

        var result = await ctrl.Parse(scriptFile, null, "true") as OkObjectResult;

        var response = result!.Value as ParseResponse;
        response!.AiMode.Should().BeTrue();
        response.PptxSlideCount.Should().Be(2);
        response.WordSlideCount.Should().Be(2);
    }

    [Fact]
    public async Task Parse_PptxScript_UsesPptxParser()
    {
        var slides = new[] { new SlideInfo("Slide 1", "PPTX body") };
        _pptxParser.Setup(p => p.ExtractSlides(It.IsAny<byte[]>())).Returns(slides);

        var ctrl = BuildController();
        var scriptFile = CreateFormFile("script.pptx", "zip content");

        var result = await ctrl.Parse(scriptFile) as OkObjectResult;

        _pptxParser.Verify(p => p.ExtractSlides(It.IsAny<byte[]>()), Times.Once);
        _wordParser.Verify(w => w.ExtractSlides(It.IsAny<byte[]>()), Times.Never);
    }

    // ── POST /api/process ───────────────────────────────────────────────

    [Fact]
    public async Task Process_WhenAnyRequiredSlideTtsFails_Returns502()
    {
        var slides = new[]
        {
            new SlideInfo("Slide 1", "First"),
            new SlideInfo("Slide 2", "Second")
        };
        _wordParser.Setup(w => w.ExtractSlides(It.IsAny<byte[]>())).Returns(slides);
        _translator.Setup(t => t.TranslateForVoiceAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string text, string _, CancellationToken _) => text);
        _tts.Setup(t => t.SynthesizeToMp3Async(
                It.Is<string>(text => text == "First"),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([0x01]);
        _tts.Setup(t => t.SynthesizeToMp3Async(
                It.Is<string>(text => text == "Second"),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var ctrl = BuildController(new AppOptions { TtsMaxParallelism = 2 });

        var result = await ctrl.Process(
            CreateFormFile("script.docx", "docx content"),
            CreateFormFile("deck.pptx", "", CreateMinimalPptxBytes(slideCount: 2))) as ObjectResult;

        result.Should().NotBeNull();
        result!.StatusCode.Should().Be(502);
        JsonSerializer.Serialize(result.Value).Should().Contain("TTS failed for slide 2");
        _pptxBuilder.Verify(p => p.EmbedAudio(It.IsAny<byte[]>(), It.IsAny<IReadOnlyList<byte[]?>>()), Times.Never);
    }

    [Fact]
    public async Task Process_WhenMultipleScriptSlidesMapToSamePptxSlide_Returns422()
    {
        var slides = new[]
        {
            new SlideInfo("Slide 1", "First"),
            new SlideInfo("Slide 2", "Second")
        };
        _wordParser.Setup(w => w.ExtractSlides(It.IsAny<byte[]>())).Returns(slides);

        var ctrl = BuildController();
        var mapping = JsonSerializer.Serialize(new Dictionary<int, int>
        {
            [0] = 0,
            [1] = 0,
        });

        var result = await ctrl.Process(
            CreateFormFile("script.docx", "docx content"),
            CreateFormFile("deck.pptx", "", CreateMinimalPptxBytes(slideCount: 2)),
            slideMappingJson: mapping) as ObjectResult;

        result.Should().NotBeNull();
        result!.StatusCode.Should().Be(422);
        JsonSerializer.Serialize(result.Value).Should().Contain("Multiple script slides map to PowerPoint slide 1");
        _tts.Verify(t => t.SynthesizeToMp3Async(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── POST /api/quality-check ───────────────────────────────────────────

    [Fact]
    public async Task QualityCheck_WhenDisabled_Returns403()
    {
        var ctrl = BuildController(new AppOptions { EnableQualityCheck = false });

        var result = await ctrl.QualityCheck(
            CreateFormFile("s.docx", ""), CreateFormFile("p.pptx", "")) as ObjectResult;

        result!.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task QualityCheck_WhenEnabled_ReturnsResults()
    {
        var slides = new[] { new SlideInfo("Slide 1", "Text") };
        _wordParser.Setup(w => w.ExtractSlides(It.IsAny<byte[]>())).Returns(slides);
        _qc.Setup(q => q.RunAsync(
                It.IsAny<byte[]>(), It.IsAny<IReadOnlyList<SlideInfo>>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new QualityCheckResult(1, "Slide 1", 0.95, []) });

        var ctrl = BuildController(new AppOptions { EnableQualityCheck = true });
        var pptxBytes = CreateMinimalPptxBytes();

        var result = await ctrl.QualityCheck(
            CreateFormFile("s.docx", ""),
            CreateFormFile("p.pptx", "", pptxBytes)) as OkObjectResult;

        result.Should().NotBeNull();
        _qc.Verify(q => q.RunAsync(
            It.IsAny<byte[]>(), It.IsAny<IReadOnlyList<SlideInfo>>(),
            "en-US-JennyNeural", It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── TranslatorService locale helper ──────────────────────────────────

    [Theory]
    [InlineData("en-US-JennyNeural", "en-US")]
    [InlineData("fr-FR-DeniseNeural", "fr-FR")]
    [InlineData("ja-JP-NanamiNeural", "ja-JP")]
    [InlineData("es-MX-DaliaNeural", "es-MX")]
    public void TranslatorService_LocaleFromVoice_ParsesCorrectly(string voice, string expectedLocale)
    {
        var locale = PptxNarrator.Api.Services.TranslatorService.LocaleFromVoice(voice);
        locale.Should().Be(expectedLocale);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static IFormFile CreateFormFile(string filename, string content, byte[]? bytes = null)
    {
        var data = bytes ?? Encoding.UTF8.GetBytes(content);
        var ms = new MemoryStream(data);
        return new FormFile(ms, 0, data.Length, "file", filename);
    }

    private static IFormFile CreateFormFileWithSlides(int slideCount)
    {
        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true))
        {
            for (int i = 1; i <= slideCount; i++)
            {
                var entry = zip.CreateEntry($"ppt/slides/slide{i}.xml");
                using var sw = new StreamWriter(entry.Open());
                sw.Write($"<slide{i}/>");
            }
        }
        var bytes = ms.ToArray();
        return CreateFormFile("pptx.pptx", "", bytes);
    }

    private static byte[] CreateMinimalPptxBytes(int slideCount = 1)
    {
        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true))
        {
            for (int i = 1; i <= slideCount; i++)
            {
                var entry = zip.CreateEntry($"ppt/slides/slide{i}.xml");
                using var sw = new StreamWriter(entry.Open());
                sw.Write("<slide/>");
            }
        }
        return ms.ToArray();
    }
}
