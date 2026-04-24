using System.Net;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using PptxNarrator.Api.Configuration;
using PptxNarrator.Api.Services;

namespace PptxNarrator.Tests.Services;

public class TranslatorServiceTests
{
    private const string FakeAadToken = "fake-aad-token-xyz789";

    [Fact]
    public async Task TranslateForVoiceAsync_EnglishVoice_PassesTextThrough()
    {
        var (sut, _) = BuildSut();

        var result = await sut.TranslateForVoiceAsync("Hello world", "en-US-JennyNeural");

        result.Should().Be("Hello world");
    }

    [Fact]
    public async Task TranslateForVoiceAsync_Transient429_RetriesAndSucceeds()
    {
        int translatorCallCount = 0;
        var (sut, _) = BuildSut(onTranslatorCall: () => translatorCallCount++);

        var result = await sut.TranslateForVoiceAsync("Hello world", "fr-FR-DeniseNeural");

        result.Should().Be("Bonjour le monde");
        translatorCallCount.Should().Be(2);
    }

    private static (TranslatorService Sut, Mock<HttpMessageHandler> Handler) BuildSut(Action? onTranslatorCall = null)
    {
        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        int callCount = 0;

        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r =>
                    r.RequestUri!.AbsoluteUri.Contains("translator/text/v3.0/translate")),
                ItExpr.IsAny<CancellationToken>())
            .Returns(() =>
            {
                onTranslatorCall?.Invoke();
                callCount++;

                if (callCount == 1)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                    {
                        Headers = { RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero) }
                    });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[{\"translations\":[{\"text\":\"Bonjour le monde\"}]}]")
                });
            });

        var factory = new MockHttpClientFactory(handlerMock.Object);

        var credMock = new Mock<Azure.Core.TokenCredential>();
        credMock.Setup(c => c.GetTokenAsync(
                It.IsAny<Azure.Core.TokenRequestContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Azure.Core.AccessToken(FakeAadToken, DateTimeOffset.UtcNow.AddHours(1)));

        var opts = Options.Create(new AppOptions { AzureSpeechResourceName = "my-speech-resource" });
        var sut = new TranslatorService(factory, credMock.Object, opts, NullLogger<TranslatorService>.Instance);
        return (sut, handlerMock);
    }

    private sealed class MockHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public MockHttpClientFactory(HttpMessageHandler h) => _handler = h;
        public HttpClient CreateClient(string name) => new(_handler) { BaseAddress = null };
    }
}