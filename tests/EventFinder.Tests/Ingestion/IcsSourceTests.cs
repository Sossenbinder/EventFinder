using EventFinder.Ingestion.Adapters;
using EventFinder.Ingestion.Contracts;
using EventFinder.Ingestion.Http;
using FluentAssertions;

namespace EventFinder.Tests.Ingestion;

public sealed class IcsSourceTests
{
    // Anchors "now" inside the fixture's recurrence window regardless of
    // wall-clock time, so occurrence counts stay deterministic.
    private static readonly DateTimeOffset FixedNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static IcsSource CreateSource(out string ics)
    {
        ics = File.ReadAllText(TestPaths.Fixture("ics", "user-group.ics"));
        var body = ics;
        var httpClient = TestPoliteHttpClient.Create(_ => TestPoliteHttpClient.TextResponse(body));
        return new IcsSource(httpClient, new AllowAllRobots(), new FixedTimeProvider(FixedNow));
    }

    private static readonly SourceDescriptor Descriptor = new()
    {
        Id = "test-ics", Org = "Test Org", Type = "ics", Url = "https://example.test/calendar.ics",
    };

    [Fact]
    public async Task FetchAsync_WeeklyRecurrence_ExpandsToExactCountOccurrence()
    {
        var source = CreateSource(out _);

        var events = await source.FetchAsync(Descriptor, CancellationToken.None);

        // RRULE:FREQ=WEEKLY;BYDAY=TU;COUNT=8 in the fixture.
        events.Count(e => e.SourceEventId.StartsWith("weekly-meetup@example.test#", StringComparison.Ordinal))
            .Should().Be(8);
    }

    [Fact]
    public async Task FetchAsync_AlreadyEndedEvent_IsSkipped()
    {
        var source = CreateSource(out _);

        var events = await source.FetchAsync(Descriptor, CancellationToken.None);

        events.Should().NotContain(e => e.SourceEventId.StartsWith("already-ended@example.test", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FetchAsync_AllDayEvent_IsMappedWithMidnightUtcStart()
    {
        var source = CreateSource(out _);

        var events = await source.FetchAsync(Descriptor, CancellationToken.None);

        var allDay = events.Single(e => e.SourceEventId.StartsWith("allday-event@example.test", StringComparison.Ordinal));
        allDay.Title.Should().Be("All Day Hack Day");
        allDay.Start.Should().Be(new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task FetchAsync_TimezoneBearingEvent_ResolvesViaVTimezoneToCorrectUtcOffset()
    {
        var source = CreateSource(out _);

        var events = await source.FetchAsync(Descriptor, CancellationToken.None);

        var firstOccurrence = events
            .Where(e => e.SourceEventId.StartsWith("weekly-meetup@example.test#", StringComparison.Ordinal))
            .OrderBy(e => e.Start)
            .First();

        // DTSTART;TZID=Europe/Berlin:20260106T183000 -- 18:30 CET (+01:00) in January.
        firstOccurrence.Start.Should().Be(new DateTimeOffset(2026, 1, 6, 17, 30, 0, TimeSpan.Zero));
        firstOccurrence.TimeZoneId.Should().Be("Europe/Berlin");
        firstOccurrence.VenueAddress.Should().Be("Stuttgart, Germany");
    }

    [Fact]
    public async Task FetchAsync_RobotsDisallowsThePath_ThrowsWithoutFetching()
    {
        var wasFetched = false;
        var httpClient = TestPoliteHttpClient.Create(_ => { wasFetched = true; return TestPoliteHttpClient.TextResponse(""); });
        var source = new IcsSource(httpClient, new DenyAllRobots(), new FixedTimeProvider(FixedNow));

        var act = () => source.FetchAsync(Descriptor, CancellationToken.None);

        await act.Should().ThrowAsync<RobotsDisallowedException>();
        wasFetched.Should().BeFalse();
    }
}
