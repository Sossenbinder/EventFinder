using System.Net.Http.Json;
using System.Text.Json;
using EventFinder.Core;
using EventFinder.Data;
using Microsoft.Extensions.Logging;

namespace EventFinder.Ingestion.Geocoding;

// Address-level geocoding via Photon (Komoot), a free, keyless service built
// on OpenStreetMap/Nominatim data. Verified live 2026-07-26:
//   GET {Endpoint}?q=<query>&limit=1&lang=de
//   -> {"features":[{"geometry":{"coordinates":[lon,lat]},
//                     "properties":{"street","housenumber","postcode","city","name"}}]}
// "Marienstr. 10, 70178 Stuttgart" resolved to housenumber precision;
// some addresses only resolve to street level, which this treats as strictly
// better than a gazetteer centroid but worse than a housenumber match.
//
// One instance is meant to live for exactly one ingestion run (registered
// Scoped, like EventStore) -- that is what makes MaxLookupsPerRun and the
// 1-request-per-second gate reset naturally between runs without a separate
// "start a new run" API.
public sealed partial class PhotonAddressGeocoder(
    IHttpClientFactory httpClientFactory,
    GeocodeCacheStore cache,
    GeocodingOptions options,
    ILogger<PhotonAddressGeocoder> logger,
    TimeProvider? timeProvider = null) : IAddressGeocoder, IDisposable
{
    // Germany's approximate bounding box (task brief). A Photon result
    // outside it is a bad match, not a real venue -- prefer the gazetteer.
    private const double MinLat = 47.2;
    private const double MaxLat = 55.1;
    private const double MinLon = 5.8;
    private const double MaxLon = 15.1;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim _rateGate = new(1, 1);
    private DateTimeOffset _nextAllowedUtc = DateTimeOffset.MinValue;
    private int _lookupsThisRun;
    private bool _loggedCapHit;

    public void Dispose() => _rateGate.Dispose();

    public async Task<AddressGeocodeResult?> GeocodeAsync(string venueAddress, string? postalCode, string? city, CancellationToken ct)
    {
        if (!options.Enabled)
        {
            return null;
        }

        var query = BuildQuery(venueAddress, postalCode, city);
        var cacheKey = Normalization.Fold(query);

        var cached = await cache.FindAsync(cacheKey, ct);
        if (cached is not null)
        {
            return cached.Found
                ? new AddressGeocodeResult(cached.Latitude!.Value, cached.Longitude!.Value, cached.Precision)
                : null;
        }

        if (_lookupsThisRun >= options.MaxLookupsPerRun)
        {
            if (!_loggedCapHit)
            {
                Log.MaxLookupsReached(logger, options.MaxLookupsPerRun);
                _loggedCapHit = true;
            }
            return null;
        }

        try
        {
            _lookupsThisRun++;
            var result = await FetchAsync(query, ct);

            await cache.SaveAsync(new GeocodeCacheEntry
            {
                Query = cacheKey,
                Found = result is not null,
                Latitude = result?.Latitude,
                Longitude = result?.Longitude,
                Precision = result?.Precision ?? LocationPrecision.None,
                ResolvedAtUtc = _timeProvider.GetUtcNow().UtcDateTime,
            }, ct);

            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Geocoding must never fail a run -- degrade silently to
            // whatever the gazetteer cascade produces.
            Log.GeocodingFailed(logger, ex, query);
            return null;
        }
    }

    private async Task<AddressGeocodeResult?> FetchAsync(string query, CancellationToken ct)
    {
        await WaitForRateLimitAsync(ct);

        var client = httpClientFactory.CreateClient(GeocodingOptions.HttpClientName);
        var url = $"{options.Endpoint}?q={Uri.EscapeDataString(query)}&limit=1&lang=de";
        using var response = await client.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync<PhotonResponse>(JsonOptions, ct);
        return ParseBestFeature(payload);
    }

    // Public (like Http/RobotsTxtCache.Parse) so tests can feed a recorded
    // Photon fixture straight through this parsing/precision/bounding-box
    // logic without a live HTTP call.
    public static AddressGeocodeResult? ParseBestFeature(PhotonResponse? payload)
    {
        var feature = payload?.Features?.FirstOrDefault();
        var coordinates = feature?.Geometry?.Coordinates;
        if (coordinates is not { Length: 2 })
        {
            return null;
        }

        var lon = coordinates[0];
        var lat = coordinates[1];
        if (lat < MinLat || lat > MaxLat || lon < MinLon || lon > MaxLon)
        {
            return null;
        }

        var properties = feature!.Properties;
        var hasHousenumber = !string.IsNullOrEmpty(properties?.Housenumber);
        var hasStreet = !string.IsNullOrEmpty(properties?.Street);
        if (!hasHousenumber && !hasStreet)
        {
            // Photon matched something coarser than street level (a city, a
            // POI); that is no better than the gazetteer already gives us.
            return null;
        }

        return new AddressGeocodeResult(lat, lon, hasHousenumber ? LocationPrecision.Address : LocationPrecision.Street);
    }

    private async Task WaitForRateLimitAsync(CancellationToken ct)
    {
        await _rateGate.WaitAsync(ct);
        try
        {
            var now = _timeProvider.GetUtcNow();
            var wait = _nextAllowedUtc - now;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, ct);
            }
            _nextAllowedUtc = _timeProvider.GetUtcNow() + TimeSpan.FromMilliseconds(options.DelayMs);
        }
        finally
        {
            _rateGate.Release();
        }
    }

    // RawEvent carries no country field -- every source this project ingests
    // is Germany-only (outline scope), so "Deutschland" is always appended.
    private static string BuildQuery(string venueAddress, string? postalCode, string? city)
    {
        var parts = new List<string> { venueAddress };
        var postalAndCity = string.Join(' ', new[] { postalCode, city }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (postalAndCity.Length > 0)
        {
            parts.Add(postalAndCity);
        }
        parts.Add("Deutschland");
        return string.Join(", ", parts);
    }

    public sealed record PhotonResponse(PhotonFeature[]? Features);
    public sealed record PhotonFeature(PhotonGeometry? Geometry, PhotonProperties? Properties);
    public sealed record PhotonGeometry(double[]? Coordinates);
    public sealed record PhotonProperties(string? Street, string? Housenumber, string? Postcode, string? City, string? Name);

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Geocoding: MaxLookupsPerRun ({Cap}) reached for this ingestion run; remaining addresses fall back to the gazetteer.")]
        public static partial void MaxLookupsReached(ILogger logger, int cap);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Photon geocoding failed for query '{Query}'; falling back to the gazetteer.")]
        public static partial void GeocodingFailed(ILogger logger, Exception ex, string query);
    }
}
