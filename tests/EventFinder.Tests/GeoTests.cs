using EventFinder.Core;
using FluentAssertions;

namespace EventFinder.Tests;

public class GeoTests
{
    // Coordinates as listed in data/places-de.csv.
    private const double KirchheimLat = 48.64683;
    private const double KirchheimLon = 9.45378;
    private const double StuttgartLat = 48.78232;
    private const double StuttgartLon = 9.17702;

    [Fact]
    public void DistanceKm_KirchheimUnterTeckToStuttgart_IsApproximatelyTheKnownStraightLineDistance()
    {
        var distance = Geo.DistanceKm(KirchheimLat, KirchheimLon, StuttgartLat, StuttgartLon);

        distance.Should().BeApproximately(25.3, 1.0);
    }

    [Fact]
    public void GetBoundingBox_ContainsEveryPointOnTheRadiusCircle()
    {
        const double radiusKm = 25;
        var box = Geo.GetBoundingBox(KirchheimLat, KirchheimLon, radiusKm);

        for (var bearingDeg = 0; bearingDeg < 360; bearingDeg += 10)
        {
            var (lat, lon) = ProjectPoint(KirchheimLat, KirchheimLon, radiusKm, bearingDeg);
            lat.Should().BeInRange(box.MinLat, box.MaxLat);
            lon.Should().BeInRange(box.MinLon, box.MaxLon);
        }
    }

    // Destination point on a great circle at the given distance/bearing from
    // (lat, lon); used only to generate boundary points for the bbox test.
    private static (double Lat, double Lon) ProjectPoint(double lat, double lon, double distanceKm, double bearingDeg)
    {
        const double earthRadiusKm = 6371.0088;
        var bearing = bearingDeg * Math.PI / 180;
        var latRad = lat * Math.PI / 180;
        var lonRad = lon * Math.PI / 180;
        var angularDistance = distanceKm / earthRadiusKm;

        var newLatRad = Math.Asin(
            (Math.Sin(latRad) * Math.Cos(angularDistance)) +
            (Math.Cos(latRad) * Math.Sin(angularDistance) * Math.Cos(bearing)));
        var newLonRad = lonRad + Math.Atan2(
            Math.Sin(bearing) * Math.Sin(angularDistance) * Math.Cos(latRad),
            Math.Cos(angularDistance) - (Math.Sin(latRad) * Math.Sin(newLatRad)));

        return (newLatRad * 180 / Math.PI, newLonRad * 180 / Math.PI);
    }
}
