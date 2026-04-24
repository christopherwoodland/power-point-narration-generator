namespace PptxNarrator.Api.Services;

/// <summary>
/// Azure Translator client using the Cognitive Services resource-specific endpoint.
/// English locales (en-*) are passed through unchanged.
/// </summary>
public sealed class TranslatorService : ITranslatorService
{
    private const string CogScope = "https://cognitiveservices.azure.com/.default";

    /// Maps TTS locale → Azure Translator language code
    private static readonly Dictionary<string, string> LocaleMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["fr-FR"] = "fr", ["es-ES"] = "es", ["es-MX"] = "es",
        ["de-DE"] = "de", ["it-IT"] = "it", ["pt-BR"] = "pt-br",
        ["pt-PT"] = "pt-pt", ["ja-JP"] = "ja", ["zh-CN"] = "zh-Hans",
        ["zh-TW"] = "zh-Hant", ["ko-KR"] = "ko", ["nl-NL"] = "nl",
        ["pl-PL"] = "pl", ["ru-RU"] = "ru", ["ar-SA"] = "ar",
        ["hi-IN"] = "hi", ["sv-SE"] = "sv", ["nb-NO"] = "nb",
        ["da-DK"] = "da", ["fi-FI"] = "fi", ["tr-TR"] = "tr",
        ["cs-CZ"] = "cs", ["hu-HU"] = "hu", ["el-GR"] = "el",
        ["he-IL"] = "he", ["th-TH"] = "th", ["vi-VN"] = "vi",
        ["id-ID"] = "id", ["uk-UA"] = "uk", ["ro-RO"] = "ro",
    };

    private readonly IHttpClientFactory _http;
    private readonly TokenCredential _credential;
    private readonly AppOptions _opts;
    private readonly ILogger<TranslatorService> _log;

    public TranslatorService(IHttpClientFactory http, TokenCredential credential,
        IOptions<AppOptions> opts, ILogger<TranslatorService> log)
    {
        _http = http;
        _credential = credential;
        _opts = opts.Value;
        _log = log;
    }

    public async Task<string> TranslateForVoiceAsync(string text, string voice, CancellationToken ct = default)
    {
        var locale = LocaleFromVoice(voice);
        if (locale.StartsWith("en-", StringComparison.OrdinalIgnoreCase))
            return text;

        if (!LocaleMap.TryGetValue(locale, out var targetLang))
            return text; // Unknown locale — pass through

        _log.LogInformation("Translating text to {TargetLang} for voice {Voice}", targetLang, voice);

        var tokenCtx = new TokenRequestContext(new[] { CogScope });
        var aad = await _credential.GetTokenAsync(tokenCtx, ct);

        var url = $"https://{_opts.AzureSpeechResourceName}.cognitiveservices.azure.com" +
                  $"/translator/text/v3.0/translate?api-version=3.0&to={targetLang}";

        var body = JsonSerializer.Serialize(new[] { new { Text = text } });

        var client = _http.CreateClient("translator");
        var resp = await HttpRetryHelper.SendWithRetryAsync(
            client,
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.Add("Authorization", $"Bearer {aad.Token}");
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                return request;
            },
            _log,
            "Translation",
            ct);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        return doc.RootElement[0]
                  .GetProperty("translations")[0]
                  .GetProperty("text")
                  .GetString() ?? text;
    }

    internal static string LocaleFromVoice(string voice)
    {
        var parts = voice.Split('-');
        return parts.Length >= 2 ? $"{parts[0]}-{parts[1]}" : "en-US";
    }
}
