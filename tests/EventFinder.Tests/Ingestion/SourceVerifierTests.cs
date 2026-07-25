using System.Net;
using EventFinder.Ingestion;
using EventFinder.Ingestion.Contracts;
using EventFinder.Ingestion.Http;
using FluentAssertions;

namespace EventFinder.Tests.Ingestion;

internal sealed class RawEventReturningSource(string type, IReadOnlyList<RawEvent> events) : IEventSource
{
    public string Type => type;

    public Task<IReadOnlyList<RawEvent>> FetchAsync(SourceDescriptor source, CancellationToken ct) => Task.FromResult(events);
}

internal sealed class HttpErrorSource(string type, HttpStatusCode status) : IEventSource
{
    public string Type => type;

    public Task<IReadOnlyList<RawEvent>> FetchAsync(SourceDescriptor source, CancellationToken ct) =>
        throw new SourceHttpErrorException(status, source.Url);
}

internal sealed class UnreachableSource(string type) : IEventSource
{
    public string Type => type;

    public Task<IReadOnlyList<RawEvent>> FetchAsync(SourceDescriptor source, CancellationToken ct) =>
        throw new SourceUnreachableException("DNS lookup failed");
}

public sealed class SourceVerifierTests
{
    [Fact]
    public async Task VerifyAsync_ReachableSource_ReportsEventCountAndNoParseError()
    {
        var events = new List<RawEvent>
        {
            new() { SourceEventId = "1", Title = "A", Start = DateTimeOffset.UtcNow, Url = "https://example.test/1" },
        };
        var sourcesByType = new Dictionary<string, IEventSource> { ["good"] = new RawEventReturningSource("good", events) };
        var verifier = new SourceVerifier(sourcesByType);
        var descriptor = new SourceDescriptor { Id = "good-source", Org = "Good", Type = "good", Url = "https://example.test" };

        var results = await verifier.VerifyAsync([descriptor], CancellationToken.None);

        var result = results.Should().ContainSingle().Subject;
        result.Reachable.Should().BeTrue();
        result.HttpStatus.Should().Be(200);
        result.EventCount.Should().Be(1);
        result.FirstParseError.Should().BeNull();
    }

    // A source that fetches fine and legitimately has nothing upcoming (a
    // quiet group over the summer, say) must verify as a pass: only
    // unreachable/non-2xx/parse-error outcomes are failures, never an empty
    // result on its own.
    [Fact]
    public async Task VerifyAsync_ReachableWithZeroEvents_StillReportsAsSuccessNotFailure()
    {
        var sourcesByType = new Dictionary<string, IEventSource> { ["good"] = new RawEventReturningSource("good", []) };
        var verifier = new SourceVerifier(sourcesByType);
        var descriptor = new SourceDescriptor { Id = "quiet-source", Org = "Quiet", Type = "good", Url = "https://example.test" };

        var results = await verifier.VerifyAsync([descriptor], CancellationToken.None);

        var result = results.Should().ContainSingle().Subject;
        result.Reachable.Should().BeTrue();
        result.HttpStatus.Should().Be(200);
        result.EventCount.Should().Be(0);
        result.FirstParseError.Should().BeNull();
    }

    [Fact]
    public async Task VerifyAsync_HttpErrorStatus_ReportsReachableWithTheStatusCode()
    {
        var sourcesByType = new Dictionary<string, IEventSource> { ["bad"] = new HttpErrorSource("bad", HttpStatusCode.NotFound) };
        var verifier = new SourceVerifier(sourcesByType);
        var descriptor = new SourceDescriptor { Id = "bad-source", Org = "Bad", Type = "bad", Url = "https://example.test" };

        var results = await verifier.VerifyAsync([descriptor], CancellationToken.None);

        var result = results.Should().ContainSingle().Subject;
        result.Reachable.Should().BeTrue();
        result.HttpStatus.Should().Be(404);
        result.EventCount.Should().Be(0);
    }

    [Fact]
    public async Task VerifyAsync_UnreachableSource_ReportsNotReachable()
    {
        var sourcesByType = new Dictionary<string, IEventSource> { ["down"] = new UnreachableSource("down") };
        var verifier = new SourceVerifier(sourcesByType);
        var descriptor = new SourceDescriptor { Id = "down-source", Org = "Down", Type = "down", Url = "https://example.test" };

        var results = await verifier.VerifyAsync([descriptor], CancellationToken.None);

        var result = results.Should().ContainSingle().Subject;
        result.Reachable.Should().BeFalse();
        result.HttpStatus.Should().BeNull();
        result.FirstParseError.Should().Contain("DNS lookup failed");
    }
}
