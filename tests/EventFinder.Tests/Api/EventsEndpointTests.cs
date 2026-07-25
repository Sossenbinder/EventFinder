using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using EventFinder.Api.Endpoints;
using EventFinder.Core;
using FluentAssertions;
using Ical.Net;

namespace EventFinder.Tests.Api;

public sealed class EventsEndpointTests : IClassFixture<EventFinderApiFactory>, IAsyncLifetime
{
    // Kirchheim unter Teck itself; Stuttgart is ~25km away (inside a 30km
    // radius); Munich is ~167km away (outside a 30km radius, but still
    // within EventQueryParsing.MaxRadiusKm so a 500km query includes it).
    private const double KirchheimLat = 48.6468;
    private const double KirchheimLon = 9.4538;
    private const double StuttgartLat = 48.78232;
    private const double StuttgartLon = 9.17702;
    private const double MunichLat = 48.1351;
    private const double MunichLon = 11.5820;
    private static readonly DateTime EventStart = new(2026, 9, 10, 18, 0, 0, DateTimeKind.Utc);

    // Attendance serializes as a string (Program.cs registers JsonStringEnumConverter
    // for the *server's* HttpJsonOptions); the test client needs the same
    // converter to deserialize the response with System.Net.Http.Json.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly EventFinderApiFactory _factory;
    private readonly HttpClient _client;

    public EventsEndpointTests(EventFinderApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.SeedEventsAsync(
            "events-endpoint-tests",
            MakeEvent("near", "Stuttgart .NET User Group", EventStart, StuttgartLat, StuttgartLon, "Stuttgart"),
            MakeEvent("far", "Munich Tech Meetup", EventStart.AddHours(1), MunichLat, MunichLon, "Munich"));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetEvents_WithinRadius_ReturnsOnlyNearEventsWithPlausibleDistance()
    {
        var response = await _client.GetAsync(
            $"/api/events?lat={KirchheimLat}&lon={KirchheimLon}&radiusKm=30");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<EventsResponse>(JsonOptions);

        body.Should().NotBeNull();
        body!.Events.Should().ContainSingle(e => e.Title == "Stuttgart .NET User Group");
        body.Events.Should().NotContain(e => e.Title == "Munich Tech Meetup");

        var near = body.Events.Single();
        near.DistanceKm.Should().BeInRange(0, 30);
    }

    [Fact]
    public async Task GetEvents_LargeRadius_IncludesTheFarEventWithALargerReportedDistance()
    {
        var response = await _client.GetAsync(
            $"/api/events?lat={KirchheimLat}&lon={KirchheimLon}&radiusKm=500");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<EventsResponse>(JsonOptions);

        var near = body!.Events.Single(e => e.Title == "Stuttgart .NET User Group");
        var far = body.Events.Single(e => e.Title == "Munich Tech Meetup");
        far.DistanceKm.Should().BeGreaterThan(near.DistanceKm);
    }

    [Theory]
    [InlineData("radiusKm=-5")]
    [InlineData("radiusKm=0")]
    public async Task GetEvents_NonPositiveRadius_ReturnsProblemDetails(string badRadius)
    {
        var response = await _client.GetAsync($"/api/events?lat={KirchheimLat}&lon={KirchheimLon}&{badRadius}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task GetEvents_LatitudeOutOfRange_ReturnsProblemDetails()
    {
        var response = await _client.GetAsync($"/api/events?lat=999&lon={KirchheimLon}&radiusKm=30");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task GetEventsIcs_ReturnsValidICalendarWithOneVEventPerEvent()
    {
        var response = await _client.GetAsync(
            $"/api/events.ics?lat={KirchheimLat}&lon={KirchheimLon}&radiusKm=500");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/calendar");

        var icsText = await response.Content.ReadAsStringAsync();
        var calendar = Calendar.Load(icsText);

        calendar.Should().NotBeNull();
        calendar!.Events.Should().HaveCount(2);
        calendar.Events.Select(e => e.Summary).Should().Contain("Stuttgart .NET User Group");
        calendar.Events.Select(e => e.Uid).Should().OnlyHaveUniqueItems();
    }

    private static Event MakeEvent(string sourceEventId, string title, DateTime startUtc, double lat, double lon, string city) =>
        new()
        {
            SourceId = "events-endpoint-tests",
            SourceEventId = sourceEventId,
            Title = title,
            StartUtc = startUtc,
            TimeZoneId = "Europe/Berlin",
            City = city,
            Latitude = lat,
            Longitude = lon,
            LocationStatus = LocationStatus.Resolved,
            Attendance = Attendance.InPerson,
            Url = $"https://example.test/{sourceEventId}",
            FirstSeenUtc = startUtc,
            DedupeKey = Dedupe.ComputeKey(title, startUtc, "Europe/Berlin", city),
        };
}
