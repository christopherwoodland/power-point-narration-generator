using System.Runtime.InteropServices;
using System.Xml;

namespace PptxNarrator.Api.Services;

/// <summary>
/// Embeds per-slide MP3 audio into a PowerPoint (.pptx) file using direct ZIP/OPC manipulation.
/// Implementation steps:
///   1. Adds MP3 + icon PNG as OPC parts.
///   2. Inserts relationships in each slide's .rels file.
///   3. Inserts &lt;p:pic&gt; audio shape into the slide XML.
///   4. Inserts &lt;p:timing&gt; for auto-play on slide entry.
/// </summary>
public sealed class PptxBuilderService : IPptxBuilderService
{
    private readonly ILogger<PptxBuilderService> _log;

    public PptxBuilderService(ILogger<PptxBuilderService> log) => _log = log;

    // OPC relationship type URIs
    private const string AudioRelType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/audio";
    private const string MediaRelType =
        "http://schemas.microsoft.com/office/2007/relationships/media";
    private const string ImageRelType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image";

    // Relationship XML namespace
    private const string RelNs =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    public byte[] EmbedAudio(byte[] pptxBytes, IReadOnlyList<byte[]?> slideAudio)
    {
        _log.LogInformation("Embedding audio into {SlideCount} slides", slideAudio.Count);
        using var msIn = new MemoryStream(pptxBytes);
        using var msOut = new MemoryStream();

        using (var zipIn = new ZipArchive(msIn, ZipArchiveMode.Read, leaveOpen: true))
        using (var zipOut = new ZipArchive(msOut, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Pre-compute what we will inject per slide
            var audioSlots = BuildAudioSlots(slideAudio);

            // Copy / transform all existing entries
            foreach (var entry in zipIn.Entries)
            {
                using var srcStream = entry.Open();
                var data = ReadAll(srcStream);

                if (entry.FullName == "[Content_Types].xml")
                {
                    data = EnsureMediaContentTypes(data);
                }
                else if (TryGetSlideNum(entry.FullName, "ppt/slides/slide", ".xml", out int sNum) &&
                    audioSlots.TryGetValue(sNum, out var slot))
                {
                    data = InjectAudioShapeIntoSlide(data, slot);
                }
                else if (TryGetSlideNum(entry.FullName, "ppt/slides/_rels/slide", ".xml.rels", out int rNum) &&
                         audioSlots.TryGetValue(rNum, out var rSlot))
                {
                    data = InjectRelationships(data, rSlot);
                }

                WriteEntry(zipOut, entry.FullName, data);
            }

            // Add new media files (MP3 + PNG icon per slide)
            foreach (var (slideNum, slot) in audioSlots)
            {
                WriteEntry(zipOut, $"ppt/media/audio_slide{slideNum}.mp3", slot.Mp3Bytes);
                WriteEntry(zipOut, $"ppt/media/audio_icon{slideNum}.png", MakeTinyPng());
            }
        }

        return msOut.ToArray();
    }

    // ── Slot building ─────────────────────────────────────────────────────

    private sealed record AudioSlot(
        int SlideNum, byte[] Mp3Bytes,
        string AudioRid, string MediaRid, string ImgRid,
        int ShapeId);

    private static Dictionary<int, AudioSlot> BuildAudioSlots(IReadOnlyList<byte[]?> slideAudio)
    {
        var slots = new Dictionary<int, AudioSlot>();
        int shapeIdBase = 1000;

        for (int i = 0; i < slideAudio.Count; i++)
        {
            if (slideAudio[i] is not { } mp3) continue;
            int slideNum = i + 1;

            slots[slideNum] = new AudioSlot(
                SlideNum: slideNum,
                Mp3Bytes: mp3,
                AudioRid: $"rIdAudio{slideNum}",
                MediaRid: $"rIdMedia{slideNum}",
                ImgRid: $"rIdImg{slideNum}",
                ShapeId: shapeIdBase + slideNum);
        }
        return slots;
    }

    // ── XML injection ─────────────────────────────────────────────────────

    private static byte[] InjectRelationships(byte[] relsXml, AudioSlot slot)
    {
        var doc = XDocument.Load(new MemoryStream(relsXml));
        var ns = XNamespace.Get(RelNs);
        var root = doc.Root!;

        // Remove any existing audio rels for this slide to avoid duplicates
        root.Elements(ns + "Relationship")
            .Where(r => r.Attribute("Id")?.Value?.StartsWith($"rIdAudio{slot.SlideNum}") == true ||
                        r.Attribute("Id")?.Value?.StartsWith($"rIdMedia{slot.SlideNum}") == true ||
                        r.Attribute("Id")?.Value?.StartsWith($"rIdImg{slot.SlideNum}") == true)
            .Remove();

        root.Add(
            new XElement(ns + "Relationship",
                new XAttribute("Id", slot.AudioRid),
                new XAttribute("Type", AudioRelType),
                new XAttribute("Target", $"../media/audio_slide{slot.SlideNum}.mp3")),
            new XElement(ns + "Relationship",
                new XAttribute("Id", slot.MediaRid),
                new XAttribute("Type", MediaRelType),
                new XAttribute("Target", $"../media/audio_slide{slot.SlideNum}.mp3")),
            new XElement(ns + "Relationship",
                new XAttribute("Id", slot.ImgRid),
                new XAttribute("Type", ImageRelType),
                new XAttribute("Target", $"../media/audio_icon{slot.SlideNum}.png"))
        );

        return SerializeXml(doc);
    }

    private static byte[] InjectAudioShapeIntoSlide(byte[] slideXml, AudioSlot slot)
    {
        var doc = XDocument.Load(new MemoryStream(slideXml));

        var pNs = XNamespace.Get("http://schemas.openxmlformats.org/presentationml/2006/main");
        var aNs = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");
        var rNs = XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/relationships");
        var p14Ns = XNamespace.Get("http://schemas.microsoft.com/office/powerpoint/2010/main");

        var root = doc.Root!;
        var spTree = root.Descendants(pNs + "spTree").FirstOrDefault();
        if (spTree is null) return slideXml;

        // Remove existing audio shape for this slide if re-processing
        spTree.Elements(pNs + "pic")
            .Where(p => p.Descendants(pNs + "cNvPr")
                          .Any(n => n.Attribute("name")?.Value == $"Audio {slot.SlideNum}"))
            .Remove();

        // Build <p:pic> audio shape
        var pic = new XElement(pNs + "pic",
            new XElement(pNs + "nvPicPr",
                new XElement(pNs + "cNvPr",
                    new XAttribute("id", slot.ShapeId),
                    new XAttribute("name", $"Audio {slot.SlideNum}"),
                    new XElement(aNs + "hlinkClick",
                        new XAttribute(rNs + "id", ""),
                        new XAttribute("action", "ppaction://media"))),
                new XElement(pNs + "cNvPicPr",
                    new XElement(aNs + "picLocks", new XAttribute("noChangeAspect", "1"))),
                new XElement(pNs + "nvPr",
                    new XElement(aNs + "audioFile",
                        new XAttribute(rNs + "link", slot.AudioRid)),
                    new XElement(pNs + "extLst",
                        new XElement(pNs + "ext",
                            new XAttribute("uri", "{DAA4B4D4-6D71-4841-9C94-3DE7FCFB9230}"),
                            new XElement(p14Ns + "media",
                                new XAttribute(rNs + "embed", slot.MediaRid)))))),
            new XElement(pNs + "blipFill",
                new XElement(aNs + "blip", new XAttribute(rNs + "embed", slot.ImgRid)),
                new XElement(aNs + "stretch", new XElement(aNs + "fillRect"))),
            new XElement(pNs + "spPr",
                new XElement(aNs + "xfrm",
                    new XElement(aNs + "off", new XAttribute("x", "457200"), new XAttribute("y", "5943600")),
                    new XElement(aNs + "ext", new XAttribute("cx", "457200"), new XAttribute("cy", "457200"))),
                new XElement(aNs + "prstGeom",
                    new XAttribute("prst", "rect"),
                    new XElement(aNs + "avLst")))
        );
        spTree.Add(pic);

        // Build <p:timing> for auto-play on slide entry
        // Remove existing timing element
        root.Elements(pNs + "timing").Remove();

        var sid = slot.ShapeId.ToString();
        var timing = BuildTimingXml(pNs, sid);
        root.Add(timing);

        return SerializeXml(doc);
    }

    // Build the <p:timing> element that auto-plays the audio shape on slide entry.
    // Includes prevCondLst, nextCondLst, and p:audio nodes required for autoplay behavior.
    private static XElement BuildTimingXml(XNamespace pNs, string sid)
    {
        var xml = $@"<p:timing xmlns:p=""http://schemas.openxmlformats.org/presentationml/2006/main""><p:tnLst><p:par><p:cTn id=""1"" dur=""indefinite"" restart=""never"" nodeType=""tmRoot""><p:childTnLst><p:seq concurrent=""1"" nextAc=""seek""><p:cTn id=""2"" dur=""indefinite"" nodeType=""mainSeq""><p:childTnLst><p:par><p:cTn id=""3"" fill=""hold""><p:stCondLst><p:cond delay=""0""/></p:stCondLst><p:childTnLst><p:par><p:cTn id=""4"" fill=""hold""><p:stCondLst><p:cond delay=""0""/></p:stCondLst><p:childTnLst><p:par><p:cTn id=""5"" presetID=""1"" presetClass=""mediacall"" presetSubtype=""0"" fill=""hold"" nodeType=""withEffect""><p:stCondLst><p:cond delay=""0""/></p:stCondLst><p:childTnLst><p:cmd type=""call"" cmd=""playFrom(0.0)""><p:cBhvr><p:cTn id=""6"" dur=""indefinite"" fill=""hold""/><p:tgtEl><p:spTgt spid=""{sid}""/></p:tgtEl></p:cBhvr></p:cmd></p:childTnLst></p:cTn></p:par></p:childTnLst></p:cTn></p:par></p:childTnLst></p:cTn></p:par></p:childTnLst></p:cTn><p:prevCondLst><p:cond evt=""onPrev"" delay=""0""><p:tgtEl><p:sldTgt/></p:tgtEl></p:cond></p:prevCondLst><p:nextCondLst><p:cond evt=""onNext"" delay=""0""><p:tgtEl><p:sldTgt/></p:tgtEl></p:cond></p:nextCondLst></p:seq><p:audio><p:cMediaNode vol=""80000""><p:cTn id=""7"" fill=""hold"" display=""0""><p:stCondLst><p:cond delay=""indefinite""/></p:stCondLst><p:endCondLst><p:cond evt=""onStopAudio"" delay=""0""><p:tgtEl><p:sldTgt/></p:tgtEl></p:cond></p:endCondLst></p:cTn><p:tgtEl><p:spTgt spid=""{sid}""/></p:tgtEl></p:cMediaNode></p:audio></p:childTnLst></p:cTn></p:par></p:tnLst></p:timing>";
        return XElement.Parse(xml);
    }

    // Add <Default> entries for mp3 and png to [Content_Types].xml if missing.
    private static byte[] EnsureMediaContentTypes(byte[] data)
    {
        var doc = XDocument.Load(new MemoryStream(data));
        var ns = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/content-types");
        var root = doc.Root!;

        var existing = root.Elements(ns + "Default")
            .Select(e => e.Attribute("Extension")?.Value?.ToLowerInvariant())
            .ToHashSet();

        if (!existing.Contains("mp3"))
            root.AddFirst(new XElement(ns + "Default",
                new XAttribute("Extension", "mp3"),
                new XAttribute("ContentType", "audio/mpeg")));

        if (!existing.Contains("png"))
            root.AddFirst(new XElement(ns + "Default",
                new XAttribute("Extension", "png"),
                new XAttribute("ContentType", "image/png")));

        return SerializeXml(doc);
    }

    // Serialize XDocument to UTF-8 bytes with XML declaration, no BOM.
    private static byte[] SerializeXml(XDocument doc)
    {
        using var ms = new MemoryStream();
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            OmitXmlDeclaration = false,
            Indent = false,
        };
        using (var writer = XmlWriter.Create(ms, settings))
            doc.Save(writer);
        return ms.ToArray();
    }

