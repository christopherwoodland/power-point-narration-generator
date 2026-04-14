using System.IO.Compression;
using System.Xml.Linq;

namespace PptxNarrator.Tests.Services;

public class PptxBuilderServiceTests
{
    private readonly PptxBuilderService _sut = new(NullLogger<PptxBuilderService>.Instance);

    [Fact]
    public void EmbedAudio_AllNullAudio_ReturnsPptxUnchanged()
    {
        var pptx = BuildMinimalPptx(2);
        var result = _sut.EmbedAudio(pptx, [null, null]);

        result.Should().NotBeNull();
        IsValidZip(result).Should().BeTrue();
    }

    [Fact]
    public void EmbedAudio_WithAudioForSlide1_AddsMediaEntry()
    {
        var pptx = BuildMinimalPptx(2);
        var fakeAudio = Encoding.UTF8.GetBytes("ID3FAKEMP3");

        var result = _sut.EmbedAudio(pptx, [fakeAudio, null]);

        using var zip = new ZipArchive(new MemoryStream(result), ZipArchiveMode.Read);
        zip.GetEntry("ppt/media/audio_slide1.mp3").Should().NotBeNull();
        zip.GetEntry("ppt/media/audio_icon1.png").Should().NotBeNull();
        // Slide 2 should have no audio
        zip.GetEntry("ppt/media/audio_slide2.mp3").Should().BeNull();
    }

    [Fact]
    public void EmbedAudio_WithAudio_InjectsRelationshipsInSlideRels()
    {
        var pptx = BuildMinimalPptx(1);
        var fakeAudio = Encoding.UTF8.GetBytes("ID3FAKEMP3");

        var result = _sut.EmbedAudio(pptx, [fakeAudio]);

        using var zip = new ZipArchive(new MemoryStream(result), ZipArchiveMode.Read);
        var relsEntry = zip.GetEntry("ppt/slides/_rels/slide1.xml.rels");
        relsEntry.Should().NotBeNull();

        using var stream = relsEntry!.Open();
        var relsDoc = XDocument.Load(stream);
        var rels = relsDoc.Descendants()
            .Where(e => e.Name.LocalName == "Relationship")
            .ToList();

        rels.Should().Contain(r =>
            r.Attribute("Type") != null && r.Attribute("Type")!.Value.Contains("audio"));
        rels.Should().Contain(r =>
            r.Attribute("Type") != null && r.Attribute("Type")!.Value.Contains("media"));
        rels.Should().Contain(r =>
            r.Attribute("Type") != null && r.Attribute("Type")!.Value.Contains("image"));
    }

