using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Evaluation;
using EventFinder.Ingestion.Contracts;
using EventFinder.Ingestion.Http;

namespace EventFinder.Ingestion.Adapters;

// Generic ICS/iCal feed adapter. Recurring VEVENTs are expanded into
// concrete occurrences via Ical.Net's own RRULE evaluator, which also
// resolves VTIMEZONE blocks -- CalDateTime.TzId/AsUtc below reflect that
// resolution, not a re-implementation of it.
public sealed class IcsSource(
    IPoliteHttpClient httpClient, IRobotsTxtCache robotsCache, TimeProvider? timeProvider = null) : IEventSource
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public string Type => "ics";

    public async Task<IReadOnlyList<RawEvent>> FetchAsync(SourceDescriptor source, CancellationToken ct)
    {
        // Politeness applies to every adapter type, not just HTML -- robots.txt
        // governs the request, not what the response is parsed into.
        var url = new Uri(source.Url);
        if (!await robotsCache.IsAllowedAsync(url, ct))
        {
            throw new RobotsDisallowedException(source.Url);
        }

        var fetch = await httpClient.GetAsync(source.Id, source.Url, ct);

        Calendar? calendar;
        try
        {
            calendar = Calendar.Load(fetch.Body);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException($"ICS source '{source.Id}' could not be parsed: {ex.Message}", ex);
        }

        if (calendar is null)
        {
            throw new InvalidOperationException($"ICS source '{source.Id}' returned an empty/unparsable calendar.");
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var windowStart = new CalDateTime(nowUtc);
        var windowEndUtc = nowUtc.AddMonths(12);

        var rawEvents = new List<RawEvent>();
        foreach (var evt in calendar.Events)
        {
            if (string.IsNullOrEmpty(evt.Uid))
            {
                continue; // no stable identity to key SourceEventId on; skip rather than abort the feed
            }

            // GetOccurrences(windowStart, ...) already excludes occurrences that
            // ended before windowStart (an event overlapping windowStart, e.g.
            // one already in progress, is still included) -- this is exactly
            // the outline's "skip events that already ended" rule, so no
            // separate end-date filter is needed here.
            var occurrences = evt.GetOccurrences(windowStart, new EvaluationOptions())
                .TakeWhile(o => o.Period.StartTime.AsUtc < windowEndUtc);

            foreach (var occurrence in occurrences)
            {
                rawEvents.Add(Map(evt, occurrence, source));
            }
        }

        return rawEvents;
    }

    private static RawEvent Map(CalendarEvent evt, Occurrence occurrence, SourceDescriptor source)
    {
        var start = occurrence.Period.StartTime;
        var end = occurrence.Period.EffectiveEndTime;
        var startUtc = start.AsUtc;

        return new RawEvent
        {
            // UID alone collides across a recurring series' occurrences; the
            // occurrence's own start makes each one unique the same way a
            // RECURRENCE-ID would.
            SourceEventId = $"{evt.Uid}#{startUtc:yyyyMMddTHHmmssZ}",
            Title = evt.Summary ?? string.Empty,
            Description = evt.Description,
            Start = new DateTimeOffset(startUtc, TimeSpan.Zero),
            End = end is null ? null : new DateTimeOffset(end.AsUtc, TimeSpan.Zero),
            TimeZoneId = string.IsNullOrEmpty(start.TzId) ? null : start.TzId,
            VenueAddress = evt.Location,
            Latitude = evt.GeographicLocation?.Latitude,
            Longitude = evt.GeographicLocation?.Longitude,
            // Most ICS feeds omit a per-event URL; falling back to the feed's
            // own URL beats leaving the required field empty.
            Url = evt.Url?.ToString() ?? source.Url,
        };
    }
}
