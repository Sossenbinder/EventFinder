using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using AngleSharp;
using EventFinder.Core;
using EventFinder.Ingestion.Contracts;
using EventFinder.Ingestion.Http;

namespace EventFinder.Ingestion.Adapters;

// Replaces the old Bevy JSON-API adapter (see AGENTS.md): gdg.community.dev's
// robots.txt disallows /api/, which is exactly the path that adapter used.
// This adapter only ever touches /sitemap*.xml and /events/details/*, both of
// which robots.txt allows.
//
// Shape, verified live 2026-07-26: /sitemap.xml is a sitemapindex containing
// (among ~196 entries) one sitemap-events-YYYY-MM.xml per month, several
// years deep in both directions. Each event's detail page carries exactly one
// schema.org Event <script type="application/ld+json">. Coordinates are never
// present on these pages -- Gazetteer resolution downstream is the only way
// these events get a lat/lon.
public sealed class GdgSitemapSource(
    IPoliteHttpClient httpClient, IRobotsTxtCache robotsCache, TimeProvider? timeProvider = null) : IEventSource
{
    public string Type => "gdg-sitemap";

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    private static readonly System.Text.RegularExpressions.Regex EventsSitemapPattern =
        new(@"sitemap-events-(\d{4})-(\d{2})\.xml", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly XNamespace SitemapNs = "http://www.sitemaps.org/schemas/sitemap/0.9";

    public async Task<IReadOnlyList<RawEvent>> FetchAsync(SourceDescriptor source, CancellationToken ct)
    {
        var indexUrl = new Uri(source.Url);
        if (!await robotsCache.IsAllowedAsync(indexUrl, ct))
        {
            throw new RobotsDisallowedException(source.Url);
        }

        // Fetched once per run and reused for every subsequent request to
        // this host below -- gdg.community.dev's robots.txt sets
        // "Crawl-delay: 2", which this adapter must honour across the whole
        // sequence of sitemap + event-detail fetches, not just the first one.
        var rules = await robotsCache.GetRulesAsync(indexUrl, ct);
        var requestCount = 0;

        async Task<string> ThrottledGetRawAsync(string url)
        {
            await ThrottleAsync(rules.CrawlDelay, requestCount, ct);
            requestCount++;
            return await httpClient.GetRawAsync(url, ct);
        }

        async Task<PoliteFetchResult> ThrottledGetAsync(string cacheKey, string url)
        {
            await ThrottleAsync(rules.CrawlDelay, requestCount, ct);
            requestCount++;
            return await httpClient.GetAsync(cacheKey, url, ct);
        }

        var indexBody = await ThrottledGetRawAsync(source.Url);
        var monthlySitemapUrls = SelectMonthlySitemapUrls(indexBody, _timeProvider.GetUtcNow());

        var eventUrls = new List<string>();
        foreach (var sitemapUrl in monthlySitemapUrls)
        {
            var monthKey = EventsSitemapPattern.Match(sitemapUrl).Value;
            var fetch = await ThrottledGetAsync($"{source.Id}:sitemap:{monthKey}", sitemapUrl);
            eventUrls.AddRange(ExtractLocs(fetch.Body));
        }

        var slugs = source.Slugs;
        var matchingUrls = eventUrls
            .Where(url => slugs.Any(slug => url.Contains($"-{slug}-presents-", StringComparison.Ordinal)))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var nowUtc = _timeProvider.GetUtcNow();
        var rawEvents = new List<RawEvent>();

        foreach (var eventUrl in matchingUrls)
        {
            var eventUri = new Uri(eventUrl);
            if (!await robotsCache.IsAllowedAsync(eventUri, ct))
            {
                continue; // stay safe even though /events/details/ is not currently disallowed
            }

            var slug = ExtractSlug(eventUrl);
            var fetch = await ThrottledGetAsync($"{source.Id}:event:{slug}", eventUrl);

            var jsonLd = await ExtractJsonLdAsync(fetch.Body, ct);
            if (jsonLd is null)
            {
                continue; // page without the expected structured data; skip rather than fail the run
            }

            var info = ParseEventJsonLd(jsonLd);
            if (info is null)
            {
                continue;
            }

            var referenceEnd = info.Value.End ?? info.Value.Start;
            if (referenceEnd < nowUtc)
            {
                continue; // already ended
            }

            rawEvents.Add(Map(info.Value, eventUrl, slug));
        }

        return rawEvents;
    }

    private static async Task ThrottleAsync(TimeSpan? crawlDelay, int requestsSoFar, CancellationToken ct)
    {
        if (requestsSoFar > 0 && crawlDelay is { } delay && delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, ct);
        }
    }

    // Window: the current month through +12 months. Sub-sitemaps for months
    // outside that window (this host keeps them back to 2015) are ignored.
    private static List<string> SelectMonthlySitemapUrls(string indexXml, DateTimeOffset nowUtc)
    {
        var startKey = (nowUtc.Year * 12) + (nowUtc.Month - 1);
        var endKey = startKey + 12;

        var selected = new List<(int Key, string Url)>();
        foreach (var loc in ExtractLocs(indexXml))
        {
            var match = EventsSitemapPattern.Match(loc);
            if (!match.Success)
            {
                continue;
            }

            var year = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var month = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            var key = (year * 12) + (month - 1);
            if (key >= startKey && key <= endKey)
            {
                selected.Add((key, loc));
            }
        }

        return [.. selected.OrderBy(s => s.Key).Select(s => s.Url)];
    }

    private static IEnumerable<string> ExtractLocs(string xml)
    {
        var doc = XDocument.Parse(xml);
        var ns = doc.Root?.GetDefaultNamespace() ?? SitemapNs;
        return doc.Descendants(ns + "loc").Select(e => e.Value.Trim());
    }

    private static string ExtractSlug(string eventUrl) =>
        new Uri(eventUrl).AbsolutePath.Trim('/').Split('/').Last();

    private static async Task<string?> ExtractJsonLdAsync(string html, CancellationToken ct)
    {
        var context = BrowsingContext.New(Configuration.Default);
        using var document = await context.OpenAsync(req => req.Content(html), ct);
        var script = document.QuerySelector("script[type='application/ld+json']");
        return script?.TextContent;
    }

    // What Map() needs out of the JSON-LD, extracted while the JsonDocument
    // backing the source JsonElements is still alive. `location` on these
    // pages is sometimes a single Place object and sometimes an array mixing
    // a VirtualLocation with a Place (hybrid events) -- FindPlace below
    // handles both shapes rather than assuming one.
    private readonly record struct GdgEventInfo(
        string Name,
        DateTimeOffset Start,
        DateTimeOffset? End,
        string? Description,
        string? VenueName,
        string? StreetAddress,
        string? Locality,
        string? PostalCode,
        Attendance? AttendanceHint);

    private static GdgEventInfo? ParseEventJsonLd(string jsonLd)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonLd);
            var root = doc.RootElement;

            var name = GetString(root, "name");
            var start = GetDateTimeOffset(root, "startDate");
            if (string.IsNullOrWhiteSpace(name) || start is null)
            {
                return null;
            }

            var end = GetDateTimeOffset(root, "endDate");
            var description = GetString(root, "description");
            var attendance = MapAttendanceMode(GetString(root, "eventAttendanceMode"));

            string? venueName = null, street = null, locality = null, postalCode = null;
            var place = FindPlace(root);
            if (place is { } placeElement)
            {
                venueName = GetString(placeElement, "name");
                if (placeElement.TryGetProperty("address", out var address))
                {
                    street = GetString(address, "streetAddress");
                    locality = GetString(address, "addressLocality");
                    postalCode = GetString(address, "postalCode");
                    // addressCountry is deliberately not read: verified
                    // unreliable (returns "US" for German venues).
                }
            }

            return new GdgEventInfo(name, start.Value, end, description, venueName, street, locality, postalCode, attendance);
        }
        catch (JsonException)
        {
            return null; // malformed structured data on this one page; skip it, not the whole run
        }
    }

    // `location` can be a single {"@type": "Place", "address": {...}} object,
    // or (hybrid events) an array also containing a {"@type":
    // "VirtualLocation"} entry with no address. Only the entry that actually
    // carries an address is useful for geocoding.
    private static JsonElement? FindPlace(JsonElement root)
    {
        if (!root.TryGetProperty("location", out var location))
        {
            return null;
        }

        if (location.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in location.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("address", out _))
                {
                    return item;
                }
            }
            return null;
        }

        return location.ValueKind == JsonValueKind.Object && location.TryGetProperty("address", out _) ? location : null;
    }

    private static Attendance? MapAttendanceMode(string? eventAttendanceMode) => eventAttendanceMode switch
    {
        "https://schema.org/OfflineEventAttendanceMode" => Attendance.InPerson,
        "https://schema.org/OnlineEventAttendanceMode" => Attendance.Online,
        "https://schema.org/MixedEventAttendanceMode" => Attendance.Hybrid,
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

    private static RawEvent Map(GdgEventInfo info, string url, string slug)
    {
        var addressParts = new[] { info.StreetAddress, info.PostalCode, info.Locality }
            .Where(p => !string.IsNullOrWhiteSpace(p));
        var venueAddress = string.Join(", ", addressParts);

        return new RawEvent
        {
            SourceEventId = slug,
            Title = info.Name,
            Description = info.Description,
            Start = info.Start,
            End = info.End,
            // All curated slugs for this source are German GDG chapters (see
            // sources.yaml); the JSON-LD only ever gives a numeric UTC offset,
            // not an IANA id, so this is the one adapter-specific place that
            // fills in the zone Dedupe needs for its local-day calculation.
            TimeZoneId = "Europe/Berlin",
            VenueName = info.VenueName,
            VenueAddress = string.IsNullOrEmpty(venueAddress) ? null : venueAddress,
            City = info.Locality,
            PostalCode = info.PostalCode,
            Url = url,
            AttendanceHint = info.AttendanceHint,
        };
    }
}
