using System.Security;
using Azure.Identity;

namespace PptxNarrator.Api.Services;

/// <summary>
/// Azure Neural TTS client.
/// Auth flow: DefaultAzureCredential → AAD token → STS token exchange → regional TTS endpoint.
/// No API keys; all auth via managed identity / Azure CLI.
/// </summary>
public sealed class TtsService : ITtsService
{
    private const string CogScope = "https://cognitiveservices.azure.com/.default";

    private readonly IHttpClientFactory _http;
    private readonly TokenCredential _credential;
    private readonly AppOptions _opts;
    private readonly ILogger<TtsService> _log;

    private string? _stsToken;
    private DateTimeOffset _stsExpiry = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public TtsService(IHttpClientFactory http, TokenCredential credential,
        IOptions<AppOptions> opts, ILogger<TtsService> log)
    {
        _http = http;
        _credential = credential;
        _opts = opts.Value;
        _log = log;
    }

    public async Task<byte[]> SynthesizeToMp3Async(string text, string voice, CancellationToken ct = default)
    {
        var stsToken = await GetSpeechTokenAsync(ct);
        var ssml = BuildSsml(text, voice);

        var url = $"https://{_opts.AzureSpeechRegion}.tts.speech.microsoft.com/cognitiveservices/v1";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("Authorization", $"Bearer {stsToken}");
        request.Headers.Add("X-Microsoft-OutputFormat", "audio-24khz-48kbitrate-mono-mp3");
        request.Headers.Add("User-Agent", "PptxNarrator/1.0");
        request.Content = new StringContent(ssml, Encoding.UTF8, "application/ssml+xml");

        var client = _http.CreateClient("tts");
        var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private async Task<string> GetSpeechTokenAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            // Return cached token if still fresh (2-min buffer)
            if (_stsToken is not null && DateTimeOffset.UtcNow < _stsExpiry.AddMinutes(-2))
                return _stsToken;

            // Step 1: AAD token via DefaultAzureCredential
            var tokenCtx = new TokenRequestContext(new[] { CogScope });
            var aad = await _credential.GetTokenAsync(tokenCtx, ct);

            // Step 2: Exchange AAD token for Speech STS token
            var stsUrl = $"https://{_opts.AzureSpeechResourceName}.cognitiveservices.azure.com/sts/v1.0/issueToken";

            using var req = new HttpRequestMessage(HttpMethod.Post, stsUrl);
            req.Headers.Add("Authorization", $"Bearer {aad.Token}");
            // Empty body — STS endpoint requires POST but no body content
            req.Content = new ByteArrayContent(Array.Empty<byte>());
            req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-www-form-urlencoded");

            var client = _http.CreateClient("sts");
            var resp = await client.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();

            _stsToken = await resp.Content.ReadAsStringAsync(ct);
            _stsExpiry = DateTimeOffset.UtcNow.AddMinutes(10);
            _log.LogDebug("STS token refreshed, expires ~{Expiry}", _stsExpiry);
            return _stsToken;
        }
        finally
        {
            _lock.Release();
        }
    }

    internal static string BuildSsml(string text, string voice)
    {
        var safeText = SecurityElement.Escape(text) ?? text;
        var parts = voice.Split('-');
        var lang = parts.Length >= 2 ? $"{parts[0]}-{parts[1]}" : "en-US";
        return $"<speak version=\"1.0\" xmlns=\"http://www.w3.org/2001/10/synthesis\" xml:lang=\"{lang}\"><voice name=\"{voice}\">{safeText}</voice></speak>";
    }
}
