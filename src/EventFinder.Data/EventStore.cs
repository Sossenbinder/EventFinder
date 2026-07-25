using EventFinder.Core;
using Microsoft.EntityFrameworkCore;

namespace EventFinder.Data;

public sealed class EventStore(EventFinderDbContext db)
{
    // Upsert is scoped to a single source's batch: cross-source identity is
    // the (SourceId, SourceEventId) unique index, not DedupeKey. Merging
    // same-DedupeKey events *across different sources* into one card (with a
    // combined URL list, per the outline's dedupe decision) is a read-time
    // concern for the API layer, not something this store rewrites in place.
    // Here, "collapse duplicates by DedupeKey" only guards against a single
    // feed accidentally listing the same occurrence twice in one batch.
    public async Task UpsertAsync(IEnumerable<Event> events, string sourceId, CancellationToken ct)
    {
        var deduped = events
            .GroupBy(e => e.DedupeKey)
            .Select(g => g.OrderBy(e => e.FirstSeenUtc).First());

        var now = DateTime.UtcNow;
        foreach (var incoming in deduped)
        {
            var existing = await db.Events.SingleOrDefaultAsync(
                e => e.SourceId == sourceId && e.SourceEventId == incoming.SourceEventId, ct);

            if (existing is null)
            {
                db.Events.Add(new Event
                {
                    SourceId = sourceId,
                    SourceEventId = incoming.SourceEventId,
                    Title = incoming.Title,
                    Description = incoming.Description,
                    StartUtc = incoming.StartUtc,
                    EndUtc = incoming.EndUtc,
                    TimeZoneId = incoming.TimeZoneId,
                    VenueName = incoming.VenueName,
                    VenueAddress = incoming.VenueAddress,
                    City = incoming.City,
                    PostalCode = incoming.PostalCode,
                    Latitude = incoming.Latitude,
                    Longitude = incoming.Longitude,
                    LocationStatus = incoming.LocationStatus,
                    Attendance = incoming.Attendance,
                    Url = incoming.Url,
                    Tags = incoming.Tags,
                    FirstSeenUtc = now,
                    LastSeenUtc = now,
                    DedupeKey = incoming.DedupeKey,
                });
            }
            else
            {
                existing.Title = incoming.Title;
                existing.Description = incoming.Description;
                existing.StartUtc = incoming.StartUtc;
                existing.EndUtc = incoming.EndUtc;
                existing.TimeZoneId = incoming.TimeZoneId;
                existing.VenueName = incoming.VenueName;
                existing.VenueAddress = incoming.VenueAddress;
                existing.City = incoming.City;
                existing.PostalCode = incoming.PostalCode;
                existing.Latitude = incoming.Latitude;
                existing.Longitude = incoming.Longitude;
                existing.LocationStatus = incoming.LocationStatus;
                existing.Attendance = incoming.Attendance;
                existing.Url = incoming.Url;
                existing.Tags = incoming.Tags;
                existing.DedupeKey = incoming.DedupeKey;
                existing.LastSeenUtc = now;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<Event>> QueryAsync(
        double lat,
        double lon,
        double radiusKm,
        DateTime? from,
        DateTime? to,
        IReadOnlyCollection<string>? tags,
        Attendance? attendance,
        CancellationToken ct)
    {
        var box = Geo.GetBoundingBox(lat, lon, radiusKm);

        var query = db.Events.AsNoTracking()
            .Where(e => e.LocationStatus == LocationStatus.Resolved)
            .Where(e => e.Latitude >= box.MinLat && e.Latitude <= box.MaxLat)
            .Where(e => e.Longitude >= box.MinLon && e.Longitude <= box.MaxLon);

        if (from is not null)
        {
            query = query.Where(e => e.StartUtc >= from);
        }
        if (to is not null)
        {
            query = query.Where(e => e.StartUtc <= to);
        }
        if (attendance is not null)
        {
            query = query.Where(e => e.Attendance == attendance);
        }

        // Bounding box is a SQL pre-filter; the exact circle needs the
        // haversine distance, which SQLite has no built-in function for.
        var candidates = await query.ToListAsync(ct);

        if (tags is { Count: > 0 })
        {
            candidates = [.. candidates.Where(e => e.Tags.Any(tags.Contains))];
        }

        return [.. candidates.Where(e => Geo.DistanceKm(lat, lon, e.Latitude!.Value, e.Longitude!.Value) <= radiusKm)];
    }

    // Events whose location the Gazetteer cascade could not resolve. Kept
    // (never dropped, per AGENTS.md) and surfaced here so the API host's
    // /api/events/unresolved can keep coverage gaps visible.
    public async Task<IReadOnlyList<Event>> GetUnresolvedAsync(CancellationToken ct) =>
        await db.Events.AsNoTracking()
            .Where(e => e.LocationStatus == LocationStatus.Unresolved)
            .OrderByDescending(e => e.LastSeenUtc)
            .ToListAsync(ct);

    // Workstream 2 (IngestionRunner) computes SourceStatus per run but has
    // nowhere durable to put it; this is that persistence, keyed by SourceId
    // (see EventFinderDbContext.OnModelCreating), so the API host's
    // /api/sources transparency view survives a restart.
    public async Task SaveSourceStatusesAsync(IReadOnlyDictionary<string, SourceStatus> statuses, CancellationToken ct)
    {
        foreach (var incoming in statuses.Values)
        {
            var existing = await db.SourceStatuses.SingleOrDefaultAsync(s => s.SourceId == incoming.SourceId, ct);
            if (existing is null)
            {
                db.SourceStatuses.Add(new SourceStatus
                {
                    SourceId = incoming.SourceId,
                    LastRunUtc = incoming.LastRunUtc,
                    LastSuccessUtc = incoming.LastSuccessUtc,
                    EventCount = incoming.EventCount,
                    LastError = incoming.LastError,
                });
            }
            else
            {
                existing.LastRunUtc = incoming.LastRunUtc;
                existing.LastSuccessUtc = incoming.LastSuccessUtc;
                existing.EventCount = incoming.EventCount;
                existing.LastError = incoming.LastError;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<SourceStatus>> GetSourceStatusesAsync(CancellationToken ct) =>
        await db.SourceStatuses.AsNoTracking().ToListAsync(ct);
}
