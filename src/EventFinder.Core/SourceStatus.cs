namespace EventFinder.Core;

public sealed class SourceStatus
{
    public required string SourceId { get; init; }
    public DateTime? LastRunUtc { get; set; }
    public DateTime? LastSuccessUtc { get; set; }
    public int EventCount { get; set; }
    public string? LastError { get; set; }
}
