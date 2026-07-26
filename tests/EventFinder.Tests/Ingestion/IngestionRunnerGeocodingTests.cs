using EventFinder.Core;
using EventFinder.Data;
using EventFinder.Ingestion;
using EventFinder.Ingestion.Contracts;
using EventFinder.Ingestion.Geocoding;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EventFinder.Tests.Ingestion;

// FakeAddressGeocoder records every call it receives so tests can assert
// the "never call Photon for online events or bare city strings" guard,
// and its response is caller-controlled so a single fixture can exercise
// every step of the explicit-coords -> Photon -> gazetteer cascade.
internal sealed class FakeAddressGeocoder(Func<AddressGeocodeResult?>? respond = null) : IAddressGeocoder
{
    public List<(string VenueAddress, string? PostalCode, string? City)> Calls { get; } = [];

    public Task<AddressGeocodeResult?> GeocodeAsync(string venueAddress, string? postalCode, string? city, CancellationToken ct)
    {
        Calls.Add((venueAddress, postalCode, city));
        return Task.FromResult(respond?.Invoke());
    }
}

internal sealed class ThrowingAddressGeocoder : IAddressGeocoder
{
    public Task<AddressGeocodeResult?> GeocodeAsync(string venueAddress, string? postalCode, string? city, CancellationToken ct) =>
        throw new InvalidOperationException("photon exploded");
}

public sealed class IngestionRunnerGeocodingTests : IDisposable
{
    private const double StuttgartLat = 48.78232;
    private const double StuttgartLon = 9.17702;
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Gazetteer Gazetteer = Gazetteer.Load(TestPaths.PlacesCsv, TestPaths.PostalCsv);

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<EventFinderDbContext> _options;

    public IngestionRunnerGeocodingTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<EventFinderDbContext>().UseSqlite(_connection).Options;
        using var ctx = new EventFinderDbContext(_options);
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task RunAsync_StreetAddress_UsesPhotonResultAndAddressPrecision()
    {
        var geocoder = new FakeAddressGeocoder(() => new AddressGeocodeResult(48.77362, 9.17345, LocationPrecision.Address));
        var stored = await RunSingleEventAsync(geocoder, raw => raw with
        {
            VenueAddress = "Marienstr. 10",
            PostalCode = "70178",
            City = "Stuttgart",
        });

        geocoder.Calls.Should().ContainSingle();
        stored.Latitude.Should().BeApproximately(48.77362, 0.0001);
        stored.Longitude.Should().BeApproximately(9.17345, 0.0001);
        stored.LocationStatus.Should().Be(LocationStatus.Resolved);
        stored.LocationPrecision.Should().Be(LocationPrecision.Address);
    }

    [Fact]
    public async Task RunAsync_PhotonReturnsStreetOnlyMatch_SetsStreetPrecision()
    {
        var geocoder = new FakeAddressGeocoder(() => new AddressGeocodeResult(48.7758, 9.183, LocationPrecision.Street));
        var stored = await RunSingleEventAsync(geocoder, raw => raw with
        {
            VenueAddress = "Kronenstraße 5",
            City = "Stuttgart",
        });

        stored.LocationPrecision.Should().Be(LocationPrecision.Street);
    }

