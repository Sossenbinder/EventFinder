using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EventFinder.Data;

// Used only by `dotnet ef migrations add` at design time; the API host
// (workstream 3) wires up the real connection string via DI.
public sealed class EventFinderDbContextFactory : IDesignTimeDbContextFactory<EventFinderDbContext>
{
    public EventFinderDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<EventFinderDbContext>()
            .UseSqlite("Data Source=eventfinder.db")
            .Options;
        return new EventFinderDbContext(options);
    }
}
