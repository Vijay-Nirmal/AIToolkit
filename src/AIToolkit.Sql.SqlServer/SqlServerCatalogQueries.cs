namespace AIToolkit.Sql.SqlServer;

/// <summary>
/// Holds the SQL Server catalog queries used by the metadata provider.
/// </summary>
/// <remarks>
/// Keeping the raw SQL in one place makes the metadata provider easier to read and gives future provider packages a clear seam for supplying
/// their own catalog SQL without changing the higher-level mapping logic.
/// </remarks>
internal static class SqlServerCatalogQueries
{
    public const string ListDatabases =
        "SELECT name AS DatabaseName FROM sys.databases ORDER BY name;";

    public const string ListSchemas =
        "SELECT name AS SchemaName FROM sys.schemas WHERE name NOT IN ('sys', 'information_schema') ORDER BY name;";

    public const string ListTables =
        "SELECT CONCAT(SCHEMA_NAME(schema_id), '.', name) AS TableName FROM sys.tables ORDER BY SCHEMA_NAME(schema_id), name;";

    public const string ListViews =
        "SELECT CONCAT(SCHEMA_NAME(schema_id), '.', name) AS ViewName FROM sys.views ORDER BY SCHEMA_NAME(schema_id), name;";

    public const string ListFunctions =
        "SELECT CONCAT(SCHEMA_NAME(schema_id), '.', name) AS FunctionName FROM sys.objects WHERE type IN ('FN', 'IF', 'TF') ORDER BY SCHEMA_NAME(schema_id), name;";

    public const string ListProcedures =
        "SELECT CONCAT(SCHEMA_NAME(schema_id), '.', name) AS ProcedureName FROM sys.procedures ORDER BY SCHEMA_NAME(schema_id), name;";

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

    public const string SqlModuleDefinition = @"
SELECT sm.definition
FROM sys.sql_modules sm
INNER JOIN sys.objects o ON o.object_id = sm.object_id
INNER JOIN sys.schemas s ON s.schema_id = o.schema_id
WHERE s.name = @schemaName AND o.name = @objectName AND o.type IN ({0});";

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