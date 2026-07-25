namespace EventFinder.Ingestion.Http;

public sealed record PoliteFetchResult(string Body, bool NotModified);

// The one HTTP entry point every adapter goes through, so politeness
// (per-host delay, bounded retries, timeout, User-Agent) and conditional
// caching live in exactly one place instead of being reimplemented per
// adapter.
public interface IPoliteHttpClient
{
    // Conditional GET keyed by sourceId: sends If-None-Match/If-Modified-Since
    // from the cache, and on a 304 hands back the cached body instead of an
    // empty one so "no change" really does mean "keep last-good data".
    Task<PoliteFetchResult> GetAsync(string sourceId, string url, CancellationToken ct);

    // Unconditional GET -- pagination continuation pages and robots.txt have
    // nothing to condition on.
    Task<string> GetRawAsync(string url, CancellationToken ct);
}
