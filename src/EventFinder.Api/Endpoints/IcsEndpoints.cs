using System.Globalization;
using EventFinder.Api.Ics;
using EventFinder.Data;

namespace EventFinder.Api.Endpoints;

// GET /api/events.ics -- the same filter as /api/events, rendered as an
// iCalendar feed so a user can subscribe to their radius in a calendar app.
public static class IcsEndpoints
{
    public static IEndpointRouteBuilder MapIcsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/events.ics", async (
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
            var ordered = events.OrderBy(e => e.StartUtc).Skip(query.Offset).Take(query.Limit).ToList();

            var calendarName = string.Create(
                CultureInfo.InvariantCulture,
                $"EventFinder — {query.RadiusKm:0.#} km around {query.Lat:0.###},{query.Lon:0.###}");
            var ics = IcsFeedBuilder.Build(ordered, calendarName);

            return Results.Text(ics, "text/calendar; charset=utf-8");
        })
        .WithName("GetEventsIcs");

        return app;
    }
}
