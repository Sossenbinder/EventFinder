using System.Diagnostics;
using EventFinder.Core;
using EventFinder.Ingestion.Adapters;
using EventFinder.Ingestion.Contracts;
using EventFinder.Ingestion.Http;
using FluentAssertions;

namespace EventFinder.Tests.Ingestion;

// Fixtures under Fixtures/gdg/ were recorded from real fetches of
// gdg.community.dev on 2026-07-26 (see each file's header comment) --
// AGENTS.md's "adapter tests never hit the network" rule.
public sealed class GdgSitemapSourceTests
{
    private const string IndexUrl = "https://gdg.community.dev/sitemap.xml";
    private const string BerlinEventUrl = "https://gdg.community.dev/events/details/google-gdg-berlin-presents-managing-engineers-in-ai-era/";
    private const string MunichEventUrl = "https://gdg.community.dev/events/details/google-gdg-cloud-munich-presents-scaling-git-for-the-age-of-agentic-workflows-with-arm-feat-gerrit-community/";

    private static readonly Dictionary<string, string> FixtureByUrl = new()
    {
        [IndexUrl] = File.ReadAllText(TestPaths.Fixture("gdg", "sitemap-index.xml")),
        ["https://gdg.community.dev/sitemap-events-2026-07.xml"] = File.ReadAllText(TestPaths.Fixture("gdg", "sitemap-events-2026-07.xml")),
        ["https://gdg.community.dev/sitemap-events-2026-08.xml"] = File.ReadAllText(TestPaths.Fixture("gdg", "sitemap-events-2026-08.xml")),
        [BerlinEventUrl] = File.ReadAllText(TestPaths.Fixture("gdg", "event-berlin-offline.html")),
        [MunichEventUrl] = File.ReadAllText(TestPaths.Fixture("gdg", "event-munich-hybrid.html")),
    };

    private static (IPoliteHttpClient Client, List<string> Requested) CreateTrackingClient()
    {
        var requested = new List<string>();
        var client = TestPoliteHttpClient.Create(req =>
        {
            var url = req.RequestUri!.ToString();
            requested.Add(url);
            if (!FixtureByUrl.TryGetValue(url, out var body))
            {
                throw new InvalidOperationException($"Unexpected request in test: {url}");
            }
            return TestPoliteHttpClient.TextResponse(body);
        });
        return (client, requested);
    }

    private static SourceDescriptor MakeDescriptor(params string[] slugs) => new()
    {
        Id = "gdg-bevy-de", Org = "GDG", Type = "gdg-sitemap", Url = IndexUrl, Slugs = slugs,
    };

    [Fact]
    public async Task FetchAsync_FiltersByChapterSlugAndSkipsOutOfWindowMonths()
    {
        var (client, requested) = CreateTrackingClient();
        // now=2026-07-01: current window is 2026-07..2027-07, so the index's
        // 2015-05 and 2026-06 sub-sitemaps must never be fetched at all.
        var source = new GdgSitemapSource(client, new AllowAllRobots(), new FixedTimeProvider(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)));
        var descriptor = MakeDescriptor("gdg-berlin", "gdg-cloud-munich");

        var events = await source.FetchAsync(descriptor, CancellationToken.None);

        events.Should().HaveCount(2);
        events.Select(e => e.SourceEventId).Should().BeEquivalentTo(
            ["google-gdg-berlin-presents-managing-engineers-in-ai-era", "google-gdg-cloud-munich-presents-scaling-git-for-the-age-of-agentic-workflows-with-arm-feat-gerrit-community"]);

