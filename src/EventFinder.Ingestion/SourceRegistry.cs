using EventFinder.Ingestion.Contracts;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace EventFinder.Ingestion;

// Root shape of sources.yaml: a single top-level "sources" list. A separate
// mutable DTO (rather than deserializing straight into SourceDescriptor) is
// needed because YamlDotNet's collection binding wants a concrete List<T>,
// not SourceDescriptor's IReadOnlyList<string> Tags.
internal sealed class SourcesYaml
{
    public List<SourceDescriptorYaml> Sources { get; set; } = [];
}

internal sealed class SourceDescriptorYaml
{
    public string Id { get; set; } = "";
    public string Org { get; set; } = "";
    public string Type { get; set; } = "";
    public string Url { get; set; } = "";
    public string? Adapter { get; set; }
    public string? Region { get; set; }
    public List<string> Tags { get; set; } = [];
    public bool Enabled { get; set; } = true;
    public List<string> Slugs { get; set; } = [];

    public SourceDescriptor ToDescriptor() => new()
    {
        Id = Id,
        Org = Org,
        Type = Type,
        Url = Url,
        Adapter = Adapter,
        Region = Region,
        Tags = Tags,
        Enabled = Enabled,
        Slugs = Slugs,
    };
}

// Loads and validates sources.yaml. This is configuration, not remote data --
// per AGENTS.md's registry-honesty rule, a malformed file or a dangling
// adapter reference must fail the load loudly, not degrade silently.
public static class SourceRegistry
{
    private static readonly string[] KnownTypes = ["ics", "html", "gdg-sitemap"];

    public static IReadOnlyList<SourceDescriptor> Load(string yamlPath, IReadOnlyCollection<string> registeredHtmlAdapterKeys)
    {
        if (!File.Exists(yamlPath))
        {
            throw new InvalidOperationException($"Source registry not found at '{yamlPath}'.");
        }

        var yaml = File.ReadAllText(yamlPath);
        return Parse(yaml, registeredHtmlAdapterKeys, yamlPath);
    }

    // Public (not just an implementation detail of Load): useful on its own
    // for validating a YAML string before it is written to disk.
    public static IReadOnlyList<SourceDescriptor> Parse(
        string yaml, IReadOnlyCollection<string> registeredHtmlAdapterKeys, string sourceDescription)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        SourcesYaml parsed;
        try
        {
            parsed = deserializer.Deserialize<SourcesYaml>(yaml) ?? new SourcesYaml();
        }
        catch (Exception ex) when (ex is YamlDotNet.Core.YamlException)
        {
            throw new InvalidOperationException($"'{sourceDescription}' is not valid YAML: {ex.Message}", ex);
        }

        var sources = parsed.Sources.Select(s => s.ToDescriptor()).ToList();
        Validate(sources, registeredHtmlAdapterKeys, sourceDescription);
        return sources;
    }

    private static void Validate(
        IReadOnlyList<SourceDescriptor> sources, IReadOnlyCollection<string> registeredHtmlAdapterKeys, string sourceDescription)
    {
        var duplicateIds = sources
            .GroupBy(s => s.Id, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicateIds.Count > 0)
        {
            throw new InvalidOperationException(
                $"'{sourceDescription}' has duplicate source ids: {string.Join(", ", duplicateIds)}.");
        }

        foreach (var source in sources)
        {
            if (!KnownTypes.Contains(source.Type))
            {
                throw new InvalidOperationException(
                    $"Source '{source.Id}' has unknown type '{source.Type}'; expected one of: {string.Join(", ", KnownTypes)}.");
            }

            if (source.Type == "html")
            {
                if (string.IsNullOrWhiteSpace(source.Adapter))
                {
                    throw new InvalidOperationException($"Source '{source.Id}' is type 'html' but names no adapter.");
                }
                if (!registeredHtmlAdapterKeys.Contains(source.Adapter))
                {
                    throw new InvalidOperationException(
                        $"Source '{source.Id}' names adapter '{source.Adapter}', which is not registered. " +
                        $"Registered adapters: {string.Join(", ", registeredHtmlAdapterKeys)}.");
                }
            }

            if (source.Type == "gdg-sitemap" && source.Slugs.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Source '{source.Id}' is type 'gdg-sitemap' but names no chapter slugs.");
            }
        }
    }
}
