using System.Security.Cryptography;
using System.Text;
using EventFinder.Core;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;

namespace EventFinder.Api.Ics;

// Renders a filtered event set as a subscribable iCalendar feed (outline:
// "subscribe to your filtered radius as a calendar feed"). UIDs are derived
// from Event.DedupeKey rather than Event.Id, so the same logical occurrence
// keeps the same UID across a re-ingest that assigns it a new database row.
public static class IcsFeedBuilder
{
    public static string Build(IReadOnlyList<Event> events, string calendarName)
    {
        var calendar = new Calendar();
        calendar.AddProperty("X-WR-CALNAME", calendarName);
        calendar.AddProperty("METHOD", "PUBLISH");

        foreach (var evt in events)
        {
            var calendarEvent = new CalendarEvent
            {
                Uid = DeriveUid(evt.DedupeKey),
                Summary = evt.Title,
                Description = evt.Description,
                Start = new CalDateTime(evt.StartUtc),
                Location = evt.VenueName ?? evt.City,
                Url = Uri.TryCreate(evt.Url, UriKind.Absolute, out var url) ? url : null,
            };
            if (evt.EndUtc is { } endUtc)
            {
                calendarEvent.End = new CalDateTime(endUtc);
            }
            calendar.Events.Add(calendarEvent);
        }

        return new CalendarSerializer().SerializeToString(calendar) ?? string.Empty;
    }

    private static string DeriveUid(string dedupeKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(dedupeKey));
        return $"{Convert.ToHexString(hash)}@eventfinder";
    }
}
