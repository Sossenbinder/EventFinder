using System.Net;
using EventFinder.Ingestion.Http;

namespace EventFinder.Tests.Ingestion;

// Adapter tests must never touch the network (AGENTS.md); these two doubles
// let PoliteHttpClient run its real logic (headers, retries, caching)
// against canned in-memory responses instead.
internal sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(responder(request));
}

internal sealed class SingleClientHttpClientFactory(HttpClient client) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => client;
}

internal sealed class InMemoryConditionalFetchCache : IConditionalFetchCache
{
    private readonly Dictionary<string, CachedFetch> _entries = [];

    public Task<CachedFetch?> GetAsync(string sourceId, CancellationToken ct) =>
        Task.FromResult(_entries.TryGetValue(sourceId, out var entry) ? entry : null);

    public Task SaveAsync(string sourceId, CachedFetch entry, CancellationToken ct)
    {
        _entries[sourceId] = entry;
        return Task.CompletedTask;
    }
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

internal static class TestPoliteHttpClient
{
    // No delay/retry padding -- these are unit tests, not politeness tests.
    public static IPoliteHttpClient Create(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        new PoliteHttpClient(
            new SingleClientHttpClientFactory(new HttpClient(new FakeHttpMessageHandler(responder))),
            new InMemoryConditionalFetchCache(),
            new PolitenessOptions { PerHostDelay = TimeSpan.Zero, MaxRetries = 0 });

    public static HttpResponseMessage TextResponse(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body) };
}
