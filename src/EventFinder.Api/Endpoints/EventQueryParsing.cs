using EventFinder.Core;

namespace EventFinder.Api.Endpoints;

// Shared by /api/events and /api/events.ics (outline: "the SAME filter
// parameters"), so the two endpoints can never validate or interpret a
// query differently.
public sealed record EventQuery(
    double Lat,
    double Lon,
    double RadiusKm,
    DateTime? From,
    DateTime? To,
    IReadOnlyCollection<string> Tags,
    Attendance? Attendance,
    string? Search,
    int Limit,
    int Offset);

public static class EventQueryParsing
{
    public const double MaxRadiusKm = 500;
    public const int MaxLimit = 500;

    public static bool TryParse(
        double lat,
        double lon,
        double radiusKm,
        DateTime? from,
        DateTime? to,
        string[]? tags,
        string? attendance,
        string? search,
        int limit,
        int offset,
        out EventQuery query,
        out IResult? problem)
    {
        var errors = new Dictionary<string, string[]>();

        if (lat is < -90 or > 90)
        {
            errors["lat"] = ["lat must be between -90 and 90."];
        }
        if (lon is < -180 or > 180)
        {
            errors["lon"] = ["lon must be between -180 and 180."];
        }
        if (radiusKm <= 0 || radiusKm > MaxRadiusKm)
        {
            errors["radiusKm"] = [$"radiusKm must be greater than 0 and at most {MaxRadiusKm}."];
        }
        if (from is not null && to is not null && from > to)
        {
            errors["to"] = ["to must not be before from."];
        }
        if (limit <= 0 || limit > MaxLimit)
        {
            errors["limit"] = [$"limit must be between 1 and {MaxLimit}."];
        }
        if (offset < 0)
        {
            errors["offset"] = ["offset must not be negative."];
        }

        Attendance? parsedAttendance = null;
        if (!string.IsNullOrWhiteSpace(attendance))
        {
            if (Enum.TryParse<Attendance>(attendance, ignoreCase: true, out var parsed))
            {
                parsedAttendance = parsed;
            }
            else
            {
                errors["attendance"] = [$"attendance must be one of: {string.Join(", ", Enum.GetNames<Attendance>())}."];
            }
        }

        if (errors.Count > 0)
        {
            query = null!;
            problem = Results.ValidationProblem(errors);
            return false;
        }

        // Query-string DateTimes without a "Z"/offset parse as Kind=Unspecified;
        // treating them as UTC here (rather than letting EventFinderDbContext's
        // UtcDateTimeConverter call ToUniversalTime(), which would reinterpret
        // them as the server's local time) is the only sane default for a
        // public API with no concept of the caller's timezone.
        query = new EventQuery(
            lat, lon, radiusKm,
            from is null ? null : DateTime.SpecifyKind(from.Value, DateTimeKind.Utc),
            to is null ? null : DateTime.SpecifyKind(to.Value, DateTimeKind.Utc),
            tags ?? [],
            parsedAttendance,
            string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
            limit,
            offset);
        problem = null;
        return true;
    }
}
