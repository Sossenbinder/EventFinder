namespace EventFinder.Api.Config;

public sealed class DatabaseOptions
{
    public const string Section = "Database";
    public string Path { get; set; } = "data/eventfinder.db";
}

// Root directory for state this process writes at runtime -- currently just
// the conditional-fetch cache (see EventFinder.Ingestion's IConditionalFetchCache).
// The SQLite file's directory is derived from DatabaseOptions.Path instead,
// so the two can be split across volumes if that's ever useful.
//
// GazetteerPlacesCsv/GazetteerPostalCsv/SourcesFile default to the copies
// shipped alongside the executable (see EventFinder.Api.csproj's Content
// items) but are overridable so tests can point at the repo's real data/
// and sources.yaml deterministically, without relying on how a test
// project's build happens to lay out its own output directory.
public sealed class DataOptions
{
    public const string Section = "Data";
    public string Directory { get; set; } = "data";
    public string GazetteerPlacesCsv { get; set; } = Path.Combine(AppContext.BaseDirectory, "data", "places-de.csv");
    public string GazetteerPostalCsv { get; set; } = Path.Combine(AppContext.BaseDirectory, "data", "postal-de.csv");
    public string SourcesFile { get; set; } = Path.Combine(AppContext.BaseDirectory, "sources.yaml");
}

public sealed class IngestionOptions
{
    public const string Section = "Ingestion";

    // Tests and local one-off `ingest once` runs set this false so the
    // background service never competes with an explicit run.
    public bool Enabled { get; set; } = true;

    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(6);

    // +/- this fraction of Interval, so many deployments started around the
    // same time don't all hit their sources at exactly the same offset.
    public double JitterFraction { get; set; } = 0.1;

    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(15);
}

public sealed class CorsOptions
{
    public const string Section = "Cors";
    public string[] AllowedOrigins { get; set; } = ["http://localhost:5173"];
}
