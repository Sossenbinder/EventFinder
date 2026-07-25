namespace EventFinder.Ingestion.Contracts;

// Deserialized 1:1 from an entry in sources.yaml by SourceRegistry. Field
// names are camelCase in the YAML (id, org, type, url, adapter, region,
// tags, enabled) via YamlDotNet's camelCase naming convention.
public sealed record SourceDescriptor
{
    public required string Id { get; init; }
    public required string Org { get; init; }

    // "ics" | "bevy" | "html" -- validated by SourceRegistry, not an enum,
    // because the adapter set is looked up by string key (IEventSource.Type)
    // and a new adapter type should not require a Core-level enum change.
    public required string Type { get; init; }

    public required string Url { get; init; }

    // Parser key for HtmlSource's DI dispatch. Required (and validated
    // against the registered parsers) when Type is "html"; ignored otherwise.
    public string? Adapter { get; init; }

    public string? Region { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public bool Enabled { get; init; } = true;

    // Chapter allow-list for "gdg-sitemap": event URLs are only kept when
    // they contain "-<slug>-presents-" for one of these. Ignored by other
    // adapter types.
    public IReadOnlyList<string> Slugs { get; init; } = [];
}
