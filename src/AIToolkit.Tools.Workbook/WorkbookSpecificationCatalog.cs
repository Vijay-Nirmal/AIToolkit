using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AIToolkit.Tools.Workbook;

internal static partial class WorkbookSpecificationCatalog
{
    private static readonly Lazy<IReadOnlyList<WorkbookSpecificationSection>> Sections = new(LoadSections);

    public static WorkbookSpecificationMatch[] Search(string query, int maxResults)
    {
        var normalizedQuery = query.Trim();
        var tokens = Tokenize(normalizedQuery);
        if (tokens.Count == 0)
        {
            return [];
        }

        return Sections.Value
            .Select(section => new
            {
                Section = section,
                Score = Score(section, normalizedQuery, tokens),
            })
            .Where(static candidate => candidate.Score > 0)
            .OrderByDescending(static candidate => candidate.Score)
            .ThenBy(static candidate => candidate.Section.Title, StringComparer.Ordinal)
            .Take(Math.Clamp(maxResults, 1, 20))
            .Select(static candidate => new WorkbookSpecificationMatch(
                candidate.Section.SectionId,
                candidate.Section.Title,
                [.. candidate.Section.Keywords],
                [.. candidate.Section.Content]))
            .ToArray();
    }

    private static int Score(WorkbookSpecificationSection section, string query, IReadOnlyCollection<string> tokens)
    {
        var score = 0;
        if (section.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            score += 8;
        }

        foreach (var keyword in section.Keywords)
        {
            if (string.Equals(keyword, query, StringComparison.OrdinalIgnoreCase))
            {
                score += 10;
            }

            if (tokens.Contains(keyword, StringComparer.OrdinalIgnoreCase))
            {
                score += 4;
            }
        }

        foreach (var token in tokens)
        {
            if (section.Title.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                score += 3;
            }

            if (section.Content.Any(line => line.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                score += 2;
            }
        }

        return score;
    }

    private static WorkbookSpecificationSection[] LoadSections()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("AIToolkit.Tools.Workbook.WorkbookDocSpecificationIndex.json")
            ?? throw new InvalidOperationException("WorkbookDoc specification index resource was not found.");
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        return JsonSerializer.Deserialize<WorkbookSpecificationSection[]>(json, ToolJsonSerializerOptions.CreateWeb())
            ?? throw new InvalidOperationException("WorkbookDoc specification index could not be deserialized.");
    }

    private static HashSet<string> Tokenize(string query) =>
        QueryTokenRegex()
            .Matches(query)
            .Select(static match => match.Value.Trim().ToLowerInvariant())
            .Where(static token => token.Length > 1)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex(@"[A-Za-z0-9#\-\./]+", RegexOptions.CultureInvariant)]
    private static partial Regex QueryTokenRegex();
}

internal sealed record WorkbookSpecificationSection(
    string SectionId,
    string Title,
    string[] Keywords,
    string[] Content);
