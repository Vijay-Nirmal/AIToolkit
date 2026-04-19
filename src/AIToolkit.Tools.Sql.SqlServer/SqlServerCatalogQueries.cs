namespace AIToolkit.Tools.Sql.SqlServer;

/// <summary>
/// Holds the SQL Server catalog queries used by the metadata provider.
/// </summary>
/// <remarks>
/// Keeping the raw SQL in one place makes the metadata provider easier to read and gives future provider packages a clear seam for supplying
/// their own catalog SQL without changing the higher-level mapping logic.
/// </remarks>
internal static class SqlServerCatalogQueries
{
    /// <summary>
    /// Gets the query that lists visible databases from <c>sys.databases</c>.
    /// </summary>
    public const string ListDatabases =
        "SELECT name AS DatabaseName FROM sys.databases ORDER BY name;";

    /// <summary>
    /// Gets the query that lists non-system schemas from <c>sys.schemas</c>.
    /// </summary>
    public const string ListSchemas =
        "SELECT name AS SchemaName FROM sys.schemas WHERE name NOT IN ('sys', 'information_schema') ORDER BY name;";

    /// <summary>
    /// Gets the query that lists tables with schema-qualified names.
    /// </summary>
    public const string ListTables =
        "SELECT CONCAT(SCHEMA_NAME(schema_id), '.', name) AS TableName FROM sys.tables ORDER BY SCHEMA_NAME(schema_id), name;";

    /// <summary>
    /// Gets the query that lists views with schema-qualified names.
    /// </summary>
    public const string ListViews =
        "SELECT CONCAT(SCHEMA_NAME(schema_id), '.', name) AS ViewName FROM sys.views ORDER BY SCHEMA_NAME(schema_id), name;";

    /// <summary>
    /// Gets the query that lists scalar, inline table-valued, and table-valued functions.
    /// </summary>
    public const string ListFunctions =
        "SELECT CONCAT(SCHEMA_NAME(schema_id), '.', name) AS FunctionName FROM sys.objects WHERE type IN ('FN', 'IF', 'TF') ORDER BY SCHEMA_NAME(schema_id), name;";

    /// <summary>
    /// Gets the query that lists stored procedures.
    /// </summary>
    public const string ListProcedures =
        "SELECT CONCAT(SCHEMA_NAME(schema_id), '.', name) AS ProcedureName FROM sys.procedures ORDER BY SCHEMA_NAME(schema_id), name;";

    /// <summary>
    /// Gets the query that resolves whether a named object is a table, view, function, or procedure.
    /// </summary>
    /// <remarks>
    /// SQL Server exposes object shape through <c>sys.objects.type</c>, so the provider maps those compact type codes to the shared
    /// <see cref="SqlObjectKind"/> names here.
    /// </remarks>
    public const string ResolveObjectKind = @"
SELECT TOP (1)
    CASE
        WHEN o.type IN ('FN', 'IF', 'TF') THEN 'Function'
        WHEN o.type IN ('P', 'PC') THEN 'Procedure'
        WHEN o.type = 'V' THEN 'View'
        WHEN o.type = 'U' THEN 'Table'
        ELSE 'Unknown'
    END AS ObjectKind
FROM sys.objects o
INNER JOIN sys.schemas s ON s.schema_id = o.schema_id
WHERE s.name = @schemaName AND o.name = @objectName;";

    /// <summary>
    /// Gets the template query that returns the stored definition text for views, functions, and procedures.
    /// </summary>
    /// <remarks>
    /// <see cref="SqlServerMetadataProvider"/> replaces the <c>{0}</c> placeholder with a provider-specific object-type filter before
    /// execution so the same base query can serve views, functions, and procedures.
    /// </remarks>
    public const string SqlModuleDefinition = @"
SELECT sm.definition
FROM sys.sql_modules sm
INNER JOIN sys.objects o ON o.object_id = sm.object_id
INNER JOIN sys.schemas s ON s.schema_id = o.schema_id
WHERE s.name = @schemaName AND o.name = @objectName AND o.type IN ({0});";

    /// <summary>
    /// Gets the query that returns table-column metadata in ordinal order.
    /// </summary>
    /// <remarks>
    /// The result includes size, precision, and scale so the provider can reconstruct readable <c>CREATE TABLE</c> statements for AI callers.
    /// </remarks>
    public const string TableColumns = @"
SELECT
    c.COLUMN_NAME,
    c.DATA_TYPE,
    c.IS_NULLABLE,
    c.ORDINAL_POSITION,
    c.CHARACTER_MAXIMUM_LENGTH,
    c.NUMERIC_PRECISION,
    c.NUMERIC_SCALE
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.TABLE_SCHEMA = @schemaName AND c.TABLE_NAME = @objectName
ORDER BY c.ORDINAL_POSITION;";
}
