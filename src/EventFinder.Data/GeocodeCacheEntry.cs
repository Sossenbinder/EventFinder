using EventFinder.Core;

namespace EventFinder.Data;

// Permanent cache of address -> coordinate lookups (currently only Photon's),
// keyed by a normalized query string so the same venue address is never sent
// over the network twice. Found=false is the negative-result marker: a
// query Photon could not answer is remembered too, so a bad batch does not
// retry the same dead address on every ingestion run.
public sealed class GeocodeCacheEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Query { get; init; }
    public bool Found { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public LocationPrecision Precision { get; set; }
    public required DateTime ResolvedAtUtc { get; set; }
}
