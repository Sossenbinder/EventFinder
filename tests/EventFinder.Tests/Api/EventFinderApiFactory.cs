using EventFinder.Core;
using EventFinder.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EventFinder.Tests.Api;

// WebApplicationFactory around the real Program (see Program.cs's trailing
// `public partial class Program;`), pointed at a throwaway SQLite file and
// fetch-cache directory per factory instance. Ingestion is disabled so tests
// control exactly what's in the store; the Gazetteer and sources.yaml paths
// are redirected to the repo's real files (TestPaths) rather than whatever a
// test project's own output directory happens to contain.
public sealed class EventFinderApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"eventfinder-tests-{Guid.NewGuid():N}");

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_tempDir);

        // Force host creation now (Services is lazy) so the DB is migrated
        // before any test seeds data into it.
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EventFinderDbContext>();
        await db.Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup; a leftover temp dir is harmless.
        }
    }

    public async Task SeedEventsAsync(string sourceId, params Event[] events)
    {
        using var scope = Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<EventStore>();
        await store.UpsertAsync(events, sourceId, CancellationToken.None);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Path"] = Path.Combine(_tempDir, "eventfinder-test.db"),
            ["Data:Directory"] = Path.Combine(_tempDir, "data"),
            ["Data:GazetteerPlacesCsv"] = TestPaths.PlacesCsv,
            ["Data:GazetteerPostalCsv"] = TestPaths.PostalCsv,
            ["Data:SourcesFile"] = Path.Combine(TestPaths.RepoRoot, "sources.yaml"),
            ["Ingestion:Enabled"] = "false",
        }));
    }
}
