using EventFinder.Core;
using EventFinder.Data;
using EventFinder.Ingestion;
using EventFinder.Ingestion.Contracts;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EventFinder.Tests.Ingestion;

internal sealed class FakeEventSource(string type, Func<SourceDescriptor, IReadOnlyList<RawEvent>> fetch) : IEventSource
{
    public string Type => type;

    public Task<IReadOnlyList<RawEvent>> FetchAsync(SourceDescriptor source, CancellationToken ct) =>
        Task.FromResult(fetch(source));
}

internal sealed class ThrowingEventSource(string type) : IEventSource
{
    public string Type => type;

    public Task<IReadOnlyList<RawEvent>> FetchAsync(SourceDescriptor source, CancellationToken ct) =>
        throw new InvalidOperationException("adapter exploded");
}

public sealed class IngestionRunnerTests : IDisposable
{
    private const double StuttgartLat = 48.78232;
    private const double StuttgartLon = 9.17702;
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Gazetteer Gazetteer = Gazetteer.Load(TestPaths.PlacesCsv, TestPaths.PostalCsv);

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<EventFinderDbContext> _options;

    public IngestionRunnerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<EventFinderDbContext>().UseSqlite(_connection).Options;
        using var ctx = new EventFinderDbContext(_options);
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task RunAsync_OneSourceThrows_OtherSourceStillSucceedsAndIsStored()
    {
        await using var ctx = new EventFinderDbContext(_options);
        var store = new EventStore(ctx);
        var sourcesByType = new Dictionary<string, IEventSource>
        {
            ["good"] = new FakeEventSource("good", _ => [MakeRawEvent("evt-1", "Working Source Meetup")]),
            ["bad"] = new ThrowingEventSource("bad"),
        };
        var runner = new IngestionRunner(sourcesByType, store, Gazetteer, timeProvider: new FixedTimeProvider(FixedNow));
        var descriptors = new[]
        {
            new SourceDescriptor { Id = "good-source", Org = "Good", Type = "good", Url = "https://example.test/good" },
            new SourceDescriptor { Id = "bad-source", Org = "Bad", Type = "bad", Url = "https://example.test/bad" },
        };

        var statuses = await runner.RunAsync(descriptors, CancellationToken.None);

        statuses["bad-source"].LastError.Should().Contain("adapter exploded");
        statuses["good-source"].LastError.Should().BeNull();
        statuses["good-source"].EventCount.Should().Be(1);

        var stored = await ctx.Events.ToListAsync(CancellationToken.None);
        stored.Should().ContainSingle(e => e.SourceId == "good-source" && e.SourceEventId == "evt-1");
    }

    [Fact]
    public async Task RunAsync_SourceFailsOnASubsequentRun_PreviouslyStoredDataFromThatSourceIsUntouched()
    {
        await using var ctx = new EventFinderDbContext(_options);
        var store = new EventStore(ctx);
        var flaky = new ToggleableEventSource("flaky");
        flaky.EventsToReturn = [MakeRawEvent("evt-1", "Flaky Source Meetup")];
        var sourcesByType = new Dictionary<string, IEventSource> { ["flaky"] = flaky };
        var runner = new IngestionRunner(sourcesByType, store, Gazetteer, timeProvider: new FixedTimeProvider(FixedNow));
        var descriptors = new[]
        {
            new SourceDescriptor { Id = "flaky-source", Org = "Flaky", Type = "flaky", Url = "https://example.test/flaky" },
        };

        var firstRun = await runner.RunAsync(descriptors, CancellationToken.None);
        firstRun["flaky-source"].LastError.Should().BeNull();
        firstRun["flaky-source"].EventCount.Should().Be(1);

        flaky.ShouldThrow = true;
        var secondRun = await runner.RunAsync(descriptors, CancellationToken.None);

        secondRun["flaky-source"].LastError.Should().NotBeNullOrEmpty();

        var stored = await ctx.Events.ToListAsync(CancellationToken.None);
        stored.Should().ContainSingle(e => e.SourceId == "flaky-source" && e.SourceEventId == "evt-1" && e.Title == "Flaky Source Meetup");
    }

    [Fact]
    public async Task RunAsync_DisabledSource_IsNeverFetched()
    {
        await using var ctx = new EventFinderDbContext(_options);
        var store = new EventStore(ctx);
        var wasCalled = false;
        var sourcesByType = new Dictionary<string, IEventSource>
        {
            ["good"] = new FakeEventSource("good", _ => { wasCalled = true; return []; }),
        };
        var runner = new IngestionRunner(sourcesByType, store, Gazetteer, timeProvider: new FixedTimeProvider(FixedNow));
        var descriptors = new[]
        {
            new SourceDescriptor { Id = "disabled-source", Org = "Good", Type = "good", Url = "https://example.test/x", Enabled = false },
        };

        var statuses = await runner.RunAsync(descriptors, CancellationToken.None);

        wasCalled.Should().BeFalse();
        statuses.Should().BeEmpty();
    }

    private static RawEvent MakeRawEvent(string sourceEventId, string title) => new()
    {
        SourceEventId = sourceEventId,
        Title = title,
        Start = FixedNow.AddDays(7),
        TimeZoneId = "Europe/Berlin",
        Latitude = StuttgartLat,
        Longitude = StuttgartLon,
        City = "Stuttgart",
        Url = $"https://example.test/{sourceEventId}",
    };
}

internal sealed class ToggleableEventSource(string type) : IEventSource
{
    public string Type => type;
    public bool ShouldThrow { get; set; }
    public IReadOnlyList<RawEvent> EventsToReturn { get; set; } = [];

    public Task<IReadOnlyList<RawEvent>> FetchAsync(SourceDescriptor source, CancellationToken ct)
    {
        if (ShouldThrow)
        {
            throw new InvalidOperationException("adapter now failing");
        }
        return Task.FromResult(EventsToReturn);
    }
}
