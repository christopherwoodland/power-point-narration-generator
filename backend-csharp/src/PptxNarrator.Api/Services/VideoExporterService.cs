using System.Runtime.InteropServices;

namespace PptxNarrator.Api.Services;

/// <summary>
/// Converts a narrated PPTX to MP4 using FFmpeg.
/// Slide rendering: PowerPoint COM via PowerShell (Windows) or LibreOffice headless (cross-platform).
/// Requires ffmpeg on PATH.
/// </summary>
public sealed class VideoExporterService : IVideoExporterService
{
    private const string AudioRelType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/audio";
    private const string RelNs =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    private const double FallbackDurationS = 3.0;
    private const int SlideWidthPx = 1920;
    private const int SlideHeightPx = 1080;

    private readonly ILogger<VideoExporterService> _log;

    public VideoExporterService(ILogger<VideoExporterService> log)
    {
        _log = log;
    }

    public async IAsyncEnumerable<ProgressEvent> ExportVideoAsync(
        byte[] pptxBytes,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), $"pptx_video_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            // ── 1. Save PPTX to disk ────────────────────────────────────────
            var pptxPath = Path.Combine(tmpDir, "input.pptx");
            await File.WriteAllBytesAsync(pptxPath, pptxBytes, ct);

            // ── 2. Count slides and extract audio ──────────────────────────
            var audioPerSlide = ExtractAudioPerSlide(pptxBytes, tmpDir);
            int slideCount = CountSlides(pptxBytes);
            if (slideCount == 0)
            {
                yield return new ProgressEvent("error", Message: "No slides found in PPTX.");
                yield break;
            }

            // ── 3. Export slides as PNGs ───────────────────────────────────
            yield return new ProgressEvent("progress", Message: "Rendering slides to images…");
            List<string>? pngPaths = null;
            string? pngError = null;
            try
            {
                pngPaths = await ExportSlidesToPngAsync(pptxPath, tmpDir, slideCount, ct);
            }
            catch (Exception ex)
            {
                pngError = $"Slide rendering failed: {ex.Message}";
            }
            if (pngError is not null)
            {
                yield return new ProgressEvent("error", Message: pngError);
                yield break;
            }

            // ── 4. Build per-slide MP4 clips ────────────────────────────────
            var clipPaths = new List<string>();
            for (int i = 0; i < slideCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                int slideNum = i + 1;
                yield return new ProgressEvent("progress",
                    Slide: slideNum, Total: slideCount,
                    Message: $"Encoding slide {slideNum} of {slideCount}…");

                var pngPath = i < pngPaths!.Count ? pngPaths[i] : null;
                if (pngPath is null || !File.Exists(pngPath))
                {
                    _log.LogWarning("PNG for slide {N} not found, skipping", slideNum);
                    continue;
                }

                audioPerSlide.TryGetValue(slideNum, out var audioPath);
                var clipPath = Path.Combine(tmpDir, $"clip_{slideNum:D4}.mp4");
                await BuildClipAsync(pngPath, audioPath, clipPath, ct);
                clipPaths.Add(clipPath);
            }

            if (clipPaths.Count == 0)
            {
                yield return new ProgressEvent("error", Message: "No clips were generated.");
                yield break;
            }

            // ── 5. Concatenate clips ────────────────────────────────────────
            yield return new ProgressEvent("progress", Message: "Combining clips into final video…");
            var concatList = Path.Combine(tmpDir, "concat.txt");
            await File.WriteAllLinesAsync(concatList,
                clipPaths.Select(p => $"file '{p.Replace("'", "'\\''")}'"
            ), ct);

            var outputMp4 = Path.Combine(tmpDir, "output.mp4");
            await RunFfmpegAsync(
                $"-y -f concat -safe 0 -i \"{concatList}\" -c copy \"{outputMp4}\"",
                ct);

