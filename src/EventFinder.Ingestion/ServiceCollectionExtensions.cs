using EventFinder.Core;
using EventFinder.Data;
using EventFinder.Ingestion.Adapters;
using EventFinder.Ingestion.Contracts;
using EventFinder.Ingestion.Http;
using Microsoft.Extensions.DependencyInjection;

namespace EventFinder.Ingestion;

// Composition-root wiring for this project's own services. workstream 3's
// API host calls this rather than re-registering everything itself; it
// still owns the actual HostBuilder/BackgroundService/CLI verb.
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEventFinderIngestion(
        this IServiceCollection services, string conditionalFetchCacheDirectory, PolitenessOptions? politeness = null)
    {
        var options = politeness ?? new PolitenessOptions();
        services.AddSingleton(options);

        services.AddHttpClient(PolitenessOptions.HttpClientName, client =>
        {
            client.Timeout = options.RequestTimeout;
            client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
        });

        services.AddSingleton<IConditionalFetchCache>(new FileConditionalFetchCache(conditionalFetchCacheDirectory));
        services.AddSingleton<IPoliteHttpClient, PoliteHttpClient>();
        services.AddSingleton<IRobotsTxtCache>(sp =>
            new RobotsTxtCache(sp.GetRequiredService<IPoliteHttpClient>(), RobotsUserAgentToken(options.UserAgent)));

        services.AddSingleton<IEventSource, IcsSource>();
        services.AddSingleton<IEventSource, GdgSitemapSource>();
        services.AddSingleton<IEventSource, HtmlSource>();
        services.AddSingleton(sp => sp.GetServices<IEventSource>().ToDictionary(s => s.Type, StringComparer.Ordinal));

        services.AddSingleton<IHtmlEventParser, MeetupGroupHtmlParser>();

        services.AddSingleton<IngestionRunner>(sp => new IngestionRunner(
            sp.GetRequiredService<Dictionary<string, IEventSource>>(),
            sp.GetRequiredService<EventStore>(),
            sp.GetRequiredService<Gazetteer>(),
            Normalization.DefaultKeywordToTag));
        services.AddSingleton<SourceVerifier>(sp => new SourceVerifier(
            sp.GetRequiredService<Dictionary<string, IEventSource>>()));

        return services;
    }

    // The first token of the descriptive User-Agent string is what robots.txt
    // "User-agent:" lines are matched against (e.g. "EventFinderBot/1.0" ->
    // "EventFinderBot").
    private static string RobotsUserAgentToken(string userAgent) =>
        userAgent.Split(['/', ' '], 2)[0];
}
