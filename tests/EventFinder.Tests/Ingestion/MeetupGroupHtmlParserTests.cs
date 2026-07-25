using AngleSharp;
using EventFinder.Core;
using EventFinder.Ingestion.Adapters;
using EventFinder.Ingestion.Contracts;
using FluentAssertions;

namespace EventFinder.Tests.Ingestion;

// Fixtures under Fixtures/meetup/ were recorded from real fetches of
// meetup.com group /events/ pages on 2026-07-26 (see each file's header
// comment) -- AGENTS.md's "adapter tests never hit the network" rule.
public sealed class MeetupGroupHtmlParserTests
{
    private static readonly SourceDescriptor Descriptor = new()
    {
        Id = "meetup-hackergarten-stuttgart", Org = "Hackergarten Stuttgart", Type = "html",
        Adapter = "meetup-group", Url = "https://www.meetup.com/hackergarten-stuttgart/events/",
    };

    private static async Task<AngleSharp.Dom.IDocument> LoadAsync(string fixtureFileName)
    {
        var html = await File.ReadAllTextAsync(TestPaths.Fixture("meetup", fixtureFileName));
        var context = BrowsingContext.New(Configuration.Default);
        return await context.OpenAsync(req => req.Content(html));
    }

    [Fact]
    public async Task Parse_RealFixture_ResolvesVenueRefAndMapsPhysicalAttendance()
    {
        using var document = await LoadAsync("hackergarten-stuttgart-events.html");
        var parser = new MeetupGroupHtmlParser(new FixedTimeProvider(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)));

        var events = parser.Parse(document, Descriptor);

        var real = events.Single(e => e.SourceEventId == "312178333");
        real.Title.Should().Be("66. Hackergarten Stuttgart");
        real.Start.Should().Be(new DateTimeOffset(2026, 8, 4, 17, 30, 0, TimeSpan.FromHours(2)));
        real.End.Should().Be(new DateTimeOffset(2026, 8, 4, 20, 30, 0, TimeSpan.FromHours(2)));
        real.VenueName.Should().Be("codecentric AG");
        real.VenueAddress.Should().Be("Industriestraße 3");
        real.City.Should().Be("Stuttgart");
        real.AttendanceHint.Should().Be(Attendance.InPerson);
        real.Url.Should().Be("https://www.meetup.com/hackergarten-stuttgart/events/312178333/");
    }

    [Fact]
    public async Task Parse_OnlineEventWithNoVenue_HasNullVenueFieldsAndOnlineAttendance()
    {
        using var document = await LoadAsync("hackergarten-stuttgart-events.html");
        var parser = new MeetupGroupHtmlParser(new FixedTimeProvider(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)));

        var events = parser.Parse(document, Descriptor);

        var online = events.Single(e => e.SourceEventId == "900000001");
        online.VenueName.Should().BeNull();
        online.VenueAddress.Should().BeNull();
        online.City.Should().BeNull();
        online.AttendanceHint.Should().Be(Attendance.Online);
    }

    [Fact]
    public async Task Parse_HybridEvent_MapsToHybridAttendance()
    {
        using var document = await LoadAsync("hackergarten-stuttgart-events.html");
        var parser = new MeetupGroupHtmlParser(new FixedTimeProvider(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)));

        var events = parser.Parse(document, Descriptor);

        events.Single(e => e.SourceEventId == "900000002").AttendanceHint.Should().Be(Attendance.Hybrid);
    }

    [Fact]
    public async Task Parse_EventThatHasAlreadyEnded_IsSkipped()
    {
        using var document = await LoadAsync("hackergarten-stuttgart-events.html");
        var parser = new MeetupGroupHtmlParser(new FixedTimeProvider(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)));

        var events = parser.Parse(document, Descriptor);

        events.Should().NotContain(e => e.SourceEventId == "900000003");
    }

    [Fact]
    public async Task Parse_GroupWithZeroUpcomingEvents_ReturnsEmptyListRatherThanThrowing()
    {
        using var document = await LoadAsync("empty-group-events.html");
        var parser = new MeetupGroupHtmlParser();

        var events = parser.Parse(document, Descriptor);

        events.Should().BeEmpty();
    }
}
