namespace PptxNarrator.Api.Configuration;

/// <summary>
/// Strongly-typed configuration bound from environment variables / appsettings.
/// Feature flags (ENABLE_*) can be toggled without rebuilding the image.
/// </summary>
public class AppOptions
{
    // ── Feature flags ───────────────────────────────────────────────────────
    /// <summary>Toggle the Quality Check step (Step 4). Env: ENABLE_QUALITY_CHECK</summary>
    public bool EnableQualityCheck { get; set; } = true;

    /// <summary>Toggle AI presentation generation. Env: ENABLE_AI_MODE</summary>
    public bool EnableAiMode { get; set; } = true;

    /// <summary>Toggle MP4 video export. Env: ENABLE_VIDEO_EXPORT</summary>
    public bool EnableVideoExport { get; set; } = true;

    // ── Azure OpenAI ────────────────────────────────────────────────────────
    public string AzureOpenAiEndpoint { get; set; } =
        "https://bhs-development-public-foundry-r.cognitiveservices.azure.com";

    public string AzureOpenAiDeployment { get; set; } = "gpt-5.2";
    public string AzureImageDeployment { get; set; } = "gpt-image-1.5";
    public string ChatApiVersion { get; set; } = "2025-01-01-preview";
    public string ImageApiVersion { get; set; } = "2024-02-01";

    // ── Azure Speech ────────────────────────────────────────────────────────
    public string AzureSpeechResourceName { get; set; } = "bhs-development-public-foundry-r";
    public string AzureSpeechRegion { get; set; } = "eastus2";

    // ── Azure Document Intelligence ─────────────────────────────────────────
    public string AzureDocIntelEndpoint { get; set; } =
        "https://bhs-development-public-foundry-r.cognitiveservices.azure.com/";

    // ── UI ──────────────────────────────────────────────────────────────────
    public string AppBannerMessage { get; set; } = "";

    // ── Application Insights ─────────────────────────────────────────────────
    /// <summary>Optional. Enables Application Insights telemetry. Env: APPLICATIONINSIGHTS_CONNECTION_STRING</summary>
    public string? ApplicationInsightsConnectionString { get; set; }
}
