using AngleSharp;
using EventFinder.Ingestion.Contracts;
using EventFinder.Ingestion.Http;

namespace EventFinder.Ingestion.Adapters;

// Per-source HTML scraper. Does not parse anything itself -- it fetches
// (after a robots.txt check) and dispatches to whichever IHtmlEventParser
// is registered under the descriptor's Adapter key. This is the most
// breakage-prone source type per the outline; per-source isolation in
// IngestionRunner is what keeps a redesigned page from taking down a run.
public sealed class HtmlSource(
    IPoliteHttpClient httpClient,
    IRobotsTxtCache robotsCache,
    IEnumerable<IHtmlEventParser> parsers) : IEventSource
{
    public string Type => "html";

    public async Task<IReadOnlyList<RawEvent>> FetchAsync(SourceDescriptor source, CancellationToken ct)
    {
        var url = new Uri(source.Url);
        if (!await robotsCache.IsAllowedAsync(url, ct))
        {
            throw new RobotsDisallowedException(source.Url);
        }

        var parser = parsers.FirstOrDefault(p => p.AdapterKey == source.Adapter)
            ?? throw new InvalidOperationException(
                $"No IHtmlEventParser registered for adapter key '{source.Adapter}' (source '{source.Id}').");

        var fetch = await httpClient.GetAsync(source.Id, source.Url, ct);

        var context = BrowsingContext.New(Configuration.Default);
        using var document = await context.OpenAsync(req => req.Content(fetch.Body), ct);

        return parser.Parse(document, source);
    }
}
