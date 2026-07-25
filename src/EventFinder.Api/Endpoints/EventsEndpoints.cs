using EventFinder.Core;
using EventFinder.Data;

namespace EventFinder.Api.Endpoints;

public sealed record EventDto(
    Guid Id,
    string SourceId,
    string Title,
    string? Description,
    DateTime StartUtc,
    DateTime? EndUtc,
    string TimeZoneId,
    string? VenueName,
    string? VenueAddress,
    string? City,
    string? PostalCode,
    double Latitude,
    double Longitude,
    double DistanceKm,
    Attendance Attendance,
    string Url,
    IReadOnlyList<string> Tags);

public sealed record EventsResponse(IReadOnlyList<EventDto> Events, int TotalCount);

public sealed record UnresolvedEventDto(
    Guid Id,
    string SourceId,
    string Title,
    DateTime StartUtc,
    string? VenueName,
    string? VenueAddress,
    string? City,
    string? PostalCode,
    Attendance Attendance,
    string Url);

// GET /api/events and GET /api/events/unresolved.
public static class EventsEndpoints
{
    public static IEndpointRouteBuilder MapEventsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/events", async (
            double lat, double lon, double radiusKm, DateTime? from, DateTime? to,
            string[]? tags, string? attendance, EventStore store, CancellationToken ct,
            int limit = 100, int offset = 0) =>
        {
            if (!EventQueryParsing.TryParse(lat, lon, radiusKm, from, to, tags, attendance, limit, offset, out var query, out var problem))
            {
                return problem!;
            }

            var events = await store.QueryAsync(
                query.Lat, query.Lon, query.RadiusKm, query.From, query.To, query.Tags, query.Attendance, ct);

            var ordered = events.OrderBy(e => e.StartUtc).ToList();
            var page = ordered
                .Skip(query.Offset)
                .Take(query.Limit)
                .Select(e => ToDto(e, query.Lat, query.Lon))
                .ToList();

            return Results.Ok(new EventsResponse(page, ordered.Count));
        })
        .WithName("GetEvents");

        app.MapGet("/api/events/unresolved", async (EventStore store, CancellationToken ct) =>
        {
            var events = await store.GetUnresolvedAsync(ct);
            return Results.Ok(events.Select(ToUnresolvedDto).ToList());
        })
        .WithName("GetUnresolvedEvents");

        return app;
    }

    private static EventDto ToDto(Event e, double centerLat, double centerLon) => new(
        e.Id,
        e.SourceId,
        e.Title,
        e.Description,
        e.StartUtc,
        e.EndUtc,
        e.TimeZoneId,
        e.VenueName,
        e.VenueAddress,
        e.City,
        e.PostalCode,
        e.Latitude!.Value,
        e.Longitude!.Value,
        Geo.DistanceKm(centerLat, centerLon, e.Latitude!.Value, e.Longitude!.Value),
        e.Attendance,
        e.Url,
        e.Tags);

    private static UnresolvedEventDto ToUnresolvedDto(Event e) => new(
        e.Id, e.SourceId, e.Title, e.StartUtc, e.VenueName, e.VenueAddress, e.City, e.PostalCode, e.Attendance, e.Url);
}
