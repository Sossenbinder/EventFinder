using EventFinder.Api.Config;
using EventFinder.Data;
using EventFinder.Ingestion;
using EventFinder.Ingestion.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EventFinder.Api;

// Runs one ingestion pass shortly after startup, then again on a jittered
// interval (outline: default 6h). Persists SourceStatus rows after every
// pass so /api/sources reflects the latest run even when nothing changed.
public sealed partial class IngestionBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<IngestionOptions> options,
    ILogger<IngestionBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(options.CurrentValue.InitialDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var rng = new Random();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                // A failed pass must not kill the loop -- per-source isolation
                // already protects individual sources; this is the outer guard
                // for anything IngestionRunner itself didn't catch (e.g. a
                // database error while persisting).
                Log.IngestionRunFailed(logger, ex);
            }

            try
            {
                await Task.Delay(NextDelay(options.CurrentValue, rng), stoppingToken);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var sources = scope.ServiceProvider.GetRequiredService<IReadOnlyList<SourceDescriptor>>();
        var runner = scope.ServiceProvider.GetRequiredService<IngestionRunner>();
        var store = scope.ServiceProvider.GetRequiredService<EventStore>();

        var statuses = await runner.RunAsync(sources, ct);
        await store.SaveSourceStatusesAsync(statuses, ct);
    }

    private static TimeSpan NextDelay(IngestionOptions opts, Random rng)
    {
        var baseMs = opts.Interval.TotalMilliseconds;
        var jitterMs = baseMs * opts.JitterFraction;
        var offsetMs = ((rng.NextDouble() * 2) - 1) * jitterMs;
        return TimeSpan.FromMilliseconds(Math.Max(0, baseMs + offsetMs));
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "Ingestion run failed")]
        public static partial void IngestionRunFailed(ILogger logger, Exception ex);
    }
}
