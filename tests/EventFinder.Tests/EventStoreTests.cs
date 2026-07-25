using EventFinder.Core;
using EventFinder.Data;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EventFinder.Tests;

public sealed class EventStoreTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<EventFinderDbContext> _options;

    public EventStoreTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<EventFinderDbContext>().UseSqlite(_connection).Options;

        using var ctx = new EventFinderDbContext(_options);
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task UpsertAsync_SameBatchTwice_YieldsTheSameRowCount()
    {
        await using var ctx = new EventFinderDbContext(_options);
        var store = new EventStore(ctx);
        var start = new DateTime(2026, 9, 10, 18, 0, 0, DateTimeKind.Utc);
        var batch = new[]
        {
            MakeEvent("evt-1", "DotNet User Group Stuttgart", start, StuttgartLat, StuttgartLon, "Stuttgart"),
            MakeEvent("evt-2", "GDG Stuttgart Meetup", start.AddDays(7), StuttgartLat, StuttgartLon, "Stuttgart"),
        };

        await store.UpsertAsync(batch, "gdg-stuttgart", CancellationToken.None);
        var countAfterFirstRun = await ctx.Events.CountAsync(CancellationToken.None);

        await store.UpsertAsync(batch, "gdg-stuttgart", CancellationToken.None);
        var countAfterSecondRun = await ctx.Events.CountAsync(CancellationToken.None);

        countAfterFirstRun.Should().Be(2);
        countAfterSecondRun.Should().Be(2);
    }

    [Fact]
    public async Task UpsertAsync_ChangedTitleForKnownSourceEvent_UpdatesInPlaceRatherThanInserting()
    {
        await using var ctx = new EventFinderDbContext(_options);
        var store = new EventStore(ctx);
        var start = new DateTime(2026, 9, 10, 18, 0, 0, DateTimeKind.Utc);

        await store.UpsertAsync(
            [MakeEvent("evt-1", "DotNet User Group Stuttgart", start, StuttgartLat, StuttgartLon, "Stuttgart")],
            "gdg-stuttgart",
            CancellationToken.None);

        await store.UpsertAsync(
            [MakeEvent("evt-1", "DotNet User Group Stuttgart (renamed)", start, StuttgartLat, StuttgartLon, "Stuttgart")],
            "gdg-stuttgart",
            CancellationToken.None);

        var events = await ctx.Events.ToListAsync(CancellationToken.None);
        events.Should().ContainSingle();
        events[0].Title.Should().Be("DotNet User Group Stuttgart (renamed)");
    }

    [Fact]
    public async Task QueryAsync_FiltersByRadiusUsingBoundingBoxThenExactHaversine()
    {
        await using var ctx = new EventFinderDbContext(_options);
        var store = new EventStore(ctx);
        var start = new DateTime(2026, 9, 10, 18, 0, 0, DateTimeKind.Utc);

        await store.UpsertAsync(
            [
                MakeEvent("near", "Nearby Meetup", start, StuttgartLat, StuttgartLon, "Stuttgart"),
                MakeEvent("far", "Distant Meetup", start, BerlinLat, BerlinLon, "Berlin"),
            ],
            "gdg-stuttgart",
            CancellationToken.None);

        var results = await store.QueryAsync(
            KirchheimLat, KirchheimLon, radiusKm: 30, from: null, to: null, tags: null, attendance: null,
            CancellationToken.None);

        results.Should().ContainSingle(e => e.SourceEventId == "near");
    }

    [Fact]
    public async Task QueryAsync_UnresolvedEvents_AreNeverReturned()
    {
        await using var ctx = new EventFinderDbContext(_options);
        var store = new EventStore(ctx);
        var start = new DateTime(2026, 9, 10, 18, 0, 0, DateTimeKind.Utc);
        var unresolved = MakeEvent("unresolved", "Unknown Venue Meetup", start, StuttgartLat, StuttgartLon, "Stuttgart");
        unresolved.Latitude = null;
        unresolved.Longitude = null;
        unresolved.LocationStatus = LocationStatus.Unresolved;

        await store.UpsertAsync([unresolved], "gdg-stuttgart", CancellationToken.None);

        var results = await store.QueryAsync(
            KirchheimLat, KirchheimLon, radiusKm: 1000, from: null, to: null, tags: null, attendance: null,
            CancellationToken.None);

        results.Should().BeEmpty();
    }

    private const double KirchheimLat = 48.64683;
    private const double KirchheimLon = 9.45378;
    private const double StuttgartLat = 48.78232;
    private const double StuttgartLon = 9.17702;
    private const double BerlinLat = 52.52437;
    private const double BerlinLon = 13.41053;

    private static Event MakeEvent(string sourceEventId, string title, DateTime startUtc, double lat, double lon, string city) =>
        new()
        {
            SourceId = "gdg-stuttgart",
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
