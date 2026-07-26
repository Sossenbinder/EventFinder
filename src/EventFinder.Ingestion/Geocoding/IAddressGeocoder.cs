using EventFinder.Core;

namespace EventFinder.Ingestion.Geocoding;

public sealed record AddressGeocodeResult(double Latitude, double Longitude, LocationPrecision Precision);

// Address-level geocoding for events whose raw data includes a street
// address, as opposed to Gazetteer's town-centroid lookup. Implementations
// must never throw for network/parse failures -- geocoding degrading to the
// existing gazetteer cascade is the whole contract (IngestionRunner does not
// wrap calls to this interface in its own try/catch).
public interface IAddressGeocoder
{
    Task<AddressGeocodeResult?> GeocodeAsync(string venueAddress, string? postalCode, string? city, CancellationToken ct);
}
