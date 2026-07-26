using Microsoft.EntityFrameworkCore;

namespace EventFinder.Data;

// Read/write access to GeocodeCacheEntry, kept separate from EventStore
// because it is a different concern (address -> coordinate memoization, not
// event upsert) with a different caller (EventFinder.Ingestion's address
// geocoder rather than the ingestion pipeline itself).
public sealed class GeocodeCacheStore(EventFinderDbContext db)
{
    public async Task<GeocodeCacheEntry?> FindAsync(string normalizedQuery, CancellationToken ct) =>
        await db.GeocodeCacheEntries.AsNoTracking().SingleOrDefaultAsync(e => e.Query == normalizedQuery, ct);

    public async Task SaveAsync(GeocodeCacheEntry entry, CancellationToken ct)
    {
        var existing = await db.GeocodeCacheEntries.SingleOrDefaultAsync(e => e.Query == entry.Query, ct);
        if (existing is null)
        {
            db.GeocodeCacheEntries.Add(entry);
        }
        else
        {
            existing.Found = entry.Found;
            existing.Latitude = entry.Latitude;
            existing.Longitude = entry.Longitude;
            existing.Precision = entry.Precision;
            existing.ResolvedAtUtc = entry.ResolvedAtUtc;
        }

        await db.SaveChangesAsync(ct);
    }
}
