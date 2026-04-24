using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using PptxNarrator.Api.Configuration;

namespace PptxNarrator.Tests.Services;

public class TtsServiceTests
{
    private const string FakeStsToken = "fake-sts-token-abc123";
    private const string FakeAadToken = "fake-aad-token-xyz789";

    private static AppOptions DefaultOptions => new()
    {
        AzureSpeechResourceName = "my-speech-resource",
        AzureSpeechRegion = "eastus2",
    };

    [Fact]
    public async Task SynthesizeToMp3Async_ValidInput_ReturnsMp3Bytes()
    {
        var expectedMp3 = Encoding.UTF8.GetBytes("ID3FAKEMP3DATA");
        var (sut, _) = BuildSut(stsResponse: FakeStsToken, ttsResponse: expectedMp3);

        var result = await sut.SynthesizeToMp3Async("Hello world", "en-US-JennyNeural");

        result.Should().Equal(expectedMp3);
    }

    [Fact]
    public async Task SynthesizeToMp3Async_BuildsSsmlWithCorrectVoiceAndLanguage()
    {
        string? capturedBody = null;
        var (sut, handlerMock) = BuildSut(
            stsResponse: FakeStsToken,
            ttsResponse: [],
            captureTtsBody: b => capturedBody = b);

        await sut.SynthesizeToMp3Async("Test text", "fr-FR-DeniseNeural");

        capturedBody.Should().Contain("fr-FR-DeniseNeural");
        capturedBody.Should().Contain("xml:lang=\"fr-FR\"");
        capturedBody.Should().Contain("Test text");
    }

    [Fact]
    public async Task SynthesizeToMp3Async_EscapesXmlSpecialChars()
    {
        var ssml = TtsService.BuildSsml("Rock & Roll <is> \"great\"", "en-US-JennyNeural");

        ssml.Should().Contain("&amp;");
        ssml.Should().Contain("&lt;");
        ssml.Should().Contain("&gt;");
        ssml.Should().Contain("&quot;");
    }

    [Fact]
    public void BuildSsml_DefaultVoice_CorrectLocale()
    {
        var ssml = TtsService.BuildSsml("Hello", "en-US-JennyNeural");

        ssml.Should().Contain("xml:lang=\"en-US\"");
        ssml.Should().Contain("name=\"en-US-JennyNeural\"");
        ssml.Should().Contain("Hello");
    }

    [Fact]
    public void BuildSsml_JapaneseVoice_CorrectLocale()
    {
        var ssml = TtsService.BuildSsml("こんにちは", "ja-JP-NanamiNeural");

        ssml.Should().Contain("xml:lang=\"ja-JP\"");
        ssml.Should().Contain("name=\"ja-JP-NanamiNeural\"");
    }

    [Fact]
    public async Task SynthesizeToMp3Async_TtsHttpError_ThrowsHttpRequestException()
    {
        var (sut, _) = BuildSut(stsResponse: FakeStsToken, ttsStatusCode: HttpStatusCode.Unauthorized);

        await sut.Invoking(s => s.SynthesizeToMp3Async("text", "en-US-JennyNeural"))
            .Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task SynthesizeToMp3Async_CachesStsToken_OnSecondCallDoesNotRefetch()
    {
        int stsCallCount = 0;
        var (sut, _) = BuildSut(
            stsResponse: FakeStsToken,
            ttsResponse: [],
            onStsCall: () => stsCallCount++);

        await sut.SynthesizeToMp3Async("First", "en-US-JennyNeural");
        await sut.SynthesizeToMp3Async("Second", "en-US-JennyNeural");

        stsCallCount.Should().Be(1, "STS token should be cached after first call");
    }

    [Fact]
    public async Task SynthesizeToMp3Async_Transient429_RetriesAndSucceeds()
    {
        int ttsCallCount = 0;
        var expectedMp3 = Encoding.UTF8.GetBytes("ID3FAKEMP3DATA");
        var (sut, _) = BuildSut(
            stsResponse: FakeStsToken,
            ttsResponse: expectedMp3,
            transientTtsFailureBeforeSuccess: true,
            onTtsCall: () => ttsCallCount++);

        var result = await sut.SynthesizeToMp3Async("Hello world", "en-US-JennyNeural");

        result.Should().Equal(expectedMp3);
        ttsCallCount.Should().Be(2);
    }

    // ── Test builder ──────────────────────────────────────────────────────

    private static (TtsService Sut, Mock<HttpMessageHandler> Handler) BuildSut(
        string? stsResponse = null,
        byte[]? ttsResponse = null,
        HttpStatusCode ttsStatusCode = HttpStatusCode.OK,
        Action<string>? captureTtsBody = null,
        Action? onStsCall = null,
        Action? onTtsCall = null,
        bool transientTtsFailureBeforeSuccess = false)
    {
        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        int ttsCallCount = 0;

        // STS token exchange
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r =>
                    r.RequestUri!.AbsoluteUri.Contains("issueToken")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                onStsCall?.Invoke();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(stsResponse ?? FakeStsToken)
                };
            });

        // TTS endpoint — capture body synchronously (ReturnsAsync doesn't accept async lambdas)
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r =>
                    r.RequestUri!.AbsoluteUri.Contains("tts.speech.microsoft.com")),
                ItExpr.IsAny<CancellationToken>())
            .Returns((HttpRequestMessage req, CancellationToken _) =>
            {
                onTtsCall?.Invoke();
                ttsCallCount++;

                if (captureTtsBody is not null)
                    captureTtsBody(req.Content!.ReadAsStringAsync().GetAwaiter().GetResult());

                if (transientTtsFailureBeforeSuccess && ttsCallCount == 1)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                    {
                        Headers = { RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero) }
                    });
                }

                return Task.FromResult(new HttpResponseMessage(ttsStatusCode)
                {
                    Content = new ByteArrayContent(ttsResponse ?? [])
                });
            });

        var factory = new MockHttpClientFactory(handlerMock.Object);

        var credMock = new Mock<Azure.Core.TokenCredential>();
        credMock.Setup(c => c.GetTokenAsync(
                It.IsAny<Azure.Core.TokenRequestContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Azure.Core.AccessToken(FakeAadToken, DateTimeOffset.UtcNow.AddHours(1)));

        var opts = Options.Create(DefaultOptions);
        var sut = new TtsService(factory, credMock.Object, opts, NullLogger<TtsService>.Instance);
        return (sut, handlerMock);
    }

    private sealed class MockHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public MockHttpClientFactory(HttpMessageHandler h) => _handler = h;
        public HttpClient CreateClient(string name) => new(_handler) { BaseAddress = null };
    }
}