    [Fact]
    public async Task RunAsync_OnlineEvent_NeverCallsTheGeocoderEvenWithAVenueAddress()
    {
        var geocoder = new FakeAddressGeocoder(() => new AddressGeocodeResult(48.77362, 9.17345, LocationPrecision.Address));
        await RunSingleEventAsync(geocoder, raw => raw with
        {
            VenueAddress = "Marienstr. 10",
            PostalCode = "70178",
            City = "Stuttgart",
            AttendanceHint = Attendance.Online,
        });

        geocoder.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_BareCityNoStreetAddress_NeverCallsTheGeocoder()
    {
        var geocoder = new FakeAddressGeocoder(() => new AddressGeocodeResult(48.77362, 9.17345, LocationPrecision.Address));
        var stored = await RunSingleEventAsync(geocoder, raw => raw with
        {
            VenueAddress = null,
            City = "Stuttgart",
        });

        geocoder.Calls.Should().BeEmpty();
        stored.LocationPrecision.Should().Be(LocationPrecision.City);
    }

    [Fact]
    public async Task RunAsync_ExplicitSourceCoordinates_SkipsThePhotonLookupEntirely()
    {
        var geocoder = new FakeAddressGeocoder(() => new AddressGeocodeResult(1, 1, LocationPrecision.Address));
        var stored = await RunSingleEventAsync(geocoder, raw => raw with
        {
            Latitude = StuttgartLat,
            Longitude = StuttgartLon,
            VenueAddress = "Marienstr. 10",
            City = "Stuttgart",
        });

        geocoder.Calls.Should().BeEmpty();
        stored.Latitude.Should().Be(StuttgartLat);
        stored.Longitude.Should().Be(StuttgartLon);
        stored.LocationPrecision.Should().Be(LocationPrecision.Address);
    }

    [Fact]
    public async Task RunAsync_PhotonHasNoAnswer_FallsBackToGazetteerCityPrecision()
    {
        var geocoder = new FakeAddressGeocoder(() => null);
        var stored = await RunSingleEventAsync(geocoder, raw => raw with
        {
            VenueAddress = "Unfindbare Str. 1",
            City = "Stuttgart",
        });

        geocoder.Calls.Should().ContainSingle();
        stored.LocationStatus.Should().Be(LocationStatus.Resolved);
        stored.LocationPrecision.Should().Be(LocationPrecision.City);
        stored.Latitude.Should().BeApproximately(StuttgartLat, 0.05);
    }

    [Fact]
    public async Task RunAsync_GeocoderThrows_GazetteerResultIsUsedInstead()
    {
        var stored = await RunSingleEventAsync(new ThrowingAddressGeocoder(), raw => raw with
        {
            VenueAddress = "Marienstr. 10",
            PostalCode = "70178",
            City = "Stuttgart",
        });

        stored.LocationStatus.Should().Be(LocationStatus.Resolved);
        stored.LocationPrecision.Should().Be(LocationPrecision.City);
        stored.Latitude.Should().BeApproximately(StuttgartLat, 0.05);
    }

    [Fact]
    public async Task RunAsync_NoAddressNoCityMatch_IsUnresolvedWithNonePrecision()
    {
        var geocoder = new FakeAddressGeocoder(() => new AddressGeocodeResult(48.77362, 9.17345, LocationPrecision.Address));
        var stored = await RunSingleEventAsync(geocoder, raw => raw with
        {
            VenueAddress = null,
            City = "Nirgendwostadt-Does-Not-Exist",
        });

        stored.LocationStatus.Should().Be(LocationStatus.Unresolved);
        stored.LocationPrecision.Should().Be(LocationPrecision.None);
    }

    private async Task<Event> RunSingleEventAsync(IAddressGeocoder geocoder, Func<RawEvent, RawEvent> customize)
    {
        await using var ctx = new EventFinderDbContext(_options);
        var store = new EventStore(ctx);
        var raw = customize(MakeBaseRawEvent());
        var sourcesByType = new Dictionary<string, IEventSource>
        {
            ["fake"] = new FakeEventSource("fake", _ => [raw]),
        };
        var runner = new IngestionRunner(
            sourcesByType, store, Gazetteer, timeProvider: new FixedTimeProvider(FixedNow), addressGeocoder: geocoder);
        var descriptors = new[]
        {
            new SourceDescriptor { Id = "geo-source", Org = "Geo", Type = "fake", Url = "https://example.test/geo" },
        };

        await runner.RunAsync(descriptors, CancellationToken.None);

        var stored = await ctx.Events.ToListAsync(CancellationToken.None);
        return stored.Should().ContainSingle().Which;
    }

    private static RawEvent MakeBaseRawEvent() => new()
    {
        SourceEventId = "evt-1",
        Title = "Geocoding Test Meetup",
        Start = FixedNow.AddDays(7),
        TimeZoneId = "Europe/Berlin",
        Url = "https://example.test/evt-1",
    };
}
