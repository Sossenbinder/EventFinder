using EventFinder.Api.Config;
using EventFinder.Core;
using EventFinder.Data;
using EventFinder.Ingestion;
using EventFinder.Ingestion.Adapters;
using EventFinder.Ingestion.Contracts;
using EventFinder.Ingestion.Geocoding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EventFinder.Api;

// Composition root shared by the web host (Program.cs) and both CLI verbs
// (Cli/CliCommands.cs), so `dotnet run`, `sources verify` and `ingest once`
// can never end up wiring the pipeline differently by accident.
public static class CompositionRoot
{
    public static IServiceCollection AddEventFinder(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.Section));
        services.Configure<DataOptions>(configuration.GetSection(DataOptions.Section));
        services.Configure<IngestionOptions>(configuration.GetSection(IngestionOptions.Section));
        services.Configure<CorsOptions>(configuration.GetSection(CorsOptions.Section));

        services.AddDbContextFactory<EventFinderDbContext>((sp, opt) =>
        {
            var path = sp.GetRequiredService<IOptions<DatabaseOptions>>().Value.Path;
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            opt.UseSqlite($"Data Source={path}");
        });
        services.AddScoped<EventFinderDbContext>(sp =>
            sp.GetRequiredService<IDbContextFactory<EventFinderDbContext>>().CreateDbContext());
        services.AddScoped<EventStore>();
        services.AddScoped<GeocodeCacheStore>();

        var dataOptions = configuration.GetSection(DataOptions.Section).Get<DataOptions>() ?? new DataOptions();
        var geocodingOptions = configuration.GetSection(GeocodingOptions.Section).Get<GeocodingOptions>() ?? new GeocodingOptions();

        // Gazetteer parses ~70k rows; loaded exactly once here and shared as
        // a singleton so no request (or ingestion run) repeats that I/O.
        services.AddSingleton(_ => Gazetteer.Load(dataOptions.GazetteerPlacesCsv, dataOptions.GazetteerPostalCsv));

        services.AddSingleton<IReadOnlyList<SourceDescriptor>>(sp =>
        {
            var adapterKeys = sp.GetServices<IHtmlEventParser>().Select(p => p.AdapterKey).ToList();
            return SourceRegistry.Load(dataOptions.SourcesFile, adapterKeys);
        });

        services.AddEventFinderIngestion(Path.Combine(dataOptions.Directory, "fetch-cache"), geocoding: geocodingOptions);

        // AddEventFinderIngestion (EventFinder.Ingestion, workstream 2) registers
        // IngestionRunner as a Singleton that captures EventStore at construction
        // time. EventStore must be Scoped here -- it wraps a per-operation
        // DbContext -- so that original registration would otherwise pin one
        // DbContext for the entire process lifetime (a captive-dependency bug,
        // and unsafe under concurrent requests). Re-registering it as Scoped
        // shadows the earlier descriptor for GetRequiredService resolution
        // without touching EventFinder.Ingestion's own code. SourceVerifier has
        // no such dependency and keeps its original, correctly-Singleton
        // registration.
        services.AddScoped<IngestionRunner>(sp => new IngestionRunner(
            sp.GetRequiredService<Dictionary<string, IEventSource>>(),
            sp.GetRequiredService<EventStore>(),
            sp.GetRequiredService<Gazetteer>(),
            Normalization.DefaultKeywordToTag,
            TimeProvider.System,
            sp.GetRequiredService<IAddressGeocoder>()));

        return services;
    }
}
