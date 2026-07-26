using EventFinder.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace EventFinder.Data;

public sealed class EventFinderDbContext(DbContextOptions<EventFinderDbContext> options) : DbContext(options)
{
    // Unit Separator (0x1F): joins Tags into one TEXT column. Cannot appear
    // in a normal tag string, unlike '|' or ','.
    private const char TagSeparator = (char)0x1F;

    public DbSet<Event> Events => Set<Event>();
    public DbSet<SourceStatus> SourceStatuses => Set<SourceStatus>();
    public DbSet<GeocodeCacheEntry> GeocodeCacheEntries => Set<GeocodeCacheEntry>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<UtcNullableDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Event>(b =>
        {
            b.HasKey(e => e.Id);
            b.Property(e => e.SourceId).IsRequired().HasMaxLength(100);
            b.Property(e => e.SourceEventId).IsRequired().HasMaxLength(200);
            b.Property(e => e.Title).IsRequired();
            b.Property(e => e.TimeZoneId).IsRequired();
            b.Property(e => e.Url).IsRequired();
            b.Property(e => e.DedupeKey).IsRequired();

            b.HasIndex(e => new { e.SourceId, e.SourceEventId }).IsUnique();
            b.HasIndex(e => e.DedupeKey);
            b.HasIndex(e => e.StartUtc);

            var tagsComparer = new ValueComparer<IReadOnlyList<string>>(
                (a, b2) => (a ?? Array.Empty<string>()).SequenceEqual(b2 ?? Array.Empty<string>()),
                v => v.Aggregate(0, (hash, tag) => HashCode.Combine(hash, tag.GetHashCode(StringComparison.Ordinal))),
                v => v.ToList());

            b.Property(e => e.Tags)
                .HasConversion(
                    v => string.Join(TagSeparator, v),
                    v => v.Length == 0 ? Array.Empty<string>() : v.Split(TagSeparator))
                .Metadata.SetValueComparer(tagsComparer);
        });

        modelBuilder.Entity<SourceStatus>(b =>
        {
            b.HasKey(s => s.SourceId);
            b.Property(s => s.SourceId).HasMaxLength(100);
        });

        modelBuilder.Entity<GeocodeCacheEntry>(b =>
        {
            b.HasKey(e => e.Id);
            b.Property(e => e.Query).IsRequired();
            b.HasIndex(e => e.Query).IsUnique();
        });
    }
}
