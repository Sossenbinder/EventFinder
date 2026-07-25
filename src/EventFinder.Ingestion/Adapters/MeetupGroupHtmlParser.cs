using System.Globalization;
using System.Text.Json;
using AngleSharp.Dom;
using EventFinder.Core;
using EventFinder.Ingestion.Contracts;

namespace EventFinder.Ingestion.Adapters;

// Parses a Meetup group's public /events/ page (curated per-group only, per
// AGENTS.md -- no platform-wide crawling). Verified live 2026-07-26: the page
// embeds a <script id="__NEXT_DATA__" type="application/json"> whose
// props.pageProps.__APOLLO_STATE__ is a flat map of normalized Apollo cache
// entities. "Event:<id>" entries carry the event fields directly; venue
// details live in a separate "Venue:<id>" entry, reached via the event's
// venue.__ref indirection.
public sealed class MeetupGroupHtmlParser(TimeProvider? timeProvider = null) : IHtmlEventParser
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public string AdapterKey => "meetup-group";

    public IReadOnlyList<RawEvent> Parse(IDocument document, SourceDescriptor source)
    {
        var script = document.QuerySelector("script#__NEXT_DATA__[type='application/json']");
        if (script is null)
        {
            throw new InvalidOperationException(
                $"Meetup source '{source.Id}': no __NEXT_DATA__ script found on '{source.Url}'.");
        }

        using var nextData = JsonDocument.Parse(script.TextContent);
        if (!TryGetApolloState(nextData.RootElement, out var apolloState))
        {
            throw new InvalidOperationException(
                $"Meetup source '{source.Id}': __NEXT_DATA__ has no props.pageProps.__APOLLO_STATE__.");
        }

        var nowUtc = _timeProvider.GetUtcNow();
        var rawEvents = new List<RawEvent>();

        foreach (var property in apolloState.EnumerateObject())
        {
            if (!property.Name.StartsWith("Event:", StringComparison.Ordinal))
            {
                continue;
            }

            var eventObj = property.Value;
            var start = GetDateTimeOffset(eventObj, "dateTime");
            if (start is null)
            {
                continue; // no usable start -- nothing to key an occurrence on
            }

            var end = GetDateTimeOffset(eventObj, "endTime");
            if ((end ?? start.Value) < nowUtc)
            {
                continue; // already past; a zero-length result here is a legitimate, successful parse
            }

            var (venueName, venueAddress, city) = ResolveVenue(apolloState, eventObj);

            rawEvents.Add(new RawEvent
            {
                // The numeric id in the "Event:<id>" key, per the outline.
                SourceEventId = property.Name["Event:".Length..],
                Title = GetString(eventObj, "title") ?? string.Empty,
                Description = GetString(eventObj, "description"),
                Start = start.Value,
                End = end,
                VenueName = venueName,
                VenueAddress = venueAddress,
                City = city,
                Url = GetString(eventObj, "eventUrl") ?? source.Url,
                AttendanceHint = MapAttendance(GetString(eventObj, "eventType")),
            });
        }

        return rawEvents;
    }

    private static bool TryGetApolloState(JsonElement nextDataRoot, out JsonElement apolloState)
    {
        apolloState = default;
        return nextDataRoot.TryGetProperty("props", out var props)
            && props.TryGetProperty("pageProps", out var pageProps)
            && pageProps.TryGetProperty("__APOLLO_STATE__", out apolloState)
            && apolloState.ValueKind == JsonValueKind.Object;
    }

    private static (string? VenueName, string? Address, string? City) ResolveVenue(JsonElement apolloState, JsonElement eventObj)
    {
        if (!eventObj.TryGetProperty("venue", out var venueRef)
            || venueRef.ValueKind != JsonValueKind.Object
            || !venueRef.TryGetProperty("__ref", out var refProp)
            || refProp.ValueKind != JsonValueKind.String)
        {
            return (null, null, null); // online events commonly have no venue at all
        }

        var venueKey = refProp.GetString();
        if (venueKey is null || !apolloState.TryGetProperty(venueKey, out var venueObj))
        {
            return (null, null, null);
        }

        return (GetString(venueObj, "name"), GetString(venueObj, "address"), GetString(venueObj, "city"));
    }

    private static Attendance? MapAttendance(string? eventType) => eventType switch
    {
        "PHYSICAL" => Attendance.InPerson,
        "ONLINE" => Attendance.Online,
        "HYBRID" => Attendance.Hybrid,
        _ => null,
    };

    private static string? GetString(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    private static DateTimeOffset? GetDateTimeOffset(JsonElement obj, string name) =>
        GetString(obj, name) is { } s && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var v)
            ? v
            : null;
}
