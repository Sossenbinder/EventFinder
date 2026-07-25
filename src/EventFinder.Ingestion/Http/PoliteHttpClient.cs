using System.Collections.Concurrent;
using System.Globalization;
using System.Net;

namespace EventFinder.Ingestion.Http;

public sealed class PoliteHttpClient(
    IHttpClientFactory httpClientFactory,
    IConditionalFetchCache cache,
    PolitenessOptions options) : IPoliteHttpClient
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _hostGates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _nextAllowedUtc = new(StringComparer.OrdinalIgnoreCase);

    public async Task<PoliteFetchResult> GetAsync(string sourceId, string url, CancellationToken ct)
    {
        var cached = await cache.GetAsync(sourceId, ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (cached is not null)
        {
            ApplyConditionalHeaders(request, cached);
        }

        using var response = await SendWithPolitenessAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.NotModified && cached is not null)
        {
            return new PoliteFetchResult(cached.Body, NotModified: true);
        }

        EnsureSuccess(response, url);
        var body = await response.Content.ReadAsStringAsync(ct);
        var freshEntry = new CachedFetch(
            response.Headers.ETag?.Tag,
            response.Content.Headers.LastModified?.UtcDateTime.ToString("R", CultureInfo.InvariantCulture),
            body,
            DateTime.UtcNow);
        await cache.SaveAsync(sourceId, freshEntry, ct);
        return new PoliteFetchResult(body, NotModified: false);
    }

    public async Task<string> GetRawAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await SendWithPolitenessAsync(request, ct);
        EnsureSuccess(response, url);
        return await response.Content.ReadAsStringAsync(ct);
    }

    private async Task<HttpResponseMessage> SendWithPolitenessAsync(HttpRequestMessage template, CancellationToken ct)
    {
        var host = template.RequestUri!.Host;
        var client = httpClientFactory.CreateClient(PolitenessOptions.HttpClientName);

        for (var attempt = 0; ; attempt++)
        {
            await WaitForHostSlotAsync(host, ct);
            var isClone = attempt > 0;
            var request = isClone ? CloneRequest(template) : template;
            var isLastAttempt = attempt == options.MaxRetries;

            try
            {
                HttpResponseMessage response;
                try
                {
                    response = await client.SendAsync(request, ct);
                }
                catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException) && !ct.IsCancellationRequested)
                {
                    if (isLastAttempt)
                    {
                        throw new SourceUnreachableException($"Failed to reach {template.RequestUri}: {ex.Message}", ex);
                    }
                    await Task.Delay(BackoffDelay(attempt), ct);
                    continue;
                }

                if (!isLastAttempt && IsTransientStatus(response.StatusCode))
                {
                    response.Dispose();
                    await Task.Delay(BackoffDelay(attempt), ct);
                    continue;
                }

                return response;
            }
            finally
            {
                if (isClone)
                {
                    request.Dispose();
                }
            }
        }
    }

    private async Task WaitForHostSlotAsync(string host, CancellationToken ct)
    {
        var gate = _hostGates.GetOrAdd(host, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (_nextAllowedUtc.TryGetValue(host, out var nextAllowed))
            {
                var wait = nextAllowed - DateTime.UtcNow;
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, ct);
                }
            }
            _nextAllowedUtc[host] = DateTime.UtcNow + options.PerHostDelay;
        }
        finally
        {
            gate.Release();
        }
    }

    private TimeSpan BackoffDelay(int attempt) =>
        options.RetryBaseDelay * Math.Pow(2, attempt);

    private static void ApplyConditionalHeaders(HttpRequestMessage request, CachedFetch cached)
    {
        if (cached.ETag is not null && System.Net.Http.Headers.EntityTagHeaderValue.TryParse(cached.ETag, out var etag))
        {
            request.Headers.IfNoneMatch.Add(etag);
            return;
        }
        if (cached.LastModified is not null
            && DateTimeOffset.TryParse(cached.LastModified, CultureInfo.InvariantCulture, DateTimeStyles.None, out var lastModified))
        {
            request.Headers.IfModifiedSince = lastModified;
        }
    }

    private static void EnsureSuccess(HttpResponseMessage response, string url)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new SourceHttpErrorException(response.StatusCode, url);
        }
    }

    private static bool IsTransientStatus(HttpStatusCode status) =>
        status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

    private static HttpRequestMessage CloneRequest(HttpRequestMessage source)
    {
        var clone = new HttpRequestMessage(source.Method, source.RequestUri);
        foreach (var header in source.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        return clone;
    }
}
