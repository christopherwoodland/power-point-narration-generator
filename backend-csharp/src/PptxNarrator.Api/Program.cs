using Azure.Identity;
using PptxNarrator.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Configuration (env vars override appsettings) ────────────────────────
builder.Services.Configure<AppOptions>(opts =>
{
    opts.EnableQualityCheck = builder.Configuration.GetValue("ENABLE_QUALITY_CHECK", true);
    opts.EnableAiMode = builder.Configuration.GetValue("ENABLE_AI_MODE", true);
    opts.EnableVideoExport = builder.Configuration.GetValue("ENABLE_VIDEO_EXPORT", true);

    opts.AzureOpenAiEndpoint = builder.Configuration["AZURE_OPENAI_ENDPOINT"]
        ?? opts.AzureOpenAiEndpoint;
    opts.AzureOpenAiDeployment = builder.Configuration["AZURE_OPENAI_DEPLOYMENT"]
        ?? opts.AzureOpenAiDeployment;
    opts.AzureImageDeployment = builder.Configuration["AZURE_IMAGE_DEPLOYMENT"]
        ?? opts.AzureImageDeployment;
    opts.AzureSpeechResourceName = builder.Configuration["AZURE_SPEECH_RESOURCE_NAME"]
        ?? opts.AzureSpeechResourceName;
    opts.AzureSpeechRegion = builder.Configuration["AZURE_SPEECH_REGION"]
        ?? opts.AzureSpeechRegion;
    opts.AzureDocIntelEndpoint = builder.Configuration["AZURE_DOC_INTEL_ENDPOINT"]
        ?? opts.AzureDocIntelEndpoint;
    opts.AppBannerMessage = builder.Configuration["APP_BANNER_MESSAGE"] ?? "";
    opts.ApplicationInsightsConnectionString =
        builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]
        ?? builder.Configuration["ApplicationInsights:ConnectionString"];
});

// ── Azure Identity (DefaultAzureCredential — no API keys) ─────────────────
var azureTenantId = builder.Configuration["AZURE_TENANT_ID"];
builder.Services.AddSingleton<Azure.Core.TokenCredential>(_ =>
    new DefaultAzureCredential(new DefaultAzureCredentialOptions
    {
        // Ensure the correct tenant is used for both dev (AzureCLI) and prod (ManagedIdentity)
        TenantId = string.IsNullOrWhiteSpace(azureTenantId) ? null : azureTenantId
    }));

// ── HTTP clients ───────────────────────────────────────────────────────────
builder.Services.AddHttpClient("tts").ConfigurePrimaryHttpMessageHandler(() =>
    new HttpClientHandler { MaxConnectionsPerServer = 10 });
builder.Services.AddHttpClient("sts").ConfigurePrimaryHttpMessageHandler(() =>
    new HttpClientHandler { MaxConnectionsPerServer = 4 });
builder.Services.AddHttpClient("stt").ConfigurePrimaryHttpMessageHandler(() =>
    new HttpClientHandler { MaxConnectionsPerServer = 10 });
builder.Services.AddHttpClient("translator").ConfigurePrimaryHttpMessageHandler(() =>
    new HttpClientHandler { MaxConnectionsPerServer = 4 });
builder.Services.AddHttpClient("openai").ConfigurePrimaryHttpMessageHandler(() =>
    new HttpClientHandler { MaxConnectionsPerServer = 10 });

// ── Application Services ──────────────────────────────────────────────────
builder.Services.AddSingleton<ITtsService, TtsService>();
builder.Services.AddSingleton<ISttService, SttService>();
builder.Services.AddSingleton<ITranslatorService, TranslatorService>();
builder.Services.AddSingleton<IWordParserService, WordParserService>();
builder.Services.AddSingleton<IPptxScriptParserService, PptxScriptParserService>();
builder.Services.AddSingleton<IPptxBuilderService, PptxBuilderService>();
builder.Services.AddSingleton<IAiPptxGeneratorService, AiPptxGeneratorService>();
builder.Services.AddSingleton<IQualityCheckerService, QualityCheckerService>();
builder.Services.AddSingleton<IVideoExporterService, VideoExporterService>();

// ── Application Insights (optional) ─────────────────────────────────────────
var aiConnStr = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]
    ?? builder.Configuration["ApplicationInsights:ConnectionString"];
if (!string.IsNullOrWhiteSpace(aiConnStr))
    builder.Services.AddApplicationInsightsTelemetry(opts => opts.ConnectionString = aiConnStr);

// ── Health checks ────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks();

// ── ASP.NET Core pipeline ──────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "PptxNarrator API", Version = "v1" });
});
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();

app.UseCors();

app.MapHealthChecks("/healthz");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseStaticFiles(); // Serve React build from wwwroot if present

app.MapControllers();

// SPA fallback — serve index.html for all non-API routes
app.MapFallbackToFile("index.html");

app.Run();

// Make Program accessible for integration tests
public partial class Program { }
