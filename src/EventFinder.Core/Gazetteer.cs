using System.Globalization;
using System.Text.RegularExpressions;

namespace EventFinder.Core;

public sealed record PlaceRecord(string Name, string Admin1, long Population, double Latitude, double Longitude);

public readonly record struct GeoResolution(double? Latitude, double? Longitude, string? MatchedPlace, LocationStatus Status)
{
    public static readonly GeoResolution Unresolved = new(null, null, null, LocationStatus.Unresolved);
}

// Offline German gazetteer over data/places-de.csv and data/postal-de.csv
// (see AGENTS.md's Data sources ledger for provenance). Loads once into
// in-memory dictionaries; all lookups afterwards are pure and network-free.
public sealed partial class Gazetteer
{
    private readonly Dictionary<string, PlaceRecord> _byFoldedName;
    private readonly Dictionary<string, (string Name, double Lat, double Lon)> _byPlz;
    private readonly List<(string Folded, PlaceRecord Place)> _placesByPopulationDesc;
    private readonly int _maxNameWords;

    private Gazetteer(
        Dictionary<string, PlaceRecord> byFoldedName,
        Dictionary<string, (string Name, double Lat, double Lon)> byPlz,
        List<(string Folded, PlaceRecord Place)> placesByPopulationDesc)
    {
        _byFoldedName = byFoldedName;
        _byPlz = byPlz;
        _placesByPopulationDesc = placesByPopulationDesc;
        _maxNameWords = byFoldedName.Count == 0
            ? 1
            : byFoldedName.Keys.Max(k => Math.Max(1, Normalization.Tokenize(k).Count));
    }

    public static Gazetteer Load(string placesCsvPath, string postalCsvPath)
    {
        var byFoldedName = new Dictionary<string, PlaceRecord>();
        var places = new List<PlaceRecord>();
        LoadPlaces(placesCsvPath, byFoldedName, places);

        var byPlz = new Dictionary<string, (string, double, double)>();
        LoadPostal(postalCsvPath, byPlz);

        var indexed = places.Select(p => (Normalization.Fold(p.Name), p)).ToList();
        indexed.Sort((a, b) => b.p.Population.CompareTo(a.p.Population));

        return new Gazetteer(byFoldedName, byPlz, indexed);
    }

    // Resolution cascade: explicit coordinates -> PLZ found in the address ->
    // folded full-token place-name match -> unresolved. Never guesses past
    // that point; callers keep unresolved events, they don't drop them.
    public GeoResolution Resolve(double? latitude, double? longitude, string? postalCode, string? venueAddress, string? city)
    {
        if (latitude is not null && longitude is not null)
        {
            return new GeoResolution(latitude, longitude, city, LocationStatus.Resolved);
        }

        var plz = ExtractPostalCode(postalCode) ?? ExtractPostalCode(venueAddress);
        if (plz is not null && _byPlz.TryGetValue(plz, out var postal))
        {
            return new GeoResolution(postal.Lat, postal.Lon, postal.Name, LocationStatus.Resolved);
        }

        return TryMatchByName(city) ?? TryMatchByName(venueAddress) ?? GeoResolution.Unresolved;
    }

    public IReadOnlyList<PlaceRecord> Search(string query, int limit)
    {
        var folded = Normalization.Fold(query);
        if (folded.Length == 0)
        {
            return [];
        }

        var results = new List<PlaceRecord>(Math.Min(limit, 16));
        foreach (var (name, place) in _placesByPopulationDesc)
        {
            if (results.Count >= limit)
            {
                break;
            }
            if (name.Contains(folded, StringComparison.Ordinal))
            {
                results.Add(place);
            }
        }
        return results;
    }

    private GeoResolution? TryMatchByName(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var tokens = Normalization.Tokenize(Normalization.Fold(text));
        if (tokens.Count == 0)
        {
            return null;
        }

        // Try the longest token phrases first so "Kirchheim unter Teck" wins
        // over a single-token alias match on "Kirchheim".
        for (var len = Math.Min(_maxNameWords, tokens.Count); len >= 1; len--)
        {
            for (var start = 0; start + len <= tokens.Count; start++)
            {
                var phrase = string.Join(' ', tokens.Skip(start).Take(len));
                if (_byFoldedName.TryGetValue(phrase, out var place))
                {
                    return new GeoResolution(place.Latitude, place.Longitude, place.Name, LocationStatus.Resolved);
                }
            }
        }

        return null;
    }

    private static string? ExtractPostalCode(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }
        var match = PostalCodePattern().Match(text);
        return match.Success ? match.Value : null;
    }

    private static void LoadPlaces(string path, Dictionary<string, PlaceRecord> byFoldedName, List<PlaceRecord> places)
    {
        using var reader = new StreamReader(path);
        reader.ReadLine(); // header: name;aliases;admin1;population;lat;lon
        Span<Range> fields = stackalloc Range[6];
        Span<Range> aliasRanges = stackalloc Range[8];
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0)
            {
                continue;
            }
            var span = line.AsSpan();
            if (span.Split(fields, ';') < 6)
            {
                continue;
            }

            var name = span[fields[0]].ToString();
            var aliasesField = span[fields[1]];
            var admin1 = span[fields[2]].ToString();
            var population = long.Parse(span[fields[3]], CultureInfo.InvariantCulture);
            var lat = double.Parse(span[fields[4]], CultureInfo.InvariantCulture);
            var lon = double.Parse(span[fields[5]], CultureInfo.InvariantCulture);

            var place = new PlaceRecord(name, admin1, population, lat, lon);
            places.Add(place);
            AddByHighestPopulation(byFoldedName, Normalization.Fold(name), place);

            if (aliasesField.IsEmpty)
            {
                continue;
            }
            var aliasCount = aliasesField.Split(aliasRanges, '|');
            for (var i = 0; i < aliasCount; i++)
            {
                var alias = aliasesField[aliasRanges[i]];
                if (alias.IsEmpty)
                {
                    continue;
                }
                // GeoNames ships 3-letter codes (e.g. 'BER' for Berlin) among
                // its aliases; only length >= 4 is a trustworthy full-token
                // match, otherwise any string containing "ber" would resolve.
                var foldedAlias = Normalization.Fold(alias.ToString());
                if (foldedAlias.Length >= 4)
                {
                    AddByHighestPopulation(byFoldedName, foldedAlias, place);
                }
            }
        }
    }

    private static void LoadPostal(string path, Dictionary<string, (string Name, double Lat, double Lon)> byPlz)
    {
        using var reader = new StreamReader(path);
        reader.ReadLine(); // header: plz;name;admin1;lat;lon
        Span<Range> fields = stackalloc Range[5];
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0)
            {
                continue;
            }
            var span = line.AsSpan();
            if (span.Split(fields, ';') < 5)
            {
                continue;
            }

            var plz = span[fields[0]].ToString();
            var name = span[fields[1]].ToString();
            var lat = double.Parse(span[fields[3]], CultureInfo.InvariantCulture);
            var lon = double.Parse(span[fields[4]], CultureInfo.InvariantCulture);
            byPlz.TryAdd(plz, (name, lat, lon));
        }
    }

    private static void AddByHighestPopulation(Dictionary<string, PlaceRecord> dict, string key, PlaceRecord candidate)
    {
        if (!dict.TryGetValue(key, out var existing) || candidate.Population > existing.Population)
        {
            dict[key] = candidate;
        }
    }

    [GeneratedRegex(@"(?<!\d)\d{5}(?!\d)")]
    private static partial Regex PostalCodePattern();
}
