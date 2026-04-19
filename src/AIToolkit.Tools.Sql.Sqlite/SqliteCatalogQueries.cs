namespace AIToolkit.Tools.Sql.Sqlite;

/// <summary>
/// Holds the SQLite catalog queries used by the metadata provider.
/// </summary>
/// <remarks>
/// SQLite exposes metadata through <c>PRAGMA</c> commands and the <c>sqlite_schema</c> table rather than through server-wide catalog views.
/// Keeping the raw SQL in one place makes the provider logic easier to review and highlights where SQLite diverges from server-based engines.
/// </remarks>
internal static class SqliteCatalogQueries
{
    /// <summary>
    /// Gets the <c>PRAGMA database_list</c> command used to enumerate attached catalogs.
    /// </summary>
    public const string DatabaseList = "PRAGMA database_list;";

    /// <summary>
    /// Builds the query used to list tables or views from <c>sqlite_schema</c>.
    /// </summary>
    /// <param name="databaseName">The attached catalog to inspect.</param>
    /// <param name="objectType">The SQLite object type, such as <c>table</c> or <c>view</c>.</param>
    /// <returns>The SQL text that reads object names from <c>sqlite_schema</c>.</returns>
    public static string ListObjects(string databaseName, string objectType) =>
        $"SELECT name FROM {QuoteIdentifier(databaseName)}.sqlite_schema WHERE type = '{objectType}' AND name NOT LIKE 'sqlite_%' ORDER BY name;";

    /// <summary>
    /// Builds the query that resolves whether an object name maps to a table or view.
    /// </summary>
    /// <param name="databaseName">The attached catalog to inspect.</param>
    /// <returns>The SQL text that maps SQLite schema rows to <see cref="SqlObjectKind"/> names.</returns>
    public static string ResolveObjectKind(string databaseName) =>
        $"SELECT CASE type WHEN 'table' THEN 'Table' WHEN 'view' THEN 'View' ELSE 'Unknown' END FROM {QuoteIdentifier(databaseName)}.sqlite_schema WHERE name = $objectName AND type IN ('table', 'view') LIMIT 1;";

    /// <summary>
    /// Builds the query that returns the stored SQL definition for a table or view.
    /// </summary>
    /// <param name="databaseName">The attached catalog to inspect.</param>
    /// <param name="objectType">The SQLite object type, such as <c>table</c> or <c>view</c>.</param>
    /// <returns>The SQL text that reads the stored definition from <c>sqlite_schema</c>.</returns>
    public static string ObjectDefinition(string databaseName, string objectType) =>
        $"SELECT sql FROM {QuoteIdentifier(databaseName)}.sqlite_schema WHERE name = $objectName AND type = '{objectType}' LIMIT 1;";

    /// <summary>
    /// Quotes an attached catalog identifier for safe interpolation into SQLite metadata queries.
    /// </summary>
    /// <param name="identifier">The catalog identifier to quote.</param>
    /// <returns>A double-quoted identifier with embedded quotes escaped.</returns>
    public static string QuoteIdentifier(string identifier) => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}
