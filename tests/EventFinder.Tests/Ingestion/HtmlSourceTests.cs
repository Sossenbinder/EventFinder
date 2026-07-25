using AngleSharp.Dom;
using EventFinder.Ingestion.Adapters;
using EventFinder.Ingestion.Contracts;
using EventFinder.Ingestion.Http;
using FluentAssertions;

namespace EventFinder.Tests.Ingestion;

internal sealed class FakeHtmlEventParser(string adapterKey, Func<IDocument, IReadOnlyList<RawEvent>> parse) : IHtmlEventParser
{
    public string AdapterKey => adapterKey;

    public IReadOnlyList<RawEvent> Parse(IDocument document, SourceDescriptor source) => parse(document);
}

internal sealed class AllowAllRobots : IRobotsTxtCache
{
    public Task<bool> IsAllowedAsync(Uri url, CancellationToken ct) => Task.FromResult(true);

    public Task<RobotsRules> GetRulesAsync(Uri url, CancellationToken ct) => Task.FromResult(RobotsRules.AllowAll);
}

internal sealed class DenyAllRobots : IRobotsTxtCache
{
    public Task<bool> IsAllowedAsync(Uri url, CancellationToken ct) => Task.FromResult(false);

    public Task<RobotsRules> GetRulesAsync(Uri url, CancellationToken ct) =>
        Task.FromResult(new RobotsRules(["/"]));
}

public sealed class HtmlSourceTests
{
    private static readonly SourceDescriptor Descriptor = new()
    {
        Id = "meetup-group", Org = "Test Group", Type = "html", Url = "https://example.test/group", Adapter = "test-parser",
    };

    [Fact]
    public async Task FetchAsync_DispatchesToTheParserMatchingTheAdapterKey()
    {
        var httpClient = TestPoliteHttpClient.Create(_ => TestPoliteHttpClient.TextResponse("<html><body><h1>Hi</h1></body></html>"));
        var parser = new FakeHtmlEventParser("test-parser", doc => [new RawEvent
        {
            SourceEventId = "1",
            Title = doc.QuerySelector("h1")!.TextContent,
            Start = DateTimeOffset.UtcNow,
            Url = "https://example.test/group/1",
        }]);
        var source = new HtmlSource(httpClient, new AllowAllRobots(), [parser]);

        var events = await source.FetchAsync(Descriptor, CancellationToken.None);

        events.Should().ContainSingle(e => e.Title == "Hi");
    }

    [Fact]
    public async Task FetchAsync_NoParserRegisteredForAdapterKey_Throws()
    {
        var httpClient = TestPoliteHttpClient.Create(_ => TestPoliteHttpClient.TextResponse("<html></html>"));
        var source = new HtmlSource(httpClient, new AllowAllRobots(), []);

        var act = () => source.FetchAsync(Descriptor, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*test-parser*");
    }

    [Fact]
    public async Task FetchAsync_RobotsDisallowsThePath_ThrowsWithoutFetching()
    {
        var wasFetched = false;
        var httpClient = TestPoliteHttpClient.Create(_ => { wasFetched = true; return TestPoliteHttpClient.TextResponse("<html></html>"); });
        var source = new HtmlSource(httpClient, new DenyAllRobots(), []);

        var act = () => source.FetchAsync(Descriptor, CancellationToken.None);

        await act.Should().ThrowAsync<RobotsDisallowedException>();
        wasFetched.Should().BeFalse();
    }
}
