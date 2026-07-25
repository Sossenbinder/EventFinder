namespace EventFinder.Ingestion.Http;

// Plain POCO rather than IOptions<T> -- this project has no ASP.NET host of
// its own; workstream 3's composition root can still bind it from
// configuration and register the bound instance.
public sealed class PolitenessOptions
{
    public const string HttpClientName = "EventFinder.Ingestion";

    // Identifies the project and gives sites an operator to complain to, per
    // AGENTS.md's "this tool will be publicly deployed" scraping requirement.
    public string UserAgent { get; set; } =
        "EventFinderBot/1.0 (+https://github.com/StefanSchranz/EventFinder; German tech-event aggregator)";

    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(20);

    // Minimum gap between two requests to the same host.
    public TimeSpan PerHostDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    public int MaxRetries { get; set; } = 3;
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(500);
}
