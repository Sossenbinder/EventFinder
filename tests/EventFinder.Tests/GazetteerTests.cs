using EventFinder.Core;
using FluentAssertions;

namespace EventFinder.Tests;

public class GazetteerTests
{
    private static readonly Gazetteer Sut = Gazetteer.Load(TestPaths.PlacesCsv, TestPaths.PostalCsv);

    [Fact]
    public void Resolve_CityName_KirchheimUnterTeck_ResolvesToKnownCoordinates()
    {
        var result = Sut.Resolve(null, null, null, null, "Kirchheim unter Teck");

        result.Status.Should().Be(LocationStatus.Resolved);
        result.Latitude.Should().BeApproximately(48.6468, 0.01);
        result.Longitude.Should().BeApproximately(9.4538, 0.01);
    }

    [Fact]
    public void Resolve_PostalCode73230_ResolvesToKirchheimUnterTeck()
    {
        var result = Sut.Resolve(null, null, "73230", null, null);

        result.Status.Should().Be(LocationStatus.Resolved);
        result.Latitude.Should().BeApproximately(48.6468, 0.01);
        result.Longitude.Should().BeApproximately(9.4538, 0.01);
    }

    [Fact]
    public void Resolve_PostalCodeInAddressString_IsExtractedAndResolved()
    {
        var result = Sut.Resolve(null, null, null, "Marktplatz 1, 73230 Kirchheim unter Teck", null);

        result.Status.Should().Be(LocationStatus.Resolved);
        result.Latitude.Should().BeApproximately(48.6468, 0.01);
    }

    [Fact]
    public void Resolve_StandaloneTokenBer_DoesNotMatchBerlinViaThreeLetterAlias()
    {
        // 'BER' is a real GeoNames alias for Berlin but shorter than the
        // length >= 4 cutoff, precisely so a standalone "ber" token elsewhere
        // in a venue string can't falsely resolve to Berlin.
        var result = Sut.Resolve(null, null, null, null, "Meet us at the ber conference room");

        result.Status.Should().Be(LocationStatus.Unresolved);
    }

    [Fact]
    public void Resolve_UmlautExpandedAlias_FindsDuesseldorf()
    {
        // Umlaut/ss-expansion folding: "Duesseldorf" must fold to the same
        // key as "Düsseldorf" so venue strings typed without umlauts resolve.
        var result = Sut.Resolve(null, null, null, null, "Duesseldorf");

        result.Status.Should().Be(LocationStatus.Resolved);
        result.MatchedPlace.Should().Be("Düsseldorf");
        result.Latitude.Should().BeApproximately(51.22319, 0.001);
    }

    [Theory]
    [InlineData("München")]
    [InlineData("Muenchen")]
    [InlineData("Munich")]
    public void Resolve_MunichUnderAnySpelling_ResolvesToTheRealCity(string spelling)
    {
        // GeoNames' primary name column carries the English exonym for some German cities
        // (Munich, Nuremberg) and the German name for others (Köln). The gazetteer build takes the
        // canonical name from the preferred German alternate name and keeps the exonym as an alias,
        // so all three spellings must land on the Bavarian city rather than a same-named hamlet.
        var result = Sut.Resolve(null, null, null, null, spelling);

        result.Status.Should().Be(LocationStatus.Resolved);
        result.MatchedPlace.Should().Be("München");
        result.Latitude.Should().BeApproximately(48.13743, 0.01);
        result.Longitude.Should().BeApproximately(11.57549, 0.01);
    }

    [Fact]
    public void Search_RanksMatchesByPopulationDescending()
    {
        var results = Sut.Search("berlin", 5);

        results.Should().NotBeEmpty();
        results[0].Name.Should().Be("Berlin");
    }
}
