using EventFinder.Core;

namespace EventFinder.Api.Endpoints;

public sealed record PlaceDto(string Name, string Admin1, long Population, double Latitude, double Longitude);

// GET /api/places?q= -- gazetteer autocomplete for the home-location picker.
// Gazetteer.Search already ranks by population (its internal index is
// sorted population-descending and scanned in that order).
public static class PlacesEndpoints
{
    private const int DefaultLimit = 10;
    private const int MaxLimit = 50;

    public static IEndpointRouteBuilder MapPlacesEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/places", (string? q, int? limit, Gazetteer gazetteer) =>
        {
            if (string.IsNullOrWhiteSpace(q))
            {
                return Results.Ok(Array.Empty<PlaceDto>());
            }

            var boundedLimit = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
            var results = gazetteer.Search(q, boundedLimit)
                .Select(p => new PlaceDto(p.Name, p.Admin1, p.Population, p.Latitude, p.Longitude))
                .ToList();

            return Results.Ok(results);
        })
        .WithName("GetPlaces");

        return app;
    }
}
