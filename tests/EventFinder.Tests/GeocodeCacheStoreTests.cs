using EventFinder.Core;
using EventFinder.Data;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EventFinder.Tests;

public sealed class GeocodeCacheStoreTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<EventFinderDbContext> _options;

    public GeocodeCacheStoreTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<EventFinderDbContext>().UseSqlite(_connection).Options;
        using var ctx = new EventFinderDbContext(_options);
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task FindAsync_UnknownQuery_ReturnsNull()
    {
        await using var ctx = new EventFinderDbContext(_options);
        var cache = new GeocodeCacheStore(ctx);

        (await cache.FindAsync("marienstr 10|70178 stuttgart", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task SaveAsync_ThenFind_RoundTripsAPositiveEntry()
    {
        await using var ctx = new EventFinderDbContext(_options);
        var cache = new GeocodeCacheStore(ctx);
        var resolvedAt = new DateTime(2026, 7, 26, 12, 0, 0, DateTimeKind.Utc);

        await cache.SaveAsync(new GeocodeCacheEntry
        {
            Query = "marienstr 10|70178 stuttgart",
            Found = true,
            Latitude = 48.77362,
            Longitude = 9.17345,
            Precision = LocationPrecision.Address,
            ResolvedAtUtc = resolvedAt,
        }, CancellationToken.None);

        var found = await cache.FindAsync("marienstr 10|70178 stuttgart", CancellationToken.None);

        found.Should().NotBeNull();
        found!.Found.Should().BeTrue();
        found.Latitude.Should().Be(48.77362);
        found.Longitude.Should().Be(9.17345);
        found.Precision.Should().Be(LocationPrecision.Address);
        found.ResolvedAtUtc.Should().Be(resolvedAt);
    }

    [Fact]
    public async Task SaveAsync_NegativeResult_RoundTripsWithFoundFalseAndNoCoordinates()
    {
        await using var ctx = new EventFinderDbContext(_options);
        var cache = new GeocodeCacheStore(ctx);

        await cache.SaveAsync(new GeocodeCacheEntry
        {
            Query = "unfindbare str 1|nirgendwo",
            Found = false,
            Precision = LocationPrecision.None,
            ResolvedAtUtc = DateTime.UtcNow,
        }, CancellationToken.None);

        var found = await cache.FindAsync("unfindbare str 1|nirgendwo", CancellationToken.None);

        found.Should().NotBeNull();
        found!.Found.Should().BeFalse();
        found.Latitude.Should().BeNull();
        found.Longitude.Should().BeNull();
    }

    [Fact]
    public async Task SaveAsync_SameQueryTwice_UpdatesInPlaceRatherThanInserting()
    {
        await using var ctx = new EventFinderDbContext(_options);
        var cache = new GeocodeCacheStore(ctx);

        await cache.SaveAsync(new GeocodeCacheEntry
        {
            Query = "marienstr 10|70178 stuttgart",
            Found = false,
            Precision = LocationPrecision.None,
            ResolvedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        }, CancellationToken.None);

        await cache.SaveAsync(new GeocodeCacheEntry
        {
            Query = "marienstr 10|70178 stuttgart",
            Found = true,
            Latitude = 48.77362,
            Longitude = 9.17345,
            Precision = LocationPrecision.Address,
            ResolvedAtUtc = new DateTime(2026, 7, 26, 0, 0, 0, DateTimeKind.Utc),
        }, CancellationToken.None);

        var rowCount = await ctx.GeocodeCacheEntries.CountAsync(CancellationToken.None);
        rowCount.Should().Be(1);

        var found = await cache.FindAsync("marienstr 10|70178 stuttgart", CancellationToken.None);
        found!.Found.Should().BeTrue();
        found.Latitude.Should().Be(48.77362);
    }
}
