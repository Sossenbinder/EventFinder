using EventFinder.Data;
using EventFinder.Ingestion;
using EventFinder.Ingestion.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EventFinder.Api.Cli;

// `sources verify` and `ingest once` (outline: CLI verbs on the same
// executable). Both share CompositionRoot.AddEventFinder with the web host
// via a plain generic Host rather than a WebApplication, since neither verb
// needs Kestrel/HTTP.
public static class CliCommands
{
    // The gate that keeps unverified URLs out of sources.yaml (AGENTS.md's
    // registry-honesty rule): exits non-zero if any *enabled* source fails.
    public static async Task<int> RunSourcesVerifyAsync(CancellationToken ct)
    {
        using var host = BuildHost();
        await using var scope = host.Services.CreateAsyncScope();

        var sources = scope.ServiceProvider.GetRequiredService<IReadOnlyList<SourceDescriptor>>();
        var verifier = scope.ServiceProvider.GetRequiredService<SourceVerifier>();
        var enabled = sources.Where(s => s.Enabled).ToList();
        var results = await verifier.VerifyAsync(enabled, ct);

        PrintTable(results);

        var anyFailed = results.Any(Failed);
        if (anyFailed)
        {
            Console.WriteLine();
            Console.WriteLine("FAILED: one or more enabled sources did not verify.");
        }
        return anyFailed ? 1 : 0;
    }

    // Forces a single ingestion run for local testing, outside the
    // background service's schedule.
    public static async Task<int> RunIngestOnceAsync(CancellationToken ct)
    {
        using var host = BuildHost();
        await using var scope = host.Services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<EventFinderDbContext>();
        await db.Database.MigrateAsync(ct);

        var sources = scope.ServiceProvider.GetRequiredService<IReadOnlyList<SourceDescriptor>>();
        var runner = scope.ServiceProvider.GetRequiredService<IngestionRunner>();
        var store = scope.ServiceProvider.GetRequiredService<EventStore>();

        var statuses = await runner.RunAsync(sources, ct);
        await store.SaveSourceStatusesAsync(statuses, ct);

        foreach (var (sourceId, status) in statuses)
        {
            Console.WriteLine(status.LastError is null
                ? $"{sourceId}: ok, {status.EventCount} events"
                : $"{sourceId}: FAILED - {status.LastError}");
        }

        return statuses.Values.Any(s => s.LastError is not null) ? 1 : 0;
    }

    private static IHost BuildHost()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddEnvironmentVariables(prefix: "EVENTFINDER__");
        builder.Services.AddEventFinder(builder.Configuration);
        return builder.Build();
    }

    // "Success" is exactly Reachable + a 2xx-standing status + no parse
    // error; SourceVerifier reports HTTP error statuses (e.g. 404) with
    // Reachable=true, so a plain Reachable check alone would miss them.
    private static bool Failed(SourceVerificationResult r) =>
        !r.Reachable || r.HttpStatus != 200 || r.FirstParseError is not null;

    private static void PrintTable(IReadOnlyList<SourceVerificationResult> results)
    {
        Console.WriteLine($"{"ID",-24} {"OK",-5} {"Status",-8} {"Parsed",-8} First error");
        foreach (var r in results)
        {
            var status = r.HttpStatus?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-";
            var error = r.FirstParseError is null ? "" : Truncate(r.FirstParseError, 60);
            Console.WriteLine($"{r.SourceId,-24} {(Failed(r) ? "no" : "yes"),-5} {status,-8} {r.EventCount,-8} {error}");
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : string.Concat(s.AsSpan(0, max), "...");
}
