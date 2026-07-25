using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EventFinder.Data;

// EF Core's SQLite provider stores DateTime as ISO8601 text by default but
// does not round-trip DateTimeKind, so reads come back Unspecified. Forcing
// Utc on both write and read keeps every timestamp in this store on one
// consistent representation -- FlatLens mixed ISO8601 text and Unix-ms
// across its DateTime columns, which is exactly the defect this avoids.
public sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

public sealed class UtcNullableDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
    v => v == null ? null : (v.Value.Kind == DateTimeKind.Utc ? v.Value : v.Value.ToUniversalTime()),
    v => v == null ? null : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc));
