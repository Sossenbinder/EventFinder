namespace EventFinder.Core;

public readonly record struct BoundingBox(double MinLat, double MaxLat, double MinLon, double MaxLon);

public static class Geo
{
    private const double EarthRadiusKm = 6371.0088;

    public static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = DegreesToRadians(lat2 - lat1);
        var dLon = DegreesToRadians(lon2 - lon1);
        var a = (Math.Sin(dLat / 2) * Math.Sin(dLat / 2))
                + (Math.Cos(DegreesToRadians(lat1)) * Math.Cos(DegreesToRadians(lat2))
                   * Math.Sin(dLon / 2) * Math.Sin(dLon / 2));
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusKm * c;
    }

    // A lat/lon rectangle guaranteed to contain the radius circle, cheap to
    // evaluate as a SQL WHERE clause so only the (small) remaining candidate
    // set needs an exact haversine pass in memory.
    public static BoundingBox GetBoundingBox(double centerLat, double centerLon, double radiusKm)
    {
        var latDelta = radiusKm / EarthRadiusKm * (180 / Math.PI);
        var lonCosine = Math.Cos(DegreesToRadians(centerLat));
        var lonDelta = Math.Abs(lonCosine) < 1e-9
            ? 180
            : radiusKm / (EarthRadiusKm * lonCosine) * (180 / Math.PI);

        return new BoundingBox(
            MinLat: Math.Max(centerLat - latDelta, -90),
            MaxLat: Math.Min(centerLat + latDelta, 90),
            MinLon: centerLon - lonDelta,
            MaxLon: centerLon + lonDelta);
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180;
}
