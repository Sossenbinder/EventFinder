using System.Net;
using EventFinder.Ingestion.Http;
using FluentAssertions;

namespace EventFinder.Tests.Ingestion;

public sealed class RobotsTxtCacheTests
{
    [Fact]
    public void Parse_WildcardDisallow_MatchesOurTokenViaTheStarGroup()
    {
        const string robots = """
            User-agent: *
            Disallow: /private/
            """;

        var rules = RobotsTxtCache.Parse(robots, "EventFinderBot");

        rules.DisallowedPrefixes.Should().ContainSingle().Which.Should().Be("/private/");
    }

    [Fact]
    public void Parse_SpecificGroupForOurToken_TakesPrecedenceOverWildcard()
    {
        const string robots = """
            User-agent: *
            Disallow: /

            User-agent: EventFinderBot
            Disallow: /no-bots-here/
            """;

        var rules = RobotsTxtCache.Parse(robots, "EventFinderBot");

        rules.DisallowedPrefixes.Should().ContainSingle().Which.Should().Be("/no-bots-here/");
    }

    [Fact]
    public void Parse_NoMatchingGroup_AllowsEverything()
    {
        const string robots = """
            User-agent: SomeOtherBot
            Disallow: /
            """;

        var rules = RobotsTxtCache.Parse(robots, "EventFinderBot");

        rules.DisallowedPrefixes.Should().BeEmpty();
    }

    // meetup.com's real robots.txt (fetched 2026-07-26) repeats "User-agent: *"
    // as many separate small stanzas, one per concern, rather than one big
    // group. All of them apply to us; a parser that only kept the first
    // wildcard stanza would silently miss every Disallow after it.
    [Fact]
    public void Parse_RepeatedWildcardStanzas_UnionsDisallowAcrossAllOfThem()
    {
        const string robots = """
            User-agent: *
            Disallow: /files/
            Disallow: /fb/

            User-agent: *
            Disallow: /api/?
            Disallow: /api?

            User-agent: *
            Disallow: */report_abuse/*
            """;

        var rules = RobotsTxtCache.Parse(robots, "EventFinderBot");

        rules.DisallowedPrefixes.Should().BeEquivalentTo(["/files/", "/fb/", "/api/?", "/api?", "*/report_abuse/*"]);
    }

    [Fact]
    public void Parse_CrawlDelay_IsCapturedForTheMatchingGroup()
    {
        const string robots = """
            User-agent: *
            Disallow: /api/
            Crawl-delay: 2
            """;

        var rules = RobotsTxtCache.Parse(robots, "EventFinderBot");

        rules.CrawlDelay.Should().Be(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Parse_NoCrawlDelayDirective_LeavesItNull()
    {
        const string robots = """
            User-agent: *
            Disallow: /private/
            """;

        var rules = RobotsTxtCache.Parse(robots, "EventFinderBot");

        rules.CrawlDelay.Should().BeNull();
    }

    [Fact]
    public async Task IsAllowedAsync_PathUnderDisallowedPrefix_ReturnsFalse()
    {
        var handler = new FakeHttpMessageHandler(req => req.RequestUri!.AbsolutePath == "/robots.txt"
            ? TestPoliteHttpClient.TextResponse("User-agent: *\nDisallow: /events/")
            : new HttpResponseMessage(HttpStatusCode.NotFound));
        var httpClient = new PoliteHttpClient(
            new SingleClientHttpClientFactory(new HttpClient(handler)),
            new InMemoryConditionalFetchCache(),
            new PolitenessOptions { PerHostDelay = TimeSpan.Zero, MaxRetries = 0 });
        var robotsCache = new RobotsTxtCache(httpClient, "EventFinderBot");

        var allowed = await robotsCache.IsAllowedAsync(new Uri("https://example.test/events/group-x"), CancellationToken.None);
        var alsoAllowed = await robotsCache.IsAllowedAsync(new Uri("https://example.test/about"), CancellationToken.None);

        allowed.Should().BeFalse();
        alsoAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task IsAllowedAsync_MissingRobotsTxt_AllowsEverything()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var httpClient = new PoliteHttpClient(
            new SingleClientHttpClientFactory(new HttpClient(handler)),
            new InMemoryConditionalFetchCache(),
            new PolitenessOptions { PerHostDelay = TimeSpan.Zero, MaxRetries = 0 });
        var robotsCache = new RobotsTxtCache(httpClient, "EventFinderBot");

        var allowed = await robotsCache.IsAllowedAsync(new Uri("https://example.test/anything"), CancellationToken.None);

        allowed.Should().BeTrue();
    }

    [Fact]
    public async Task GetRulesAsync_ExposesCrawlDelay_ForCallersThatNeedToThrottleThemselves()
    {
        var handler = new FakeHttpMessageHandler(req => req.RequestUri!.AbsolutePath == "/robots.txt"
            ? TestPoliteHttpClient.TextResponse("User-agent: *\nDisallow: /api/\nCrawl-delay: 2")
            : new HttpResponseMessage(HttpStatusCode.NotFound));
        var httpClient = new PoliteHttpClient(
            new SingleClientHttpClientFactory(new HttpClient(handler)),
            new InMemoryConditionalFetchCache(),
            new PolitenessOptions { PerHostDelay = TimeSpan.Zero, MaxRetries = 0 });
        var robotsCache = new RobotsTxtCache(httpClient, "EventFinderBot");

        var rules = await robotsCache.GetRulesAsync(new Uri("https://example.test/sitemap.xml"), CancellationToken.None);

        rules.CrawlDelay.Should().Be(TimeSpan.FromSeconds(2));
        rules.DisallowedPrefixes.Should().Contain("/api/");
    }
}
