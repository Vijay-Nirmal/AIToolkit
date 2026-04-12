namespace AIToolkit.Tools.Sql.Sqlite;

/// <summary>
/// Holds the SQLite catalog queries used by the metadata provider.
/// </summary>
internal static class SqliteCatalogQueries
{
    public const string DatabaseList = "PRAGMA database_list;";

    public static string ListObjects(string databaseName, string objectType) =>
        $"SELECT name FROM {QuoteIdentifier(databaseName)}.sqlite_schema WHERE type = '{objectType}' AND name NOT LIKE 'sqlite_%' ORDER BY name;";

    public static string ResolveObjectKind(string databaseName) =>
        $"SELECT CASE type WHEN 'table' THEN 'Table' WHEN 'view' THEN 'View' ELSE 'Unknown' END FROM {QuoteIdentifier(databaseName)}.sqlite_schema WHERE name = $objectName AND type IN ('table', 'view') LIMIT 1;";

    public static string ObjectDefinition(string databaseName, string objectType) =>
        $"SELECT sql FROM {QuoteIdentifier(databaseName)}.sqlite_schema WHERE name = $objectName AND type = '{objectType}' LIMIT 1;";

    public static string QuoteIdentifier(string identifier) => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}