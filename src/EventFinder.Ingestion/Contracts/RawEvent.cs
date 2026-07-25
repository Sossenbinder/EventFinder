using EventFinder.Core;

namespace EventFinder.Ingestion.Contracts;

// What an adapter hands back before Normalization/Gazetteer/Dedupe touch it.
// Deliberately looser than Core.Event: no Id, no dedupe key, no FirstSeen/
// LastSeen bookkeeping -- those are IngestionRunner's job, not the adapter's.
public sealed record RawEvent
{
    public required string SourceEventId { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }

    // DateTimeOffset always carries an offset, satisfying the "with offset"
    // half of the contract; TimeZoneId below carries the IANA id when the
    // source actually names one (Bevy's event_timezone, an ICS VTIMEZONE).
    public required DateTimeOffset Start { get; init; }
    public DateTimeOffset? End { get; init; }
    public string? TimeZoneId { get; init; }

    public string? VenueName { get; init; }
    public string? VenueAddress { get; init; }
    public string? City { get; init; }
    public string? PostalCode { get; init; }

    // Nullable: only Bevy's _geoloc and ICS's optional GEO property supply
    // these directly. Gazetteer.Resolve fills the gap for everything else.
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }

    public required string Url { get; init; }

    // Null when the source gives no signal; IngestionRunner defaults it.
    public Attendance? AttendanceHint { get; init; }
}
