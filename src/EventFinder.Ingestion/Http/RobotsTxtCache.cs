using System.Collections.Concurrent;
using System.Globalization;

namespace EventFinder.Ingestion.Http;

// The Disallow prefixes (and, if present, Crawl-delay) that apply to us for
// one host. Allow overrides and wildcard/`$` path matching are not
// implemented -- a plain prefix-match Disallow-only reading is more
// conservative than the full spec, which is the safe direction to err in for
// a politeness check.
public sealed record RobotsRules(IReadOnlyList<string> DisallowedPrefixes, TimeSpan? CrawlDelay = null)
{
    public static readonly RobotsRules AllowAll = new([]);
}

public interface IRobotsTxtCache
{
    Task<bool> IsAllowedAsync(Uri url, CancellationToken ct);

    // Exposes the full parsed rule set (not just the allow/disallow verdict)
    // so callers that need Crawl-delay -- a sequential, many-requests adapter
    // like the GDG sitemap source -- can throttle themselves accordingly.
    Task<RobotsRules> GetRulesAsync(Uri url, CancellationToken ct);
}

// Fetches and caches robots.txt per host for the lifetime of this instance
// (typically one ingestion run). A missing or unreachable robots.txt is
// treated as "allow all", matching the conventional reading of RFC 9309.
public sealed class RobotsTxtCache(IPoliteHttpClient httpClient, string userAgentToken) : IRobotsTxtCache
{
    private readonly ConcurrentDictionary<string, Task<RobotsRules>> _rulesByHost = new(StringComparer.OrdinalIgnoreCase);

    public async Task<bool> IsAllowedAsync(Uri url, CancellationToken ct)
    {
        var rules = await GetRulesAsync(url, ct);
        return !rules.DisallowedPrefixes.Any(prefix => url.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal));
    }

    public Task<RobotsRules> GetRulesAsync(Uri url, CancellationToken ct) =>
        _rulesByHost.GetOrAdd(url.Host, _ => FetchRulesAsync(url, ct));

    private async Task<RobotsRules> FetchRulesAsync(Uri pageUrl, CancellationToken ct)
    {
        var robotsUrl = new Uri(pageUrl, "/robots.txt");
        try
        {
            var body = await httpClient.GetRawAsync(robotsUrl.ToString(), ct);
            return Parse(body, userAgentToken);
        }
        catch (SourceHttpErrorException)
        {
            return RobotsRules.AllowAll;
        }
        catch (SourceUnreachableException)
        {
            return RobotsRules.AllowAll;
        }
    }

    // Mutable per-group accumulator. A plain class (not a value tuple) is
    // needed here: Crawl-delay is a scalar field, and mutating a scalar field
    // through `current.Value.X = ...` on a nullable *value* tuple would only
    // mutate a throwaway copy, unlike the List<string> fields whose mutation
    // works through the shared reference regardless.
    private sealed class RobotsGroup
    {
        public List<string> Agents { get; } = [];
        public List<string> Disallow { get; } = [];
        public double? CrawlDelaySeconds { get; set; }
    }

    public static RobotsRules Parse(string content, string userAgentToken)
    {
        var groups = new List<RobotsGroup>();
        RobotsGroup? current = null;
        var sawDirectiveInCurrent = false;

        foreach (var rawLine in content.Split('\n'))
        {
            var line = StripComment(rawLine).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var colon = line.IndexOf(':');
            if (colon < 0)
            {
                continue;
            }

            var field = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();

            if (field.Equals("User-agent", StringComparison.OrdinalIgnoreCase))
            {
                if (current is null || sawDirectiveInCurrent)
                {
                    current = new RobotsGroup();
                    groups.Add(current);
                    sawDirectiveInCurrent = false;
                }
                current.Agents.Add(value);
            }
            else if (field.Equals("Disallow", StringComparison.OrdinalIgnoreCase) && value.Length > 0)
            {
                if (current is not null)
                {
                    current.Disallow.Add(value);
                    sawDirectiveInCurrent = true;
                }
            }
            else if (field.Equals("Crawl-delay", StringComparison.OrdinalIgnoreCase) && current is not null)
            {
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
                {
                    current.CrawlDelaySeconds = seconds;
                }
                sawDirectiveInCurrent = true;
            }
            else if (current is not null)
            {
                // Allow, Sitemap, etc. -- not modeled, but they still close
                // the current group's User-agent list.
                sawDirectiveInCurrent = true;
            }
        }

        // robots.txt commonly repeats "User-agent: *" (or our own token) as
        // several separate stanzas, one per concern (e.g. meetup.com has a
        // dozen small "User-agent: *" blocks). Per RFC 9309, all rule lines
        // under the same product token are the union of that token's rules,
        // not just whichever block happened to appear first.
        var specific = groups.Where(g => g.Agents.Any(a => a.Equals(userAgentToken, StringComparison.OrdinalIgnoreCase))).ToList();
        if (specific.Count > 0)
        {
            return Combine(specific);
        }

        var wildcard = groups.Where(g => g.Agents.Contains("*")).ToList();
        return wildcard.Count > 0 ? Combine(wildcard) : RobotsRules.AllowAll;
    }

    private static RobotsRules Combine(IReadOnlyList<RobotsGroup> matchingGroups)
    {
        var disallow = matchingGroups.SelectMany(g => g.Disallow).Distinct(StringComparer.Ordinal).ToList();
        var crawlDelaySeconds = matchingGroups.Select(g => g.CrawlDelaySeconds).FirstOrDefault(v => v is not null);
        var crawlDelay = crawlDelaySeconds is null ? (TimeSpan?)null : TimeSpan.FromSeconds(crawlDelaySeconds.Value);
        return new RobotsRules(disallow, crawlDelay);
    }

    private static string StripComment(string line)
    {
        var hash = line.IndexOf('#');
        return hash < 0 ? line : line[..hash];
    }
}
