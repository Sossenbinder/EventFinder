using EventFinder.Core;
using EventFinder.Data;
using EventFinder.Ingestion.Contracts;
using EventFinder.Ingestion.Geocoding;

namespace EventFinder.Ingestion;

// Orchestrates one ingestion pass over the registry: fetch -> Normalization
// -> geo resolve (explicit coords -> Photon address lookup -> Gazetteer's
// PLZ/place-name cascade) -> Dedupe.ComputeKey -> EventStore.UpsertAsync,
// with per-source isolation around the whole pipeline. Returns the run's
// SourceStatus per source; persisting those beyond this process (e.g. to
// SQLite for the /sources page) is the API host's job, not this runner's --
// EventStore currently exposes no SourceStatus persistence to call into.
public sealed class IngestionRunner(
    IReadOnlyDictionary<string, IEventSource> sourcesByType,
    EventStore store,
    Gazetteer gazetteer,
    IReadOnlyDictionary<string, string>? keywordToTag = null,
    TimeProvider? timeProvider = null,
    IAddressGeocoder? addressGeocoder = null)
{
    // A German street name almost always ends in one of these (Straße,
    // Weg, Allee, ...); combined with "contains a digit" (the house number),
    // this is what keeps Photon from ever being asked about a bare city name
    // or an online event's non-address. Deliberately conservative: a false
    // negative here just means the gazetteer handles it as before, a false
    // positive spends one cache-checked Photon lookup on a query that will
    // simply come back empty.
    private static readonly string[] StreetTokens =
    [
        "strasse", "straße", "str.", "weg", "allee", "platz", "gasse", "ring", "damm",
    ];

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly IReadOnlyDictionary<string, string> _keywordToTag = keywordToTag ?? new Dictionary<string, string>();

    public async Task<IReadOnlyDictionary<string, SourceStatus>> RunAsync(
        IEnumerable<SourceDescriptor> sources, CancellationToken ct)
    {
        var statuses = new Dictionary<string, SourceStatus>(StringComparer.Ordinal);

        foreach (var source in sources.Where(s => s.Enabled))
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var status = new SourceStatus { SourceId = source.Id, LastRunUtc = now };
            statuses[source.Id] = status;

            try
            {
                if (!sourcesByType.TryGetValue(source.Type, out var adapter))
                {
                    throw new InvalidOperationException($"No IEventSource registered for type '{source.Type}'.");
                }

                var rawEvents = await adapter.FetchAsync(source, ct);
                var events = new List<Event>(rawEvents.Count);
                foreach (var raw in rawEvents)
                {
                    events.Add(await ToEventAsync(raw, source, now, ct));
                }
                await store.UpsertAsync(events, source.Id, ct);

                status.LastSuccessUtc = now;
                status.EventCount = events.Count;
                status.LastError = null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Per-source isolation (outline Key Decisions / AGENTS.md):
                // one broken adapter must never fail the run or empty the
                // store. Whatever this source upserted on a previous
                // successful run is left exactly as it was.
                status.LastError = ex.Message;
            }
        }

        return statuses;
    }

    private async Task<Event> ToEventAsync(RawEvent raw, SourceDescriptor source, DateTime nowUtc, CancellationToken ct)
    {
        var title = Normalization.CleanTitle(raw.Title);
        var description = raw.Description is null ? null : Normalization.CleanDescription(raw.Description);
        var timeZoneId = string.IsNullOrEmpty(raw.TimeZoneId) ? "UTC" : raw.TimeZoneId;
        var startUtc = raw.Start.UtcDateTime;
        var endUtc = raw.End?.UtcDateTime;

        var (geo, precision) = await ResolveLocationAsync(raw, ct);
        var resolvedCity = geo.MatchedPlace ?? raw.City;

        var titleKeywordTags = Normalization.ExtractTags(title, description, _keywordToTag);
        var tags = source.Tags
            .Concat(titleKeywordTags)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToArray();

        return new Event
        {
            SourceId = source.Id,
            SourceEventId = raw.SourceEventId,
            Title = title,
            Description = description,
            StartUtc = startUtc,
            EndUtc = endUtc,
            TimeZoneId = timeZoneId,
            VenueName = raw.VenueName,
            VenueAddress = raw.VenueAddress,
            City = resolvedCity,
            PostalCode = raw.PostalCode,
            Latitude = geo.Latitude,
            Longitude = geo.Longitude,
            LocationStatus = geo.Status,
            LocationPrecision = precision,
            // ICS/HTML rarely signal attendance mode; in-person is the
            // common case for the meetup/user-group sources this project
            // targets, so it is the default absent a real hint.
            Attendance = raw.AttendanceHint ?? Attendance.InPerson,
            Url = raw.Url,
            Tags = tags,
            FirstSeenUtc = nowUtc,
            LastSeenUtc = nowUtc,
            DedupeKey = Dedupe.ComputeKey(title, startUtc, timeZoneId, resolvedCity),
        };
    }

    // Cascade order per the outline update: explicit source coordinates ->
    // Photon address lookup -> Gazetteer's own PLZ -> place-name fallback ->
    // unresolved. Gazetteer.Resolve already implements the last three steps
    // when handed no explicit lat/lon, so this only has to slot the address
    // geocoder in between step 1 and the rest.
    private async Task<(GeoResolution Geo, LocationPrecision Precision)> ResolveLocationAsync(RawEvent raw, CancellationToken ct)
    {
        if (raw.Latitude is not null && raw.Longitude is not null)
        {
            var explicitGeo = gazetteer.Resolve(raw.Latitude, raw.Longitude, raw.PostalCode, raw.VenueAddress, raw.City);
            // Coordinates supplied directly by the source (Bevy's _geoloc,
            // ICS GEO) already pin a specific venue, not a town centroid.
            return (explicitGeo, LocationPrecision.Address);
        }

        if (addressGeocoder is not null
            && raw.AttendanceHint != Attendance.Online
            && LooksLikeStreetAddress(raw.VenueAddress))
        {
            AddressGeocodeResult? addressResult = null;
            try
            {
                addressResult = await addressGeocoder.GeocodeAsync(raw.VenueAddress!, raw.PostalCode, raw.City, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Belt-and-suspenders on top of PhotonAddressGeocoder's own
                // internal try/catch: geocoding must never fail a run, so
                // any IAddressGeocoder that throws just falls through to the
                // gazetteer cascade below instead of failing this source.
            }

            if (addressResult is not null)
            {
                var geo = new GeoResolution(addressResult.Latitude, addressResult.Longitude, raw.City, LocationStatus.Resolved);
                return (geo, addressResult.Precision);
            }
        }

        var gazetteerGeo = gazetteer.Resolve(null, null, raw.PostalCode, raw.VenueAddress, raw.City);
        var precision = gazetteerGeo.Status == LocationStatus.Resolved ? LocationPrecision.City : LocationPrecision.None;
        return (gazetteerGeo, precision);
    }

    // "Street-ish": contains a digit (almost always the house number) or a
    // recognisable German street-name token. Never true for null/empty, so
    // online events (no venue address at all) and bare city strings never
    // reach the geocoder.
    private static bool LooksLikeStreetAddress(string? venueAddress)
    {
        if (string.IsNullOrWhiteSpace(venueAddress))
        {
            return false;
        }

        if (venueAddress.Any(char.IsDigit))
        {
            return true;
        }

        var folded = Normalization.Fold(venueAddress);
        return StreetTokens.Any(token => folded.Contains(Normalization.Fold(token), StringComparison.Ordinal));
    }
}
