using System.Text.RegularExpressions;

namespace AIToolkit.Tools.Sql.MySql;

/// <summary>
/// Applies a conservative MySQL-oriented safety classification to ad-hoc SQL text.
/// </summary>
/// <remarks>
/// The classifier strips comments and quoted content before inspecting tokens so routine string literals do not accidentally trigger mutation
/// or administrative classifications. MySQL-specific commands such as <c>INSTALL</c> and <c>UNINSTALL</c> are blocked outright because they
/// affect server state beyond a single schema.
/// </remarks>
internal sealed partial class MySqlQueryClassifier : ISqlQueryClassifier
{
    private static readonly string[] ApprovalKeywords =
    [
        "ALTER",
        "ANALYZE",
        "CALL",
        "CREATE",
        "DELETE",
        "DROP",
        "GRANT",
        "INSERT",
        "INTO",
        "LOAD",
        "OPTIMIZE",
        "RENAME",
        "REPAIR",
        "REPLACE",
        "REVOKE",
        "TRUNCATE",
        "UPDATE",
    ];

    private static readonly string[] BlockedKeywords =
    [
        "INSTALL",
        "SHUTDOWN",
        "UNINSTALL",
    ];

    private static readonly string[] ReadOnlyKeywords =
    [
        "DESCRIBE",
        "EXPLAIN",
        "SELECT",
        "SHOW",
        "WITH",
    ];

    /// <summary>
    /// Classifies ad-hoc MySQL SQL text as read-only, approval-required, or blocked.
    /// </summary>
    /// <param name="query">The SQL text to inspect.</param>
    /// <returns>
    /// A classification that records the detected statement types, the computed safety level, and a reason when the query is not a safe read.
    /// </returns>
    /// <remarks>
    /// The classifier removes comments and quoted content before token inspection so keywords inside string literals, identifiers, and comments
    /// do not trigger false positives.
    /// </remarks>
    public SqlQueryClassification Classify(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new SqlQueryClassification(Array.Empty<string>(), SqlStatementSafety.Blocked, "Query text is required.");
        }

        var normalized = StripQuotedContent(StripCommentsRegex().Replace(query, string.Empty)).Trim();
        var upper = normalized.ToUpperInvariant();
        var statementTypes = ExtractStatementTypes(normalized);

        if (ContainsKeyword(upper, BlockedKeywords))
        {
            return new SqlQueryClassification(statementTypes, SqlStatementSafety.Blocked, "The query contains blocked MySQL commands.");
        }

        if (ContainsKeyword(upper, ApprovalKeywords))
        {
            return new SqlQueryClassification(statementTypes, SqlStatementSafety.ApprovalRequired, "The query can modify database state.");
        }

        if (statementTypes.Count == 0 || !ContainsKeyword(upper, ReadOnlyKeywords))
        {
            return new SqlQueryClassification(statementTypes, SqlStatementSafety.Blocked, "The query could not be classified as a safe read operation.");
        }

        return new SqlQueryClassification(statementTypes, SqlStatementSafety.ReadOnly);
    }

    private static List<string> ExtractStatementTypes(string query)
    {
        var types = new List<string>();

        foreach (var segment in query.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.IsNullOrWhiteSpace(segment))
            {
                continue;
            }

            var match = LeadingTokenRegex().Match(segment);
            if (!match.Success)
            {
                continue;
            }

            var token = match.Groups[1].Value.ToUpperInvariant();
            if (!types.Contains(token, StringComparer.Ordinal))
            {
                types.Add(token);
            }
        }

        return types;
    }

    private static bool ContainsKeyword(string query, IEnumerable<string> keywords)
    {
        var tokens = KeywordTokenRegex().Matches(query).Select(static match => match.Value).ToHashSet(StringComparer.Ordinal);

        foreach (var keyword in keywords)
        {
            if (tokens.Contains(keyword))
            {
                return true;
            }
        }

        return false;
    }

    [GeneratedRegex(@"(?ms)/\*.*?\*/|--.*?$|#.*?$", RegexOptions.Compiled)]
    private static partial Regex StripCommentsRegex();

    [GeneratedRegex(@"'([^'\\]|\\.|'')*'|""([^""\\]|\\.|"""")*""|`([^`]|``)*`", RegexOptions.Compiled | RegexOptions.Singleline)]
    private static partial Regex StripQuotedContentRegex();

    [GeneratedRegex(@"^\s*([A-Za-z_]+)", RegexOptions.Compiled)]
    private static partial Regex LeadingTokenRegex();

    [GeneratedRegex(@"[A-Z_]+", RegexOptions.Compiled)]
    private static partial Regex KeywordTokenRegex();

    private static string StripQuotedContent(string query) => StripQuotedContentRegex().Replace(query, " ");
}
