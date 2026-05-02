namespace PptxNarrator.Api.Configuration;

/// <summary>
/// Strongly-typed configuration bound from environment variables / appsettings.
/// Feature flags (ENABLE_*) can be toggled without rebuilding the image.
/// </summary>
public class AppOptions
{
    // ── Feature flags ───────────────────────────────────────────────────────
    /// <summary>Toggle the Quality Check step (Step 4). Env: ENABLE_QUALITY_CHECK</summary>
    public bool EnableQualityCheck { get; set; } = false;

    /// <summary>Toggle AI presentation generation. Env: ENABLE_AI_MODE</summary>
    public bool EnableAiMode { get; set; } = false;

    /// <summary>Toggle MP4 video export. Env: ENABLE_VIDEO_EXPORT</summary>
    public bool EnableVideoExport { get; set; } = false;

    /// <summary>Toggle MP3 audio export (zip of embedded audio files). Env: ENABLE_MP3_EXPORT</summary>
    public bool EnableMp3Export { get; set; } = true;

    // ── Azure OpenAI ────────────────────────────────────────────────────────
    public string AzureOpenAiEndpoint { get; set; } =
        "https://bhs-development-public-foundry-r.cognitiveservices.azure.com";

    public string AzureOpenAiDeployment { get; set; } = "gpt-5.2";

    /// <summary>Endpoint for image generation. If set, uses the MAI (/mai/v1/) API. Env: AZURE_IMAGE_ENDPOINT</summary>
    public string AzureImageEndpoint { get; set; } = "";
    public string AzureImageDeployment { get; set; } = "MAI-Image-2e";
    public string ChatApiVersion { get; set; } = "2025-01-01-preview";
    public string ImageApiVersion { get; set; } = "2024-02-01";

    // ── Azure Speech ────────────────────────────────────────────────────────
    public string AzureSpeechResourceName { get; set; } = "bhs-development-public-foundry-r";
    public string AzureSpeechRegion { get; set; } = "eastus2";

    /// <summary>
    /// "standard" = regional Speech Service (default).
    /// "mai" = Azure AI Foundry MAI-Voice-1 endpoint.
    /// Env: AZURE_TTS_MODE
    /// </summary>
    public string AzureTtsMode { get; set; } = "standard";

    /// <summary>Base URL of the Foundry resource for MAI Voice. Env: AZURE_VOICE_ENDPOINT</summary>
    public string AzureVoiceEndpoint { get; set; } = "";

    /// <summary>
    /// Max number of concurrent slide TTS operations.
    /// Keep this bounded to avoid throttling. Env: AZURE_TTS_MAX_PARALLELISM
    /// </summary>
    public int TtsMaxParallelism { get; set; } = 4;

    // ── Azure Document Intelligence ─────────────────────────────────────────
    public string AzureDocIntelEndpoint { get; set; } =
        "https://bhs-development-public-foundry-r.cognitiveservices.azure.com/";

    // ── UI ──────────────────────────────────────────────────────────────────
    /// <summary>When true, the "Use slide text as narration" toggle defaults to on. Env: DEFAULT_SINGLE_PPTX_MODE</summary>
    public bool DefaultSinglePptxMode { get; set; } = false;
    public string AppBannerMessage { get; set; } = "";
    public string UploadFilesMessage { get; set; } = "Provide a narration script and (optionally) a PowerPoint to narrate.";

    // ── UI Branding Storage ──────────────────────────────────────────────────
    /// <summary>
    /// Azure Storage account name for Blob-backed UI branding.
    /// When set, branding is read/written via Entra RBAC (no shared keys).
    /// Leave blank for local dev — falls back to file on disk.
    /// Env: AZURE_BRANDING_STORAGE_ACCOUNT
    /// </summary>
    public string BrandingStorageAccountName { get; set; } = "";

    /// <summary>Blob container name for UI branding JSON. Env: AZURE_BRANDING_CONTAINER</summary>
    public string BrandingStorageContainer { get; set; } = "branding-data";

    // ── Application Insights ─────────────────────────────────────────────────
    /// <summary>Optional. Enables Application Insights telemetry. Env: APPLICATIONINSIGHTS_CONNECTION_STRING</summary>
    public string? ApplicationInsightsConnectionString { get; set; }
}