        // gdg-munich-android, gdg-paderborn and gdg-berlin-android are all
        // present in the sitemaps but not in the configured slug list (and
        // "-gdg-berlin-presents-" must not accidentally match inside
        // "-gdg-berlin-android-presents-"), so their detail pages are never
        // requested.
        requested.Should().NotContain(u => u.Contains("berlin-android", StringComparison.Ordinal));
        requested.Should().NotContain(u => u.Contains("munich-android", StringComparison.Ordinal));
        requested.Should().NotContain(u => u.Contains("paderborn", StringComparison.Ordinal));
        requested.Should().NotContain(u => u.Contains("2015-05", StringComparison.Ordinal));
        requested.Should().NotContain(u => u.Contains("2026-06", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FetchAsync_MapsRealJsonLdFields_IncludingHybridArrayLocation()
    {
        var (client, _) = CreateTrackingClient();
        var source = new GdgSitemapSource(client, new AllowAllRobots(), new FixedTimeProvider(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)));
        var descriptor = MakeDescriptor("gdg-berlin", "gdg-cloud-munich");

        var events = await source.FetchAsync(descriptor, CancellationToken.None);

        var berlin = events.Single(e => e.Url == BerlinEventUrl);
        berlin.Title.Should().Be("Managing Engineers In AI Era");
        berlin.Start.Should().Be(new DateTimeOffset(2026, 8, 6, 18, 0, 25, TimeSpan.FromHours(2)));
        berlin.End.Should().Be(new DateTimeOffset(2026, 8, 6, 21, 0, 0, TimeSpan.FromHours(2)));
        berlin.City.Should().Be("Berlin");
        berlin.PostalCode.Should().Be("10997");
        berlin.Latitude.Should().BeNull(); // GDG detail pages never carry coordinates
        berlin.AttendanceHint.Should().Be(Attendance.InPerson); // OfflineEventAttendanceMode

        // Munich's "location" is a JSON array (VirtualLocation + Place) --
        // the real hybrid-event shape -- and must still resolve city/postal
        // from the Place entry, ignoring the unreliable addressCountry ("US"
        // for a Munich venue in the real fetch).
        var munich = events.Single(e => e.Url == MunichEventUrl);
        munich.City.Should().Be("München");
        munich.PostalCode.Should().Be("80636");
        munich.VenueName.Should().Be("Isar Valley");
        munich.AttendanceHint.Should().Be(Attendance.Hybrid); // MixedEventAttendanceMode
        munich.VenueAddress.Should().Contain("80636").And.Contain("München");
    }

    [Fact]
    public async Task FetchAsync_EventThatHasAlreadyEnded_IsSkipped()
    {
        var (client, _) = CreateTrackingClient();
        // Munich ends 2026-07-08T22:00+02:00 (20:00 UTC); Berlin doesn't start
        // until August. "Now" here sits strictly between the two.
        var source = new GdgSitemapSource(client, new AllowAllRobots(), new FixedTimeProvider(new DateTimeOffset(2026, 7, 10, 0, 0, 0, TimeSpan.Zero)));
        var descriptor = MakeDescriptor("gdg-berlin", "gdg-cloud-munich");

        var events = await source.FetchAsync(descriptor, CancellationToken.None);

        events.Should().ContainSingle();
        events[0].Url.Should().Be(BerlinEventUrl);
    }

    [Fact]
    public async Task FetchAsync_NoConfiguredSlugsMatch_ReturnsEmptyRatherThanEverything()
    {
        var (client, _) = CreateTrackingClient();
        var source = new GdgSitemapSource(client, new AllowAllRobots(), new FixedTimeProvider(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)));
        var descriptor = MakeDescriptor("gdg-some-chapter-not-in-the-fixtures");

        var events = await source.FetchAsync(descriptor, CancellationToken.None);

        events.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchAsync_RobotsDisallowsTheIndex_ThrowsWithoutFetching()
    {
        var (client, requested) = CreateTrackingClient();
        var source = new GdgSitemapSource(client, new DenyAllRobots(), new FixedTimeProvider(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)));
        var descriptor = MakeDescriptor("gdg-berlin");

        var act = () => source.FetchAsync(descriptor, CancellationToken.None);

        await act.Should().ThrowAsync<RobotsDisallowedException>();
        requested.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchAsync_HonoursCrawlDelayFromRobotsTxt_BetweenSuccessiveRequests()
    {
        var (client, requested) = CreateTrackingClient();
        var crawlDelayRobots = new FixedCrawlDelayRobots(TimeSpan.FromMilliseconds(150));
        // now=2026-08-01: only the August sitemap (1 monthly fetch) is in
        // window, and only gdg-berlin matches -> index + sitemap + 1 detail
        // page = 3 requests = 2 gaps of (at least) the crawl delay.
        var source = new GdgSitemapSource(client, crawlDelayRobots, new FixedTimeProvider(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)));
        var descriptor = MakeDescriptor("gdg-berlin");

        var stopwatch = Stopwatch.StartNew();
        var events = await source.FetchAsync(descriptor, CancellationToken.None);
        stopwatch.Stop();

        requested.Should().HaveCount(3);
        events.Should().ContainSingle();
        stopwatch.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(280)); // 2 * 150ms, with slack
    }
}

internal sealed class FixedCrawlDelayRobots(TimeSpan crawlDelay) : IRobotsTxtCache
{
    public Task<bool> IsAllowedAsync(Uri url, CancellationToken ct) => Task.FromResult(true);

    public Task<RobotsRules> GetRulesAsync(Uri url, CancellationToken ct) =>
        Task.FromResult(new RobotsRules([], crawlDelay));
}
