namespace PptxNarrator.Api.Services.Interfaces;

public interface IVideoExporterService
{
    /// <summary>
    /// Export a narrated PPTX to an MP4 video.
    /// Yields <see cref="PptxNarrator.Api.Models.ProgressEvent"/> records as NDJSON.
    /// Requires FFmpeg on PATH and either LibreOffice (cross-platform) or
    /// PowerPoint (Windows COM, via PowerShell) to render slides as PNGs.
    /// </summary>
    IAsyncEnumerable<Models.ProgressEvent> ExportVideoAsync(
        byte[] pptxBytes, CancellationToken ct = default);
}
