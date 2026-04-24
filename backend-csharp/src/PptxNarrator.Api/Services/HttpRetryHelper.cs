using System.Net;

namespace PptxNarrator.Api.Services;

internal static class HttpRetryHelper
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(2);

    public static async Task<HttpResponseMessage> SendWithRetryAsync(
        HttpClient client,
        Func<HttpRequestMessage> requestFactory,
        ILogger log,
        string operation,
        CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            using var request = requestFactory();

            try
            {
                var response = await client.SendAsync(request, ct);
                if (!IsTransient(response.StatusCode) || attempt >= MaxAttempts)
                    return response;

                var delay = GetDelay(response, attempt);
                log.LogWarning(
                    "{Operation} returned transient HTTP {StatusCode} on attempt {Attempt}/{MaxAttempts}; retrying in {DelayMs} ms",
                    operation,
                    (int)response.StatusCode,
                    attempt,
                    MaxAttempts,
                    delay.TotalMilliseconds);

                response.Dispose();
                await Task.Delay(delay, ct);
            }
            catch (HttpRequestException ex) when (attempt < MaxAttempts)
            {
                var delay = GetDelay(response: null, attempt);
                log.LogWarning(
                    ex,
                    "{Operation} hit a transient transport failure on attempt {Attempt}/{MaxAttempts}; retrying in {DelayMs} ms",
                    operation,
                    attempt,
                    MaxAttempts,
                    delay.TotalMilliseconds);

                await Task.Delay(delay, ct);
            }
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return statusCode == HttpStatusCode.RequestTimeout
            || statusCode == HttpStatusCode.TooManyRequests
            || code >= 500;
    }

    private static TimeSpan GetDelay(HttpResponseMessage? response, int attempt)
    {
        var retryAfter = response?.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
            return delta <= MaxDelay ? delta : MaxDelay;

        if (retryAfter?.Date is { } date)
        {
            var computed = date - DateTimeOffset.UtcNow;
            if (computed > TimeSpan.Zero)
                return computed <= MaxDelay ? computed : MaxDelay;
        }

        var factor = Math.Pow(2, attempt - 1);
        var backoff = TimeSpan.FromMilliseconds(BaseDelay.TotalMilliseconds * factor);
        return backoff <= MaxDelay ? backoff : MaxDelay;
    }
}