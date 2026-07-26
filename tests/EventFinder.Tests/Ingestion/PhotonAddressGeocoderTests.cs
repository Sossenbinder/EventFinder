using System.Net;
using System.Text.Json;
using EventFinder.Core;
using EventFinder.Data;
using EventFinder.Ingestion.Geocoding;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace EventFinder.Tests.Ingestion;

// PhotonAddressGeocoder never touches the network in tests (AGENTS.md):
// ParseBestFeature is exercised directly against recorded Photon JSON
// fixtures, and GeocodeAsync's HTTP calls go through FakeHttpMessageHandler.
public sealed class PhotonAddressGeocoderTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<EventFinderDbContext> _options;

    public PhotonAddressGeocoderTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<EventFinderDbContext>().UseSqlite(_connection).Options;
        using var ctx = new EventFinderDbContext(_options);
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public void ParseBestFeature_HousenumberInResponse_ReturnsAddressPrecision()
    {
        var payload = LoadFixture("photon-housenumber-response.json");

        var result = PhotonAddressGeocoder.ParseBestFeature(payload);

        result.Should().NotBeNull();
        result!.Precision.Should().Be(LocationPrecision.Address);
        result.Latitude.Should().BeApproximately(48.77362, 0.0001);
        result.Longitude.Should().BeApproximately(9.17345, 0.0001);
    }

    [Fact]
    public void ParseBestFeature_StreetOnlyResponse_ReturnsStreetPrecision()
    {
        var payload = LoadFixture("photon-street-response.json");

        var result = PhotonAddressGeocoder.ParseBestFeature(payload);

        result.Should().NotBeNull();
        result!.Precision.Should().Be(LocationPrecision.Street);
    }

    [Fact]
    public void ParseBestFeature_NoFeatures_ReturnsNull()
    {
        var payload = LoadFixture("photon-empty-response.json");

        PhotonAddressGeocoder.ParseBestFeature(payload).Should().BeNull();
    }

    [Fact]
    public void ParseBestFeature_CoordinatesOutsideGermany_AreRejected()
    {
        var payload = LoadFixture("photon-out-of-germany-response.json");

        PhotonAddressGeocoder.ParseBestFeature(payload).Should().BeNull();
    }

    [Fact]
    public async Task GeocodeAsync_SameAddressTwice_OnlyHitsTheNetworkOnce()
    {
        var requestCount = 0;
        var handler = new FakeHttpMessageHandler(_ =>
        {
            requestCount++;
            return JsonResponse("photon-housenumber-response.json");
        });
        await using var ctx = new EventFinderDbContext(_options);
        var geocoder = CreateGeocoder(handler, ctx);

        var first = await geocoder.GeocodeAsync("Marienstr. 10", "70178", "Stuttgart", CancellationToken.None);
        var second = await geocoder.GeocodeAsync("Marienstr. 10", "70178", "Stuttgart", CancellationToken.None);

        requestCount.Should().Be(1);
        first.Should().NotBeNull();
        second.Should().BeEquivalentTo(first);
    }

    [Fact]
    public async Task GeocodeAsync_NegativeResultIsCached_SecondCallSkipsTheNetworkToo()
    {
        var requestCount = 0;
        var handler = new FakeHttpMessageHandler(_ =>
        {
            requestCount++;
            return JsonResponse("photon-empty-response.json");
        });
        await using var ctx = new EventFinderDbContext(_options);
        var geocoder = CreateGeocoder(handler, ctx);

        var first = await geocoder.GeocodeAsync("Nirgendweg 1", "00000", "Nirgendwo", CancellationToken.None);
        var second = await geocoder.GeocodeAsync("Nirgendweg 1", "00000", "Nirgendwo", CancellationToken.None);

        requestCount.Should().Be(1);
        first.Should().BeNull();
        second.Should().BeNull();
    }

    [Fact]
    public async Task GeocodeAsync_HttpClientThrows_ReturnsNullInsteadOfPropagating()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("boom"));
        await using var ctx = new EventFinderDbContext(_options);
        var geocoder = CreateGeocoder(handler, ctx);

        var result = await geocoder.GeocodeAsync("Marienstr. 10", "70178", "Stuttgart", CancellationToken.None);

        result.Should().BeNull();
    }

    private static PhotonAddressGeocoder CreateGeocoder(FakeHttpMessageHandler handler, EventFinderDbContext ctx) =>
        new(
            new SingleClientHttpClientFactory(new HttpClient(handler)),
            new GeocodeCacheStore(ctx),
            new GeocodingOptions(),
            NullLogger<PhotonAddressGeocoder>.Instance,
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 26, 0, 0, 0, TimeSpan.Zero)));

    private static HttpResponseMessage JsonResponse(string fixtureFileName) =>
        new(HttpStatusCode.OK) { Content = new StringContent(File.ReadAllText(TestPaths.Fixture("geocoding", fixtureFileName))) };

    private static PhotonAddressGeocoder.PhotonResponse LoadFixture(string fixtureFileName)
    {
        var json = File.ReadAllText(TestPaths.Fixture("geocoding", fixtureFileName));
        return JsonSerializer.Deserialize<PhotonAddressGeocoder.PhotonResponse>(json, JsonOptions)!;
    }
}
