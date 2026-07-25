using EventFinder.Ingestion;
using FluentAssertions;

namespace EventFinder.Tests.Ingestion;

public sealed class SourceRegistryTests
{
    [Fact]
    public void Parse_ValidYaml_ReturnsDescriptorsInOrder()
    {
        const string yaml = """
            sources:
              - id: some-ics-feed
                org: Some User Group
                type: ics
                url: "https://a.example/feed.ics"
                region: global
                tags: [gdg, google]
                enabled: true
            """;

        var sources = SourceRegistry.Parse(yaml, registeredHtmlAdapterKeys: [], sourceDescription: "test.yaml");

        sources.Should().ContainSingle();
        sources[0].Id.Should().Be("some-ics-feed");
        sources[0].Type.Should().Be("ics");
        sources[0].Tags.Should().BeEquivalentTo(["gdg", "google"]);
        sources[0].Enabled.Should().BeTrue();
    }

    [Fact]
    public void Parse_GdgSitemapWithSlugs_RoundTripsTheSlugList()
    {
        const string yaml = """
            sources:
              - id: gdg-sitemap-de
                org: Google Developer Groups
                type: gdg-sitemap
                url: "https://gdg.community.dev/sitemap.xml"
                slugs: [gdg-karlsruhe, gdg-berlin]
            """;

        var sources = SourceRegistry.Parse(yaml, registeredHtmlAdapterKeys: [], sourceDescription: "test.yaml");

        sources.Should().ContainSingle();
        sources[0].Type.Should().Be("gdg-sitemap");
        sources[0].Slugs.Should().BeEquivalentTo(["gdg-karlsruhe", "gdg-berlin"]);
    }

    [Fact]
    public void Parse_GdgSitemapWithNoSlugs_ThrowsRatherThanSilentlyMatchingNothingForever()
    {
        const string yaml = """
            sources:
              - id: gdg-sitemap-de
                org: Google Developer Groups
                type: gdg-sitemap
                url: "https://gdg.community.dev/sitemap.xml"
            """;

        var act = () => SourceRegistry.Parse(yaml, registeredHtmlAdapterKeys: [], sourceDescription: "test.yaml");

        act.Should().Throw<InvalidOperationException>().WithMessage("*no chapter slugs*");
    }

    [Fact]
    public void Parse_DuplicateIds_ThrowsRatherThanSilentlyLoadingBoth()
    {
        const string yaml = """
            sources:
              - id: dup
                org: A
                type: ics
                url: "https://a.example/feed.ics"
              - id: dup
                org: B
                type: ics
                url: "https://b.example/feed.ics"
            """;

        var act = () => SourceRegistry.Parse(yaml, registeredHtmlAdapterKeys: [], sourceDescription: "test.yaml");

        act.Should().Throw<InvalidOperationException>().WithMessage("*duplicate*dup*");
    }

    [Fact]
    public void Parse_UnknownType_ThrowsLoudlyRatherThanIgnoringTheEntry()
    {
        const string yaml = """
            sources:
              - id: weird
                org: A
                type: rss
                url: "https://a.example/feed"
            """;

        var act = () => SourceRegistry.Parse(yaml, registeredHtmlAdapterKeys: [], sourceDescription: "test.yaml");

        act.Should().Throw<InvalidOperationException>().WithMessage("*unknown type*");
    }

    [Fact]
    public void Parse_HtmlSourceWithUnregisteredAdapter_Throws()
    {
        const string yaml = """
            sources:
              - id: meetup-group
                org: Some Meetup
                type: html
                url: "https://meetup.example/group"
                adapter: nonexistent-parser
            """;

        var act = () => SourceRegistry.Parse(yaml, registeredHtmlAdapterKeys: ["known-parser"], sourceDescription: "test.yaml");

        act.Should().Throw<InvalidOperationException>().WithMessage("*nonexistent-parser*not registered*");
    }

    [Fact]
    public void Parse_HtmlSourceWithRegisteredAdapter_Succeeds()
    {
        const string yaml = """
            sources:
              - id: meetup-group
                org: Some Meetup
                type: html
                url: "https://meetup.example/group"
                adapter: known-parser
            """;

        var sources = SourceRegistry.Parse(yaml, registeredHtmlAdapterKeys: ["known-parser"], sourceDescription: "test.yaml");

        sources.Should().ContainSingle();
        sources[0].Adapter.Should().Be("known-parser");
    }

    [Fact]
    public void Parse_MalformedYaml_ThrowsRatherThanReturningAnEmptyRegistry()
    {
        const string malformed = "sources: [this is not: valid: yaml: at all";

        var act = () => SourceRegistry.Parse(malformed, registeredHtmlAdapterKeys: [], sourceDescription: "test.yaml");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Load_SeededRepoSourcesYaml_ContainsTheCuratedRegistry()
    {
        var path = System.IO.Path.Combine(TestPaths.RepoRoot, "sources.yaml");

        var sources = SourceRegistry.Load(path, registeredHtmlAdapterKeys: ["meetup-group"]);

        // 1 gdg-sitemap source + N curated meetup-group sources; asserting
        // "at least a couple dozen" rather than an exact count keeps this
        // test from needing an edit every time the main session curates one
        // more meetup group into sources.yaml.
        sources.Count.Should().BeGreaterThanOrEqualTo(30);

        var gdg = sources.Single(s => s.Id == "gdg-bevy-de");
        gdg.Type.Should().Be("gdg-sitemap");
        gdg.Url.Should().Contain("gdg.community.dev");
        gdg.Slugs.Should().Contain(["gdg-karlsruhe", "gdg-berlin", "gdg-munich"]);

        sources.Where(s => s.Type == "html").Should().OnlyContain(s => s.Adapter == "meetup-group");
    }
}