    [Fact]
    public void EmbedAudio_WithAudio_InjectsPickShapeInSlideXml()
    {
        var pptx = BuildMinimalPptx(1);
        var fakeAudio = Encoding.UTF8.GetBytes("ID3FAKEMP3");

        var result = _sut.EmbedAudio(pptx, [fakeAudio]);

        using var zip = new ZipArchive(new MemoryStream(result), ZipArchiveMode.Read);
        var slideEntry = zip.GetEntry("ppt/slides/slide1.xml")!;
        using var stream = slideEntry.Open();
        var slideDoc = XDocument.Load(stream);

        var pics = slideDoc.Descendants()
            .Where(e => e.Name.LocalName == "pic")
            .ToList();
        pics.Should().NotBeEmpty();

        var audioFile = slideDoc.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "audioFile");
        audioFile.Should().NotBeNull();
    }

    [Fact]
    public void EmbedAudio_WithAudio_InjectsTimingElement()
    {
        var pptx = BuildMinimalPptx(1);
        var fakeAudio = Encoding.UTF8.GetBytes("ID3FAKEMP3");

        var result = _sut.EmbedAudio(pptx, [fakeAudio]);

        using var zip = new ZipArchive(new MemoryStream(result), ZipArchiveMode.Read);
        var slideEntry = zip.GetEntry("ppt/slides/slide1.xml")!;
        using var stream = slideEntry.Open();
        var slideDoc = XDocument.Load(stream);

        var timing = slideDoc.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "timing");
        timing.Should().NotBeNull();
    }

    [Fact]
    public void EmbedAudio_MultipleSlides_OnlyInjectsForNonNullSlots()
    {
        var pptx = BuildMinimalPptx(3);
        var audio = Encoding.UTF8.GetBytes("ID3FAKEMP3");

        var result = _sut.EmbedAudio(pptx, [audio, null, audio]);

        using var zip = new ZipArchive(new MemoryStream(result), ZipArchiveMode.Read);
        zip.GetEntry("ppt/media/audio_slide1.mp3").Should().NotBeNull();
        zip.GetEntry("ppt/media/audio_slide2.mp3").Should().BeNull();
        zip.GetEntry("ppt/media/audio_slide3.mp3").Should().NotBeNull();
    }

    [Fact]
    public void MakeTinyPng_ReturnValidPngSignature()
    {
        var png = PptxBuilderService.MakeTinyPng();

        png.Should().NotBeEmpty();
        // PNG magic bytes: 0x89 PNG 0x0D 0x0A 0x1A 0x0A
        png[0].Should().Be(0x89);
        png[1].Should().Be(0x50); // 'P'
        png[2].Should().Be(0x4E); // 'N'
        png[3].Should().Be(0x47); // 'G'
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static readonly XNamespace PNs =
        "http://schemas.openxmlformats.org/presentationml/2006/main";
    private static readonly XNamespace RelNs =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>Build a minimal in-memory PPTX with N blank slides.</summary>
    private static byte[] BuildMinimalPptx(int slideCount)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, "[Content_Types].xml", BuildContentTypes(slideCount));
            WriteEntry(zip, "_rels/.rels", BuildRootRels());
            WriteEntry(zip, "ppt/presentation.xml", BuildPresentation(slideCount));
            WriteEntry(zip, "ppt/_rels/presentation.xml.rels", BuildPresRels(slideCount));

            for (int i = 1; i <= slideCount; i++)
            {
                WriteEntry(zip, $"ppt/slides/slide{i}.xml", BuildSlide(i));
                WriteEntry(zip, $"ppt/slides/_rels/slide{i}.xml.rels", BuildSlideRels());
            }
        }
        return ms.ToArray();
    }

    private static string BuildContentTypes(int n)
    {
        var slides = string.Join("", Enumerable.Range(1, n).Select(i =>
            $"""<Override PartName="/ppt/slides/slide{i}.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.slide+xml"/>"""));
        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Default Extension="mp3" ContentType="audio/mpeg"/>
              <Default Extension="png" ContentType="image/png"/>
              <Override PartName="/ppt/presentation.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml"/>
              {slides}
            </Types>
            """;
    }

    private static string BuildRootRels() => """
        <?xml version="1.0" encoding="utf-8"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="ppt/presentation.xml"/>
        </Relationships>
        """;

    private static string BuildPresentation(int n) => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <p:presentation xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main">
          <p:sldIdLst>
            {string.Join("", Enumerable.Range(1, n).Select(i => $"""<p:sldId id="{255 + i}" r:id="rId{i}" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"/>"""))}
          </p:sldIdLst>
        </p:presentation>
        """;

    private static string BuildPresRels(int n) =>
        $"""
        <?xml version="1.0" encoding="utf-8"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          {string.Join("", Enumerable.Range(1, n).Select(i =>
              $"""<Relationship Id="rId{i}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide" Target="slides/slide{i}.xml"/>"""))}
        </Relationships>
        """;

    private static string BuildSlide(int num) => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <p:sld xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
               xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
          <p:cSld>
            <p:spTree>
              <p:nvGrpSpPr>
                <p:cNvPr id="1" name=""/>
                <p:cNvGrpSpPr/>
                <p:nvPr/>
              </p:nvGrpSpPr>
              <p:grpSpPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="0" cy="0"/></a:xfrm></p:grpSpPr>
            </p:spTree>
          </p:cSld>
        </p:sld>
        """;

    private static string BuildSlideRels() => """
        <?xml version="1.0" encoding="utf-8"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
        </Relationships>
        """;

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name);
        using var stream = entry.Open();
        // TrimStart removes the leading newline that C# raw string literals add before the
        // first content line. No-BOM UTF-8 prevents the XML parser from seeing BOMs as content.
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            .GetBytes(content.TrimStart());
        stream.Write(bytes);
    }

    private static bool IsValidZip(byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
            return zip.Entries.Count > 0;
        }
        catch { return false; }
    }
}
