using System.Diagnostics;

namespace PptxNarrator.Api.Services;

/// <summary>
/// Azure Speech-to-Text client.
/// Converts MP3 → 16 kHz PCM WAV (via ffmpeg) → posts to Azure STT REST API.
/// Reuses the STS token from TtsService.
/// </summary>
public sealed class SttService : ISttService
{
    private readonly ITtsService _tts;
    private readonly IHttpClientFactory _http;
    private readonly AppOptions _opts;
    private readonly ILogger<SttService> _log;

    public SttService(ITtsService tts, IHttpClientFactory http,
        IOptions<AppOptions> opts, ILogger<SttService> log)
    {
        _tts = tts;
        _http = http;
        _opts = opts.Value;
        _log = log;
    }

    public async Task<string> TranscribeAsync(byte[] mp3Bytes, string languageLocale = "en-US", CancellationToken ct = default)
    {
        var wavBytes = await ConvertMp3ToWavAsync(mp3Bytes, ct);
        var stsToken = await GetSpeechTokenAsync(ct);

        var url = $"https://{_opts.AzureSpeechRegion}.stt.speech.microsoft.com" +
                  $"/speech/recognition/conversation/cognitiveservices/v1" +
                  $"?language={languageLocale}&format=detailed";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("Authorization", $"Bearer {stsToken}");
        request.Content = new ByteArrayContent(wavBytes);
        request.Content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav")
            {
                Parameters = { new("codecs", "audio/pcm"), new("samplerate", "16000") }
            };

        var client = _http.CreateClient("stt");
        var resp = await client.SendAsync(request, ct);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync(ct);
        // Parse "DisplayText" from the detailed format response
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("DisplayText", out var dt))
            return dt.GetString() ?? string.Empty;
        if (doc.RootElement.TryGetProperty("NBest", out var nbest) && nbest.GetArrayLength() > 0)
            return nbest[0].GetProperty("Display").GetString() ?? string.Empty;

        return string.Empty;
    }

    private async Task<string> GetSpeechTokenAsync(CancellationToken ct)
    {
        // Reuse TtsService's token (cast to access internal method)
        if (_tts is TtsService concrete)
        {
            // Access via reflection-free path — synthesize dummy to prime the token,
            // then grab it. Alternatively, we just call the same credential flow.
        }
        // Directly obtain via TTS service internal call — delegate to shared STS flow
        // by synthesizing with a tiny empty string (the STS exchange is the same endpoint).
        // In practice, share a common ISpeechTokenService. Here we replicate the exchange:
        return await ObtainStsTokenAsync(ct);
    }

    private async Task<string> ObtainStsTokenAsync(CancellationToken ct)
    {
        // This mirrors TtsService's STS exchange but is self-contained.
        // A production refactor would extract a shared ISpeechTokenService.
        var ttsField = typeof(TtsService).GetField("_credential",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var credential = ttsField?.GetValue(_tts) as Azure.Core.TokenCredential;

        if (credential is null)
            throw new InvalidOperationException("Cannot obtain credential from TtsService.");

        const string scope = "https://cognitiveservices.azure.com/.default";
        var tokenCtx = new Azure.Core.TokenRequestContext(new[] { scope });
        var aad = await credential.GetTokenAsync(tokenCtx, ct);

        var stsUrl = $"https://{_opts.AzureSpeechResourceName}.cognitiveservices.azure.com/sts/v1.0/issueToken";
        using var req = new HttpRequestMessage(HttpMethod.Post, stsUrl);
        req.Headers.Add("Authorization", $"Bearer {aad.Token}");
        req.Content = new StringContent(string.Empty, Encoding.UTF8, "application/x-www-form-urlencoded");

        var resp = await _http.CreateClient("sts").SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct);
    }

    private static async Task<byte[]> ConvertMp3ToWavAsync(byte[] mp3Bytes, CancellationToken ct)
    {
        var tmpDir = Path.GetTempPath();
        var inFile = Path.Combine(tmpDir, $"stt_in_{Guid.NewGuid():N}.mp3");
        var outFile = Path.Combine(tmpDir, $"stt_out_{Guid.NewGuid():N}.wav");

        try
        {
            await File.WriteAllBytesAsync(inFile, mp3Bytes, ct);

            using var proc = new Process();
            proc.StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-y -i \"{inFile}\" -ar 16000 -ac 1 -acodec pcm_s16le \"{outFile}\"",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            proc.Start();
            await proc.WaitForExitAsync(ct);
            if (proc.ExitCode != 0)
            {
                var err = await proc.StandardError.ReadToEndAsync(ct);
                throw new InvalidOperationException($"ffmpeg failed: {err[..Math.Min(300, err.Length)]}");
            }

            return await File.ReadAllBytesAsync(outFile, ct);
        }
        finally
        {
            if (File.Exists(inFile)) File.Delete(inFile);
            if (File.Exists(outFile)) File.Delete(outFile);
        }
    }
}
