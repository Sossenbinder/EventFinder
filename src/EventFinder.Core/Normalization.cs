using System.Globalization;
using System.Text;

namespace EventFinder.Core;

public static class Normalization
{
    // Common lead-ins that source sites prepend to titles/descriptions and add
    // nothing once the event is pulled out of its original page context.
    private static readonly string[] DefaultBoilerplatePrefixes =
    [
        "about this event",
        "please join us for",
        "join us for",
        "you're invited to",
        "save the date:",
    ];

    public static string CleanTitle(string raw) =>
        StripBoilerplatePrefixes(CollapseWhitespace(raw), DefaultBoilerplatePrefixes);

    public static string CleanDescription(string? raw) =>
        raw is null ? string.Empty : StripBoilerplatePrefixes(CollapseWhitespace(raw), DefaultBoilerplatePrefixes);

    public static string CollapseWhitespace(string text)
    {
        var span = text.AsSpan().Trim();
        var builder = new StringBuilder(span.Length);
        var lastWasSpace = false;
        foreach (var c in span)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace)
                {
                    builder.Append(' ');
                }
                lastWasSpace = true;
            }
            else
            {
                builder.Append(c);
                lastWasSpace = false;
            }
        }
        return builder.ToString();
    }

    public static string StripBoilerplatePrefixes(string text, IReadOnlyList<string> prefixes)
    {
        foreach (var prefix in prefixes)
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return CollapseWhitespace(text[prefix.Length..].TrimStart(':', '-', ' '));
            }
        }
        return text;
    }

    // Mirrors scripts/build_gazetteer.py's fold(): casefold + German umlaut
    // expansion + diacritic strip. Dedupe keys and gazetteer lookups must use
    // the exact same folding as the CSV generator, or "München" and "Muenchen"
    // stop being the same key.
    public static string Fold(string text)
    {
        var lowered = text.ToLowerInvariant();
        var expanded = lowered
            .Replace("ä", "ae", StringComparison.Ordinal)
            .Replace("ö", "oe", StringComparison.Ordinal)
            .Replace("ü", "ue", StringComparison.Ordinal)
            .Replace("ß", "ss", StringComparison.Ordinal);
        var normalized = expanded.Normalize(NormalizationForm.FormKD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }
        return builder.ToString();
    }

    public static List<string> Tokenize(string foldedText)
    {
        var tokens = new List<string>();
        var start = -1;
        for (var i = 0; i <= foldedText.Length; i++)
        {
            var isTokenChar = i < foldedText.Length && char.IsLetterOrDigit(foldedText[i]);
            if (isTokenChar)
            {
                start = start < 0 ? i : start;
            }
            else if (start >= 0)
            {
                tokens.Add(foldedText[start..i]);
                start = -1;
            }
        }
        return tokens;
    }

    public static IReadOnlyList<string> ExtractTags(
        string title, string? description, IReadOnlyDictionary<string, string> keywordToTag)
    {
        var foldedText = Fold($"{title} {description}");
        var tokens = new HashSet<string>(Tokenize(foldedText), StringComparer.Ordinal);
        var tags = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var (keyword, tag) in keywordToTag)
        {
            var foldedKeyword = Fold(keyword);

            // A keyword only qualifies for the fast token-equality path when it
            // folds down to exactly the single alnum word it already is (e.g.
            // "java"). That is what stops it from also matching inside
            // "javascript". Anything else -- a phrase ("cloud native") or a
            // keyword containing punctuation Tokenize() would split on
            // (".net", "c#", "c++", "ci-cd") -- can never appear as a token of
            // its own, so it must fall back to a plain substring search over
            // the untokenized text instead.
            var keywordTokens = Tokenize(foldedKeyword);
            var isCleanSingleToken = keywordTokens.Count == 1 && keywordTokens[0] == foldedKeyword;

            var matches = isCleanSingleToken
                ? tokens.Contains(foldedKeyword)
                : ContainsBounded(foldedText, foldedKeyword);
            if (matches)
            {
                tags.Add(tag);
            }
        }

        return [.. tags];
    }

    // Substring search that still requires word boundaries on both sides. A raw
    // Contains() tagged "66. Hackergarten Stuttgart" as ".net" because its
    // description links to hackergarten.net; requiring the character before the
    // match to be a non-alphanumeric keeps ".NET 10" while rejecting a domain
    // suffix.
    private static bool ContainsBounded(string foldedText, string foldedKeyword)
    {
        var from = 0;
        while (from <= foldedText.Length - foldedKeyword.Length)
        {
            var at = foldedText.IndexOf(foldedKeyword, from, StringComparison.Ordinal);
            if (at < 0)
            {
                return false;
            }

            var end = at + foldedKeyword.Length;
            var boundedLeft = at == 0 || !char.IsLetterOrDigit(foldedText[at - 1]);
            var boundedRight = end == foldedText.Length || !char.IsLetterOrDigit(foldedText[end]);
            if (boundedLeft && boundedRight)
            {
                return true;
            }

            from = at + 1;
        }

        return false;
    }

    // Content judgment for what counts as e.g. "ai" or "cloud" -- curated
    // German+English keyword pairs per the outline's tag taxonomy. Matched
    // case-insensitively (via Fold) against title+description by ExtractTags
    // above. A single event may pick up several tags; IngestionRunner
    // de-duplicates and sorts them.
    public static readonly IReadOnlyDictionary<string, string> DefaultKeywordToTag = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["ai"] = "ai",
        ["ki"] = "ai",
        ["machine learning"] = "ai",
        ["ml"] = "ai",
        ["llm"] = "ai",
        ["genai"] = "ai",

        ["kubernetes"] = "cloud",
        ["k8s"] = "cloud",
        ["cloud native"] = "cloud",
        ["docker"] = "cloud",
        ["container"] = "cloud",

        ["java"] = "java",
        ["jvm"] = "java",
        ["spring"] = "java",
        ["kotlin"] = "java",

        [".net"] = "dotnet",
        ["dotnet"] = "dotnet",
        ["c#"] = "dotnet",
        ["csharp"] = "dotnet",
        ["azure"] = "dotnet",

        ["javascript"] = "frontend",
        ["typescript"] = "frontend",
        ["react"] = "frontend",
        ["vue"] = "frontend",
        ["angular"] = "frontend",
        ["frontend"] = "frontend",

        ["python"] = "data",
        ["data science"] = "data",
        ["datenanalyse"] = "data",
        ["analytics"] = "data",
        ["data engineering"] = "data",

        ["security"] = "security",
        ["sicherheit"] = "security",
        ["owasp"] = "security",
        ["pentest"] = "security",
        ["hacking"] = "security",

        ["devops"] = "devops",
        ["sre"] = "devops",
        ["platform engineering"] = "devops",
        ["ci-cd"] = "devops",

        ["rust"] = "systems",
        ["c++"] = "systems",
        ["cpp"] = "systems",
        ["golang"] = "systems",
        // Deliberately no bare "go": it matches ordinary English prose ("let's
        // go", "go to the venue") far more often than the language.

        ["agile"] = "practice",
        ["scrum"] = "practice",
        ["kanban"] = "practice",
        ["craft"] = "practice",
        ["architektur"] = "practice",
        ["architecture"] = "practice",

        ["ux"] = "design",
        ["ui"] = "design",
        ["design"] = "design",
        ["usability"] = "design",

        ["iot"] = "hardware",
        ["embedded"] = "hardware",
        ["robotik"] = "hardware",
        ["robotics"] = "hardware",
        ["hardware"] = "hardware",

        ["open source"] = "opensource",
        ["opensource"] = "opensource",
        ["linux"] = "opensource",
        ["hackergarten"] = "opensource",
    };
}
