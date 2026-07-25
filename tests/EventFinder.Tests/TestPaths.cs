namespace EventFinder.Tests;

internal static class TestPaths
{
    public static string RepoRoot { get; } = FindRepoRoot();

    public static string PlacesCsv => Path.Combine(RepoRoot, "data", "places-de.csv");

    public static string PostalCsv => Path.Combine(RepoRoot, "data", "postal-de.csv");

    public static string FixturesDir => Path.Combine(RepoRoot, "tests", "EventFinder.Tests", "Fixtures");

    public static string Fixture(params string[] relativeParts) => Path.Combine([FixturesDir, .. relativeParts]);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EventFinder.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName
            ?? throw new InvalidOperationException($"Could not locate EventFinder.slnx above {AppContext.BaseDirectory}");
    }
}