    // ── Utilities ─────────────────────────────────────────────────────────

    private static bool TryGetSlideNum(string path, string prefix, string suffix, out int num)
    {
        num = 0;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return false;

        var middle = path[prefix.Length..^suffix.Length];
        return int.TryParse(middle, out num);
    }

    private static void WriteEntry(ZipArchive zip, string name, byte[] data)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(data);
    }

    private static byte[] ReadAll(Stream s)
    {
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>Generates a minimal 1×1 white PNG for the audio icon placeholder.</summary>
    internal static byte[] MakeTinyPng()
    {
        static byte[] Chunk(byte[] tag, byte[] data)
        {
            var len = BitConverter.GetBytes(data.Length);
            if (BitConverter.IsLittleEndian) Array.Reverse(len);
            var crcInput = new byte[tag.Length + data.Length];
            Buffer.BlockCopy(tag, 0, crcInput, 0, tag.Length);
            Buffer.BlockCopy(data, 0, crcInput, tag.Length, data.Length);
            var crc = BitConverter.GetBytes(Crc32(crcInput));
            if (BitConverter.IsLittleEndian) Array.Reverse(crc);
            return [.. len, .. tag, .. data, .. crc];
        }

        static uint Crc32(byte[] buf)
        {
            uint crc = 0xFFFFFFFF;
            foreach (byte b in buf)
            {
                crc ^= b;
                for (int k = 0; k < 8; k++)
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
            return crc ^ 0xFFFFFFFF;
        }

        var ihdr = new byte[] { 0, 0, 0, 1, 0, 0, 0, 1, 8, 2, 0, 0, 0 };
        var idat = Compress(new byte[] { 0, 0xFF, 0xFF, 0xFF });
        var png = new byte[]
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A
        };
        return [.. png,
            .. Chunk("IHDR"u8.ToArray(), ihdr),
            .. Chunk("IDAT"u8.ToArray(), idat),
            .. Chunk("IEND"u8.ToArray(), [])];
    }

    private static byte[] Compress(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var deflate = new System.IO.Compression.DeflateStream(ms, CompressionLevel.Optimal))
            deflate.Write(data);
        return ms.ToArray();
    }
}
