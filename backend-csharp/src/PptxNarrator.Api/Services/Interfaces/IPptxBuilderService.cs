namespace PptxNarrator.Api.Services.Interfaces;

public interface IPptxBuilderService
{
    /// <summary>
    /// Embed per-slide MP3 audio into a PPTX file.
    /// <paramref name="slideAudio"/> is indexed 0-based, matching PPTX slide order.
    /// Null entries are skipped (no audio for that slide).
    /// </summary>
    byte[] EmbedAudio(byte[] pptxBytes, IReadOnlyList<byte[]?> slideAudio);
}
