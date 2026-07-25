using System.Text.Json;

namespace EventFinder.Ingestion.Http;

// What a conditional GET needs to remember between runs: the validators to
// send back (ETag / Last-Modified) and the last successfully fetched body,
// so a 304 response can be turned back into the same content the adapter
// parsed last time instead of an empty result.
public sealed record CachedFetch(string? ETag, string? LastModified, string Body, DateTime FetchedAtUtc);

public interface IConditionalFetchCache
{
    Task<CachedFetch?> GetAsync(string sourceId, CancellationToken ct);

    Task SaveAsync(string sourceId, CachedFetch entry, CancellationToken ct);
}

// Persists one JSON file per source under a cache directory. Deliberately
// file-backed rather than a new EventFinder.Data table: this is HTTP
// plumbing local to ingestion, not domain data, and keeping it here avoids
// touching workstream 1's schema/migrations for a concern that never needs
// to be queried by the API.
public sealed class FileConditionalFetchCache(string cacheDirectory) : IConditionalFetchCache
{
    public async Task<CachedFetch?> GetAsync(string sourceId, CancellationToken ct)
    {
        var path = PathFor(sourceId);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<CachedFetch>(stream, cancellationToken: ct);
        }
        catch (JsonException)
        {
            // A corrupted cache entry must not fail the fetch; treat it as
            // "nothing cached" and let the caller do an unconditional GET.
            return null;
        }
    }

    public async Task SaveAsync(string sourceId, CachedFetch entry, CancellationToken ct)
    {
        Directory.CreateDirectory(cacheDirectory);
        var path = PathFor(sourceId);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, entry, cancellationToken: ct);
    }

    private string PathFor(string sourceId) => Path.Combine(cacheDirectory, $"{Uri.EscapeDataString(sourceId)}.json");
}
