using EventFinder.Core;
using EventFinder.Data;
using EventFinder.Ingestion.Contracts;

namespace EventFinder.Ingestion;

// Orchestrates one ingestion pass over the registry: fetch -> Normalization
// -> Gazetteer resolve -> Dedupe.ComputeKey -> EventStore.UpsertAsync, with
// per-source isolation around the whole pipeline. Returns the run's
// SourceStatus per source; persisting those beyond this process (e.g. to
// SQLite for the /sources page) is the API host's job, not this runner's --
// EventStore currently exposes no SourceStatus persistence to call into.
public sealed class IngestionRunner(
    IReadOnlyDictionary<string, IEventSource> sourcesByType,
    EventStore store,
    Gazetteer gazetteer,
    IReadOnlyDictionary<string, string>? keywordToTag = null,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly IReadOnlyDictionary<string, string> _keywordToTag = keywordToTag ?? new Dictionary<string, string>();

    public async Task<IReadOnlyDictionary<string, SourceStatus>> RunAsync(
        IEnumerable<SourceDescriptor> sources, CancellationToken ct)
    {
        var statuses = new Dictionary<string, SourceStatus>(StringComparer.Ordinal);

        foreach (var source in sources.Where(s => s.Enabled))
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var status = new SourceStatus { SourceId = source.Id, LastRunUtc = now };
            statuses[source.Id] = status;

            try
            {
                if (!sourcesByType.TryGetValue(source.Type, out var adapter))
                {
                    throw new InvalidOperationException($"No IEventSource registered for type '{source.Type}'.");
                }

                var rawEvents = await adapter.FetchAsync(source, ct);
                var events = rawEvents.Select(raw => ToEvent(raw, source, now)).ToList();
                await store.UpsertAsync(events, source.Id, ct);

                status.LastSuccessUtc = now;
                status.EventCount = events.Count;
                status.LastError = null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Per-source isolation (outline Key Decisions / AGENTS.md):
                // one broken adapter must never fail the run or empty the
                // store. Whatever this source upserted on a previous
                // successful run is left exactly as it was.
                status.LastError = ex.Message;
            }
        }

        return statuses;
    }

    private Event ToEvent(RawEvent raw, SourceDescriptor source, DateTime nowUtc)
    {
        var title = Normalization.CleanTitle(raw.Title);
        var description = raw.Description is null ? null : Normalization.CleanDescription(raw.Description);
        var timeZoneId = string.IsNullOrEmpty(raw.TimeZoneId) ? "UTC" : raw.TimeZoneId;
        var startUtc = raw.Start.UtcDateTime;
        var endUtc = raw.End?.UtcDateTime;

        var geo = gazetteer.Resolve(raw.Latitude, raw.Longitude, raw.PostalCode, raw.VenueAddress, raw.City);
        var resolvedCity = geo.MatchedPlace ?? raw.City;

        var titleKeywordTags = Normalization.ExtractTags(title, description, _keywordToTag);
        var tags = source.Tags
            .Concat(titleKeywordTags)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToArray();

        return new Event
        {
            SourceId = source.Id,
            SourceEventId = raw.SourceEventId,
            Title = title,
            Description = description,
            StartUtc = startUtc,
            EndUtc = endUtc,
            TimeZoneId = timeZoneId,
            VenueName = raw.VenueName,
            VenueAddress = raw.VenueAddress,
            City = resolvedCity,
            PostalCode = raw.PostalCode,
            Latitude = geo.Latitude,
            Longitude = geo.Longitude,
            LocationStatus = geo.Status,
            // ICS/HTML rarely signal attendance mode; in-person is the
            // common case for the meetup/user-group sources this project
            // targets, so it is the default absent a real hint.
            Attendance = raw.AttendanceHint ?? Attendance.InPerson,
            Url = raw.Url,
            Tags = tags,
            FirstSeenUtc = nowUtc,
            LastSeenUtc = nowUtc,
            DedupeKey = Dedupe.ComputeKey(title, startUtc, timeZoneId, resolvedCity),
        };
    }
}
