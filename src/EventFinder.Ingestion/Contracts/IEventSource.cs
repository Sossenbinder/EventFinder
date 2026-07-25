namespace EventFinder.Ingestion.Contracts;

// One implementation per SourceDescriptor.Type ("ics", "bevy", "html").
// FetchAsync owns its own HTTP concerns (conditional headers, pagination,
// robots.txt for html) -- IngestionRunner only orchestrates the pipeline
// after RawEvents come back, plus the per-source failure isolation around
// this call.
public interface IEventSource
{
    string Type { get; }

    Task<IReadOnlyList<RawEvent>> FetchAsync(SourceDescriptor source, CancellationToken ct);
}
