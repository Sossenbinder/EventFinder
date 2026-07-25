using System.Net;
using EventFinder.Ingestion.Http;
using FluentAssertions;

namespace EventFinder.Tests.Ingestion;

public sealed class PoliteHttpClientTests
{
    [Fact]
    public async Task GetAsync_ServerReturns304_ReturnsTheCachedBodyInstead()
    {
        var cache = new InMemoryConditionalFetchCache();
        await cache.SaveAsync("src", new CachedFetch("\"abc\"", null, "cached body", DateTime.UtcNow), CancellationToken.None);

        var handler = new FakeHttpMessageHandler(req =>
        {
            req.Headers.IfNoneMatch.Should().ContainSingle(t => t.Tag == "\"abc\"");
            return new HttpResponseMessage(HttpStatusCode.NotModified);
        });
        var client = new PoliteHttpClient(
            new SingleClientHttpClientFactory(new HttpClient(handler)),
            cache,
            new PolitenessOptions { PerHostDelay = TimeSpan.Zero, MaxRetries = 0 });

        var result = await client.GetAsync("src", "https://example.test/feed", CancellationToken.None);

        result.NotModified.Should().BeTrue();
        result.Body.Should().Be("cached body");
    }

    [Fact]
    public async Task GetAsync_SuccessResponse_SavesEtagAndBodyForNextCall()
    {
        var cache = new InMemoryConditionalFetchCache();
        var handler = new FakeHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("fresh body") };
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"new-etag\"");
            return response;
        });
        var client = new PoliteHttpClient(
            new SingleClientHttpClientFactory(new HttpClient(handler)),
            cache,
            new PolitenessOptions { PerHostDelay = TimeSpan.Zero, MaxRetries = 0 });

        var result = await client.GetAsync("src", "https://example.test/feed", CancellationToken.None);

        result.NotModified.Should().BeFalse();
        result.Body.Should().Be("fresh body");

        var cached = await cache.GetAsync("src", CancellationToken.None);
        cached.Should().NotBeNull();
        cached!.ETag.Should().Be("\"new-etag\"");
        cached.Body.Should().Be("fresh body");
    }

    [Fact]
    public async Task GetAsync_PersistentServerError_ThrowsSourceHttpErrorExceptionWithStatus()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var client = new PoliteHttpClient(
            new SingleClientHttpClientFactory(new HttpClient(handler)),
            new InMemoryConditionalFetchCache(),
            new PolitenessOptions { PerHostDelay = TimeSpan.Zero, MaxRetries = 1, RetryBaseDelay = TimeSpan.Zero });

        var act = () => client.GetAsync("src", "https://example.test/feed", CancellationToken.None);

        (await act.Should().ThrowAsync<SourceHttpErrorException>()).Which.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task GetAsync_NetworkFailureOnEveryAttempt_ThrowsSourceUnreachableException()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("boom"));
        var client = new PoliteHttpClient(
            new SingleClientHttpClientFactory(new HttpClient(handler)),
            new InMemoryConditionalFetchCache(),
            new PolitenessOptions { PerHostDelay = TimeSpan.Zero, MaxRetries = 1, RetryBaseDelay = TimeSpan.Zero });

        var act = () => client.GetAsync("src", "https://example.test/feed", CancellationToken.None);

        await act.Should().ThrowAsync<SourceUnreachableException>();
    }
}
