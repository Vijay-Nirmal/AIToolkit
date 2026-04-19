namespace AIToolkit.Tools.Sql.MySql;

/// <summary>
/// Holds the MySQL catalog queries used by the metadata provider.
/// </summary>
/// <remarks>
/// MySQL uses <c>information_schema</c> for most metadata, but some context such as the active database still comes from connection-scoped
/// functions. Centralizing the raw SQL here keeps provider-specific catalog logic easy to audit.
/// </remarks>
internal static class MySqlCatalogQueries
{
    /// <summary>
    /// Gets the query that lists user-visible databases.
    /// </summary>
    public const string ListDatabases = @"
SELECT schema_name
FROM information_schema.schemata
WHERE schema_name NOT IN ('information_schema', 'mysql', 'performance_schema', 'sys')
ORDER BY schema_name;";

    /// <summary>
    /// Gets the query that returns the currently selected database as MySQL's schema equivalent.
    /// </summary>
    public const string ListSchemas = "SELECT DATABASE();";

    /// <summary>
    /// Gets the query that lists base tables in the current database.
    /// </summary>
    public const string ListTables = @"
SELECT CONCAT(table_schema, '.', table_name)
FROM information_schema.tables
WHERE table_schema = DATABASE() AND table_type = 'BASE TABLE'
ORDER BY table_name;";

    /// <summary>
    /// Gets the query that lists views in the current database.
    /// </summary>
    public const string ListViews = @"
SELECT CONCAT(table_schema, '.', table_name)
FROM information_schema.tables
WHERE table_schema = DATABASE() AND table_type = 'VIEW'
ORDER BY table_name;";

    /// <summary>
    /// Gets the query that lists functions in the current database.
    /// </summary>
    public const string ListFunctions = @"
SELECT CONCAT(routine_schema, '.', routine_name)
FROM information_schema.routines
WHERE routine_schema = DATABASE() AND routine_type = 'FUNCTION'
ORDER BY routine_name;";

    /// <summary>
    /// Gets the query that lists stored procedures in the current database.
    /// </summary>
    public const string ListProcedures = @"
SELECT CONCAT(routine_schema, '.', routine_name)
FROM information_schema.routines
WHERE routine_schema = DATABASE() AND routine_type = 'PROCEDURE'
ORDER BY routine_name;";

    /// <summary>
    /// Gets the query that returns the active database for the current connection.
    /// </summary>
    public const string CurrentDatabase = "SELECT DATABASE();";

    /// <summary>
    /// Gets the query that resolves whether a named object is a table, view, function, or procedure.
    /// </summary>
    public const string ResolveObjectKind = @"
SELECT kind
FROM (
    SELECT CASE table_type WHEN 'BASE TABLE' THEN 'Table' WHEN 'VIEW' THEN 'View' ELSE 'Unknown' END AS kind, 0 AS sort_order
    FROM information_schema.tables
    WHERE table_schema = @schemaName AND table_name = @objectName

    UNION ALL

    SELECT CASE routine_type WHEN 'FUNCTION' THEN 'Function' WHEN 'PROCEDURE' THEN 'Procedure' ELSE 'Unknown' END AS kind, 1 AS sort_order
    FROM information_schema.routines
    WHERE routine_schema = @schemaName AND routine_name = @objectName
) kinds
ORDER BY sort_order
LIMIT 1;";

    /// <summary>
    /// Gets the query that returns the stored definition fragment for a view.
    /// </summary>
    public const string ViewDefinition = @"
SELECT view_definition
FROM information_schema.views
WHERE table_schema = @schemaName AND table_name = @objectName;";

    /// <summary>
    /// Gets the query that returns the stored definition fragment for a function or procedure.
    /// </summary>
    public const string RoutineDefinition = @"
SELECT routine_definition
FROM information_schema.routines
WHERE routine_schema = @schemaName AND routine_name = @objectName AND routine_type = @routineType
LIMIT 1;";

    /// <summary>
    /// Gets the query that returns table columns in ordinal order.
    /// </summary>
    public const string TableColumns = @"
SELECT column_name, column_type, is_nullable, ordinal_position
FROM information_schema.columns
WHERE table_schema = @schemaName AND table_name = @objectName
ORDER BY ordinal_position;";
}
