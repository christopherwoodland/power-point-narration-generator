namespace PptxNarrator.Api.Models;

/// <summary>Progress event streamed as newline-delimited JSON.</summary>
public record ProgressEvent(
    string Type,          // "progress" | "done" | "error"
    int Slide = 0,
    int Total = 0,
    string Phase = "",
    string Message = "",
    string? Pptx = null,  // base64 on type=done
    string? Mp4 = null    // base64 on type=done (video)
);
