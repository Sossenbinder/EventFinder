using EventFinder.Core;
using FluentAssertions;

namespace EventFinder.Tests;

public sealed class NormalizationTests
{
    [Theory]
    [InlineData("Intro to Kubernetes and Docker", "cloud")]
    [InlineData("Machine Learning Grundlagen", "ai")]
    [InlineData("KI im Unternehmen", "ai")]
    [InlineData("Java User Group Meetup", "java")]
    [InlineData("Kotlin Koans Night", "java")]
    [InlineData("OWASP Security Night", "security")]
    [InlineData("DevOps and CI-CD Pipelines", "devops")]
    [InlineData("Rust Systems Programming", "systems")]
    [InlineData("Golang Stammtisch", "systems")]
    [InlineData("Agile Architektur Meetup", "practice")]
    [InlineData("UX Design Basics", "design")]
    [InlineData("IoT and Robotik Workshop", "hardware")]
    [InlineData("Hackergarten: Open Source Night", "opensource")]
    [InlineData("Data Science mit Python", "data")]
    public void ExtractTags_MapsKeywordToExpectedTag(string title, string expectedTag)
    {
        var tags = Normalization.ExtractTags(title, description: null, Normalization.DefaultKeywordToTag);

        tags.Should().Contain(expectedTag);
    }

    // ".net", "c#", "c++" and "ci-cd" all contain characters Tokenize() splits
    // on, so they can never appear as a token of the document text -- only a
    // plain substring search finds them. This is the regression test for that
    // fix in ExtractTags.
    [Theory]
    [InlineData("Building APIs with .NET 9", "dotnet")]
    [InlineData("C# for Java Developers", "dotnet")]
    [InlineData("Modern C++ Patterns", "systems")]
    public void ExtractTags_KeywordsContainingSymbols_StillMatch(string title, string expectedTag)
    {
        var tags = Normalization.ExtractTags(title, description: null, Normalization.DefaultKeywordToTag);

        tags.Should().Contain(expectedTag);
    }

    // Regression: the real "66. Hackergarten Stuttgart" description links to
    // hackergarten.net, which an unbounded substring search read as ".net" and
    // tagged dotnet. Symbol keywords must still respect word boundaries.
    [Fact]
    public void ExtractTags_SymbolKeyword_DoesNotMatchInsideADomainName()
    {
        var tags = Normalization.ExtractTags(
            "66. Hackergarten Stuttgart",
            "Wir treffen uns wieder. Siehe auch: http://hackergarten.net/",
            Normalization.DefaultKeywordToTag);

        tags.Should().NotContain("dotnet");
        tags.Should().Contain("opensource");
    }

    // "go" was removed from the keyword map: it fires on ordinary prose far
    // more often than on the language.
    [Fact]
    public void ExtractTags_OrdinaryProse_DoesNotPickUpSystemsTag()
    {
        var tags = Normalization.ExtractTags(
            "OWASP Stuttgart Chapter Stammtisch", "Let's go to the venue together.", Normalization.DefaultKeywordToTag);

        tags.Should().NotContain("systems");
        tags.Should().Contain("security");
    }

    // The whole point of the token-equality fast path: "java" must not fire
    // just because "javascript" contains it as a substring.
    [Fact]
    public void ExtractTags_SingleWordKeyword_DoesNotMatchInsideALongerWord()
    {
        var tags = Normalization.ExtractTags("JavaScript Frameworks Night", description: null, Normalization.DefaultKeywordToTag);

        tags.Should().NotContain("java");
        tags.Should().Contain("frontend");
    }

    [Fact]
    public void ExtractTags_MultipleKeywordsMatch_ReturnsDeduplicatedSortedTags()
    {
        var tags = Normalization.ExtractTags(
            "Kubernetes and Docker for Java Developers", "We'll also touch on K8s basics.", Normalization.DefaultKeywordToTag);

        tags.Should().BeEquivalentTo(["cloud", "java"]);
        tags.Should().BeInAscendingOrder(StringComparer.Ordinal);
    }

    [Fact]
    public void ExtractTags_NoKeywordsMatch_ReturnsEmpty()
    {
        var tags = Normalization.ExtractTags("Quarterly Board Game Night", description: null, Normalization.DefaultKeywordToTag);

        tags.Should().BeEmpty();
    }
}
