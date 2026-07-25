using AngleSharp.Dom;
using EventFinder.Ingestion.Contracts;

namespace EventFinder.Ingestion.Adapters;

// One implementation per hand-written per-site scraper. AdapterKey must
// match a sources.yaml entry's "adapter" field exactly; SourceRegistry
// validates that link at load time so a typo fails at startup, not silently
// at fetch time.
//
// No implementation ships in this workstream: shipping one would mean
// writing it against a site never actually fetched/recorded as a fixture,
// which is exactly the "never invent a source URL/parser" rule this project
// runs on. HtmlSource below is the dispatch mechanism and registration
// point; the first real parser lands alongside its recorded fixture when a
// specific HTML source is curated and verified.
public interface IHtmlEventParser
{
    string AdapterKey { get; }

    IReadOnlyList<RawEvent> Parse(IDocument document, SourceDescriptor source);
}
