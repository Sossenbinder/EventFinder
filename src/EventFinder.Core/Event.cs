using System.Diagnostics.CodeAnalysis;

namespace EventFinder.Core;

public enum LocationStatus
{
    Resolved,
    Unresolved,
}

// How precisely LocationStatus.Resolved was pinned down. Lets the UI and the
// /sources page distinguish a real venue position from a town centroid --
// the whole point of address-level geocoding (see IngestionRunner's cascade).
public enum LocationPrecision
{
    Address,
    Street,
    City,
    None,
}

public enum Attendance
{
    InPerson,
    Online,
    Hybrid,
}

// "Event" is the domain name the outline specifies; CA1716 flags it as a
// keyword in other CLS languages (VB's Event), which doesn't apply here.
[SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "Domain name from the spec.")]
public sealed class Event
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string SourceId { get; init; }
    public required string SourceEventId { get; init; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public required DateTime StartUtc { get; set; }
    public DateTime? EndUtc { get; set; }
    public required string TimeZoneId { get; set; }
    public string? VenueName { get; set; }
    public string? VenueAddress { get; set; }
    public string? City { get; set; }
    public string? PostalCode { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public LocationStatus LocationStatus { get; set; }
    public LocationPrecision LocationPrecision { get; set; }
    public Attendance Attendance { get; set; }
    public required string Url { get; set; }
    public IReadOnlyList<string> Tags { get; set; } = [];
    public required DateTime FirstSeenUtc { get; init; }
    public DateTime LastSeenUtc { get; set; }
    public required string DedupeKey { get; set; }
}
