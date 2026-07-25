namespace EventFinder.Core;

public static class Dedupe
{
    // Two events are "the same" if they share a folded title, the same
    // resolved city, and land on the same calendar day *in the event's own
    // time zone* -- a 23:30 CEST meetup and its 21:30 UTC timestamp must not
    // be split across two different days just because UTC has already
    // crossed midnight.
    public static string ComputeKey(string title, DateTime startUtc, string timeZoneId, string? resolvedCity)
    {
        var localDate = ToLocalDate(startUtc, timeZoneId);
        var foldedTitle = Normalization.Fold(title);
        var foldedCity = resolvedCity is null ? string.Empty : Normalization.Fold(resolvedCity);
        return $"{foldedTitle}|{localDate:yyyy-MM-dd}|{foldedCity}";
    }

    private static DateOnly ToLocalDate(DateTime startUtc, string timeZoneId)
    {
        var utc = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc);
        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // An adapter fed us a bogus zone id; fall back to UTC rather than
            // let dedupe (a pure function) throw over bad source data.
            zone = TimeZoneInfo.Utc;
        }

        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
        return DateOnly.FromDateTime(local);
    }
}
