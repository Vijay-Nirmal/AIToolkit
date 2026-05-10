using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AIToolkit.Tools.Deck;

internal static partial class DeckSpecificationCatalog
{
    private static readonly Lazy<IReadOnlyList<DeckSpecificationSection>> Sections = new(LoadSections);
    private static readonly Lazy<IReadOnlyDictionary<string, DeckSpecificationSection>> SectionsById = new(
        () => Sections.Value.ToDictionary(static section => section.SectionId, StringComparer.OrdinalIgnoreCase));

    public static DeckSpecificationMatch[] Search(string query, int maxResults)
        => Search(query, sectionIds: null, maxResults);

    public static DeckSpecificationMatch[] Search(string? query, IEnumerable<string>? sectionIds, int maxResults)
    {
        var normalizedSectionIds = NormalizeSectionIds(sectionIds).ToArray();
        var ignoreMaxResults = normalizedSectionIds.Length > 0;
        var effectiveMaxResults = Math.Clamp(maxResults, 1, 20);
        var matches = new List<DeckSpecificationMatch>(ignoreMaxResults ? normalizedSectionIds.Length : effectiveMaxResults);
        var seenSectionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var sectionId in normalizedSectionIds)
        {
            if (!ignoreMaxResults && matches.Count >= effectiveMaxResults)
            {
                return [.. matches];
            }

            if (!SectionsById.Value.TryGetValue(sectionId, out var section) || !seenSectionIds.Add(section.SectionId))
            {
                continue;
            }

            matches.Add(CreateMatch(section));
        }

        var normalizedQuery = query?.Trim() ?? string.Empty;
        var tokens = Tokenize(normalizedQuery);
        if (tokens.Count == 0)
        {
            return [.. matches];
        }

        matches.AddRange(
            Sections.Value
                .Where(section => !seenSectionIds.Contains(section.SectionId))
                .Select(section => new
                {
                    Section = section,
                    Score = Score(section, normalizedQuery, tokens),
                })
                .Where(static candidate => candidate.Score > 0)
                .OrderByDescending(static candidate => candidate.Score)
                .ThenBy(static candidate => candidate.Section.Title, StringComparer.Ordinal)
                .Take(ignoreMaxResults ? int.MaxValue : effectiveMaxResults - matches.Count)
                .Select(static candidate => CreateMatch(candidate.Section)));

        return [.. matches];
    }

    public static IReadOnlyList<string> GetSectionIds() =>
        [.. Sections.Value.Select(static section => section.SectionId)];

    private static int Score(DeckSpecificationSection section, string query, IReadOnlyCollection<string> tokens)
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

    private static DeckSpecificationSection[] LoadSections()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("AIToolkit.Tools.Deck.DeckDocSpecificationIndex.json")
            ?? throw new InvalidOperationException("DeckDoc specification index resource was not found.");
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        return JsonSerializer.Deserialize<DeckSpecificationSection[]>(json, ToolJsonSerializerOptions.CreateWeb())
            ?? throw new InvalidOperationException("DeckDoc specification index could not be deserialized.");
    }

    private static HashSet<string> Tokenize(string query) =>
        QueryTokenRegex()
            .Matches(query)
            .Select(static match => match.Value.Trim().ToLowerInvariant())
            .Where(static token => token.Length > 1)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> NormalizeSectionIds(IEnumerable<string>? sectionIds) =>
        sectionIds is null
            ? []
            : sectionIds
                .Where(static sectionId => !string.IsNullOrWhiteSpace(sectionId))
                .Select(static sectionId => sectionId.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase);

    private static DeckSpecificationMatch CreateMatch(DeckSpecificationSection section) =>
        new(
            section.SectionId,
            section.Title,
            [.. section.Keywords],
            [.. section.Content],
            [.. section.CommonlyUsedWith]);

    [GeneratedRegex(@"[A-Za-z0-9#\-\./]+", RegexOptions.CultureInvariant)]
    private static partial Regex QueryTokenRegex();
}

internal sealed record DeckSpecificationSection(
    string SectionId,
    string Title,
    string[] Keywords,
    string[] Content,
    string[] CommonlyUsedWith);

