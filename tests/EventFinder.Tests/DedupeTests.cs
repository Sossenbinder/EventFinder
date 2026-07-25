using EventFinder.Core;
using FluentAssertions;

namespace EventFinder.Tests;

public class DedupeTests
{
    [Fact]
    public void ComputeKey_SameEventFromTwoSources_ProducesTheSameKey()
    {
        var start = new DateTime(2026, 9, 10, 18, 0, 0, DateTimeKind.Utc);

        // Titles reach Dedupe already cleaned by Normalization.CleanTitle;
        // ComputeKey itself only folds case/diacritics, so two sources
        // differing just in casing/umlaut spelling must still collapse.
        var keyFromBevy = Dedupe.ComputeKey("DotNet User Group Stuttgart", start, "Europe/Berlin", "Stuttgart");
        var keyFromIcs = Dedupe.ComputeKey("dotnet user group stuttgart", start, "Europe/Berlin", "Stuttgart");

        keyFromBevy.Should().Be(keyFromIcs);
    }

    [Fact]
    public void ComputeKey_SameTitleOnDifferentDays_ProducesDifferentKeys()
    {
        var day1 = new DateTime(2026, 9, 10, 18, 0, 0, DateTimeKind.Utc);
        var day2 = new DateTime(2026, 9, 11, 18, 0, 0, DateTimeKind.Utc);

        var key1 = Dedupe.ComputeKey("DotNet User Group Stuttgart", day1, "Europe/Berlin", "Stuttgart");
        var key2 = Dedupe.ComputeKey("DotNet User Group Stuttgart", day2, "Europe/Berlin", "Stuttgart");

        key1.Should().NotBe(key2);
    }

    [Fact]
    public void ComputeKey_NearUtcMidnight_UsesTheEventsLocalCalendarDay()
    {
        // 22:30 UTC on 2026-06-10 is 00:30 CEST on 2026-06-11 in Europe/Berlin;
        // the key must reflect the local day, not the UTC day.
        var start = new DateTime(2026, 6, 10, 22, 30, 0, DateTimeKind.Utc);

        var key = Dedupe.ComputeKey("Late Show", start, "Europe/Berlin", "Stuttgart");

        key.Should().Contain("2026-06-11");
        key.Should().NotContain("2026-06-10");
    }
}