            // ── 6. Return as base64 ─────────────────────────────────────────
            var mp4Bytes = await File.ReadAllBytesAsync(outputMp4, ct);
            _log.LogInformation("[VideoExport] Done — {Bytes:N0} bytes", mp4Bytes.Length);
            yield return new ProgressEvent("done",
                Mp4: Convert.ToBase64String(mp4Bytes));
        }
        finally
        {
            try { Directory.Delete(tmpDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    // ── Slide count ──────────────────────────────────────────────────────

    private static int CountSlides(byte[] pptxBytes)
    {
        using var zip = new ZipArchive(new MemoryStream(pptxBytes), ZipArchiveMode.Read);
        int n = 1;
        while (zip.GetEntry($"ppt/slides/slide{n}.xml") is not null) n++;
        return n - 1;
    }

    // ── Audio extraction ─────────────────────────────────────────────────

    private static Dictionary<int, string> ExtractAudioPerSlide(byte[] pptxBytes, string tmpDir)
    {
        var result = new Dictionary<int, string>();
        var relNs = XNamespace.Get(RelNs);

        using var zip = new ZipArchive(new MemoryStream(pptxBytes), ZipArchiveMode.Read);

        for (int i = 1; ; i++)
        {
            if (zip.GetEntry($"ppt/slides/slide{i}.xml") is null) break;

            var relsEntry = zip.GetEntry($"ppt/slides/_rels/slide{i}.xml.rels");
            if (relsEntry is null) continue;

            using var relsStream = relsEntry.Open();
            var doc = XDocument.Load(relsStream);

            var audioRel = doc.Root?.Elements(relNs + "Relationship")
                .FirstOrDefault(r => r.Attribute("Type")?.Value == AudioRelType);

            if (audioRel is null) continue;

            var target = audioRel.Attribute("Target")?.Value ?? "";
            var mediaPath = ResolveRelTarget(target);
            var mediaEntry = zip.GetEntry(mediaPath);
            if (mediaEntry is null) continue;

            var outPath = Path.Combine(tmpDir, $"audio_slide{i}.mp3");
            using var ms = new MemoryStream();
            using (var s = mediaEntry.Open()) s.CopyTo(ms);
            File.WriteAllBytes(outPath, ms.ToArray());
            result[i] = outPath;
        }
        return result;
    }

    private static string ResolveRelTarget(string target)
    {
        if (target.StartsWith('/')) return target.TrimStart('/');
        var parts = $"ppt/slides/{target}".Replace('\\', '/').Split('/');
        var resolved = new List<string>();
        foreach (var p in parts)
        {
            if (p == "..") { if (resolved.Count > 0) resolved.RemoveAt(resolved.Count - 1); }
            else if (p is not ("" or ".")) resolved.Add(p);
        }
        return string.Join('/', resolved);
    }

    // ── PNG export ───────────────────────────────────────────────────────

    private async Task<List<string>> ExportSlidesToPngAsync(
        string pptxPath, string outDir, int slideCount, CancellationToken ct)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try { return await ExportViaPowerPointComAsync(pptxPath, outDir, slideCount, ct); }
            catch (Exception ex)
            {
                _log.LogWarning("PowerPoint COM export failed ({Msg}), falling back to LibreOffice", ex.Message);
            }
        }
        return await ExportViaLibreOfficeAsync(pptxPath, outDir, ct);
    }

    private static async Task<List<string>> ExportViaPowerPointComAsync(
        string pptxPath, string outDir, int slideCount, CancellationToken ct)
    {
        // Run a PowerShell one-liner that uses PowerPoint COM to export PNGs
        var pngDir = Path.Combine(outDir, "pngs");
        Directory.CreateDirectory(pngDir);
        var pptxAbs = Path.GetFullPath(pptxPath).Replace("'", "''");
        var pngDirAbs = Path.GetFullPath(pngDir).Replace("'", "''");

        var script = $@"
$ppt = New-Object -ComObject PowerPoint.Application
$ppt.Visible = [Microsoft.Office.Core.MsoTriState]::msoFalse
$prs = $ppt.Presentations.Open('{pptxAbs}', $true, $false, $false)
for ($i=1; $i -le $prs.Slides.Count; $i++) {{
    $prs.Slides($i).Export('{pngDirAbs}\slide{{}}{0:D4}.png' -f $i, 'PNG', {SlideWidthPx}, {SlideHeightPx})
}}
$prs.Close()
$ppt.Quit()
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($ppt) | Out-Null
";
        await RunProcessAsync("powershell", $"-NoProfile -Command \"{script.Replace("\"", "\\\"")}\"", ct);

        return Enumerable.Range(1, slideCount)
            .Select(i => Path.Combine(pngDir, $"slide{i:D4}.png"))
            .Where(File.Exists)
            .ToList();
    }

    private static async Task<List<string>> ExportViaLibreOfficeAsync(
        string pptxPath, string outDir, CancellationToken ct)
    {
        // Step 1: PPTX → PDF
        var loProfile = Path.Combine(outDir, "lo_profile");
        Directory.CreateDirectory(loProfile);
        var loProfileUrl = $"file://{loProfile.Replace('\\', '/')}";

        await RunProcessAsync("libreoffice",
            $"--headless \"-env:UserInstallation={loProfileUrl}\" --convert-to pdf --outdir \"{outDir}\" \"{pptxPath}\"",
            ct, envOverrides: new() { ["HOME"] = outDir });

        var pdfPath = Path.ChangeExtension(Path.Combine(outDir, Path.GetFileName(pptxPath)), ".pdf");
        if (!File.Exists(pdfPath))
            throw new FileNotFoundException("LibreOffice did not produce a PDF.", pdfPath);

        // Step 2: PDF → PNGs via pdftoppm
        var pngPrefix = Path.Combine(outDir, "slide");
        await RunProcessAsync("pdftoppm",
            $"-png -rx 192 -ry 192 \"{pdfPath}\" \"{pngPrefix}\"", ct);

        return Directory.GetFiles(outDir, "slide-*.png")
            .OrderBy(f => f)
            .ToList();
    }

    // ── FFmpeg clip builder ──────────────────────────────────────────────

    private static async Task BuildClipAsync(
        string pngPath, string? audioPath, string outPath, CancellationToken ct)
    {
        if (audioPath is not null && File.Exists(audioPath))
        {
            // Loop the PNG for the exact duration of the audio, then add 200ms silence pad at start
            await RunFfmpegAsync(
                $"-y -loop 1 -i \"{pngPath}\" -i \"{audioPath}\" " +
                $"-filter_complex \"[1:a]adelay=200|200[a]\" -map 0:v -map \"[a]\" " +
                $"-c:v libx264 -preset ultrafast -tune stillimage -crf 18 " +
                $"-c:a aac -b:a 192k -shortest -pix_fmt yuv420p \"{outPath}\"",
                ct);
        }
        else
        {
            // No audio — fixed 3-second still
            await RunFfmpegAsync(
                $"-y -loop 1 -i \"{pngPath}\" -t {FallbackDurationS:F1} " +
                $"-c:v libx264 -preset ultrafast -tune stillimage -crf 18 " +
                $"-an -pix_fmt yuv420p \"{outPath}\"",
                ct);
        }
    }

    // ── Process helpers ───────────────────────────────────────────────────

    private static async Task RunFfmpegAsync(string args, CancellationToken ct) =>
        await RunProcessAsync("ffmpeg", args, ct);

    private static async Task RunProcessAsync(
        string exe, string args, CancellationToken ct,
        Dictionary<string, string>? envOverrides = null)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(exe, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (envOverrides is not null)
            foreach (var kv in envOverrides)
                psi.Environment[kv.Key] = kv.Value;

        using var proc = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {exe}");

        var stderr = await proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        if (proc.ExitCode != 0)
            throw new InvalidOperationException(
                $"{exe} exited with code {proc.ExitCode}.\n{stderr.TrimEnd()}");
    }
}
