namespace EventFinder.Ingestion.Geocoding;

// Plain POCO rather than IOptions<T> -- this project has no ASP.NET host of
// its own (same reasoning as Http/PolitenessOptions); the API host's
// CompositionRoot binds it from the "Geocoding" configuration section and
// passes the bound instance into AddEventFinderIngestion.
public sealed class GeocodingOptions
{
    public const string Section = "Geocoding";
    public const string HttpClientName = "EventFinder.Geocoding";

    // A free community service -- off by default would be safer, but the
    // whole point of this feature is address-level precision, so it defaults
    // on and Enabled=false is the escape hatch (also used by tests).
    public bool Enabled { get; set; } = true;

    public string Endpoint { get; set; } = "https://photon.komoot.io/api/";

    // Keeps a bad ingestion batch (e.g. a source suddenly emitting hundreds
    // of street addresses) from hammering a free, keyless service.
    public int MaxLookupsPerRun { get; set; } = 200;

    // Minimum gap between two Photon requests -- "be conservative" per the
    // task brief, so this defaults to 1 request/second rather than
    // PolitenessOptions' 500ms.
    public int DelayMs { get; set; } = 1000;

    public string UserAgent { get; set; } =
        "EventFinderBot/1.0 (+https://github.com/StefanSchranz/EventFinder; German tech-event aggregator)";

    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);
}
