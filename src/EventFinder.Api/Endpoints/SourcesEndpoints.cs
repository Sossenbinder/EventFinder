using EventFinder.Data;
using EventFinder.Ingestion.Contracts;

namespace EventFinder.Api.Endpoints;

public sealed record SourceStatusDto(
    string Id,
    string Org,
    string Type,
    string Url,
    bool Enabled,
    DateTime? LastRunUtc,
    DateTime? LastSuccessUtc,
    int EventCount,
    string? LastError);

// GET /api/sources -- the transparency view: every registry entry with its
// last run, last success, event count and last error (outline's /sources
// page).
public static class SourcesEndpoints
{
    public static IEndpointRouteBuilder MapSourcesEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/sources", async (
            IReadOnlyList<SourceDescriptor> registry, EventStore store, CancellationToken ct) =>
        {
            var statuses = (await store.GetSourceStatusesAsync(ct))
                .ToDictionary(s => s.SourceId, StringComparer.Ordinal);

            var result = registry
                .Select(source =>
                {
                    statuses.TryGetValue(source.Id, out var status);
                    return new SourceStatusDto(
                        source.Id,
                        source.Org,
                        source.Type,
                        source.Url,
                        source.Enabled,
                        status?.LastRunUtc,
                        status?.LastSuccessUtc,
                        status?.EventCount ?? 0,
                        status?.LastError);
                })
                .ToList();

            return Results.Ok(result);
        })
        .WithName("GetSources");

        return app;
    }
}
