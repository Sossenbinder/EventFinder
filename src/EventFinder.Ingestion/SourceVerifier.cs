using EventFinder.Ingestion.Contracts;
using EventFinder.Ingestion.Http;

namespace EventFinder.Ingestion;

public sealed record SourceVerificationResult(
    string SourceId, bool Reachable, int? HttpStatus, int EventCount, string? FirstParseError);

// Backs the `sources verify` CLI verb (wired up by workstream 3): the gate
// that keeps unverified URLs out of sources.yaml. Reuses the same
// IEventSource adapters a real run would use, so "verify" and "run" can
// never disagree about whether a source works.
public sealed class SourceVerifier(IReadOnlyDictionary<string, IEventSource> sourcesByType)
{
    public async Task<IReadOnlyList<SourceVerificationResult>> VerifyAsync(
        IEnumerable<SourceDescriptor> sources, CancellationToken ct)
    {
        var results = new List<SourceVerificationResult>();
        foreach (var source in sources)
        {
            results.Add(await VerifyOneAsync(source, ct));
        }
        return results;
    }

    private async Task<SourceVerificationResult> VerifyOneAsync(SourceDescriptor source, CancellationToken ct)
    {
        if (!sourcesByType.TryGetValue(source.Type, out var adapter))
        {
            return new SourceVerificationResult(source.Id, Reachable: false, HttpStatus: null, EventCount: 0,
                FirstParseError: $"No IEventSource registered for type '{source.Type}'.");
        }

        try
        {
            var events = await adapter.FetchAsync(source, ct);
            // A non-exception return means the underlying request succeeded;
            // the adapter contract does not surface the exact status code,
            // so 200 stands in for "some success status".
            return new SourceVerificationResult(source.Id, Reachable: true, HttpStatus: 200, events.Count, FirstParseError: null);
        }
        catch (SourceUnreachableException ex)
        {
            return new SourceVerificationResult(source.Id, Reachable: false, HttpStatus: null, EventCount: 0, ex.Message);
        }
        catch (SourceHttpErrorException ex)
        {
            return new SourceVerificationResult(source.Id, Reachable: true, (int)ex.StatusCode, EventCount: 0, FirstParseError: null);
        }
        catch (RobotsDisallowedException ex)
        {
            return new SourceVerificationResult(source.Id, Reachable: true, HttpStatus: null, EventCount: 0, ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Reached the server (or got far enough to attempt a parse) but
            // could not make sense of the response.
            return new SourceVerificationResult(source.Id, Reachable: true, HttpStatus: null, EventCount: 0, ex.Message);
        }
    }
}
