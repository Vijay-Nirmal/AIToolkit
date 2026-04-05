namespace AIToolkit.Sql.PostgreSql;

/// <summary>
/// Holds the PostgreSQL catalog queries used by the metadata provider.
/// </summary>
internal static class PostgreSqlCatalogQueries
{
    public const string ListExtensions = @"
SELECT
    e.extname,
    e.extversion,
    n.nspname,
    d.description
FROM pg_extension e
INNER JOIN pg_namespace n ON n.oid = e.extnamespace
LEFT JOIN pg_description d ON d.objoid = e.oid AND d.classoid = 'pg_extension'::regclass AND d.objsubid = 0
ORDER BY e.extname;";

    public const string ListDatabases = @"
SELECT datname
FROM pg_database
WHERE datallowconn AND NOT datistemplate
ORDER BY datname;";

    public const string ListSchemas = @"
SELECT schema_name
FROM information_schema.schemata
WHERE schema_name <> 'information_schema' AND schema_name NOT LIKE 'pg_%'
ORDER BY schema_name;";

    public const string ListTables = @"
SELECT concat(table_schema, '.', table_name)
FROM information_schema.tables
WHERE table_type = 'BASE TABLE' AND table_schema <> 'information_schema' AND table_schema NOT LIKE 'pg_%'
ORDER BY table_schema, table_name;";

    public const string ListViews = @"
SELECT concat(table_schema, '.', table_name)
FROM information_schema.tables
WHERE table_type = 'VIEW' AND table_schema <> 'information_schema' AND table_schema NOT LIKE 'pg_%'
ORDER BY table_schema, table_name;";

    public const string ListFunctions = @"
SELECT concat(n.nspname, '.', p.proname)
FROM pg_proc p
INNER JOIN pg_namespace n ON n.oid = p.pronamespace
WHERE p.prokind = 'f' AND n.nspname <> 'information_schema' AND n.nspname NOT LIKE 'pg_%'
ORDER BY n.nspname, p.proname;";

    public const string ListProcedures = @"
SELECT concat(n.nspname, '.', p.proname)
FROM pg_proc p
INNER JOIN pg_namespace n ON n.oid = p.pronamespace
WHERE p.prokind = 'p' AND n.nspname <> 'information_schema' AND n.nspname NOT LIKE 'pg_%'
ORDER BY n.nspname, p.proname;";

    public const string ResolveObjectKind = @"
SELECT kind
FROM (
    SELECT
        CASE c.relkind
            WHEN 'r' THEN 'Table'
            WHEN 'v' THEN 'View'
            ELSE 'Unknown'
        END AS kind,
        0 AS sort_order
    FROM pg_class c
    INNER JOIN pg_namespace n ON n.oid = c.relnamespace
    WHERE n.nspname = @schemaName AND c.relname = @objectName AND c.relkind IN ('r', 'v')

    UNION ALL

    SELECT
        CASE p.prokind
            WHEN 'f' THEN 'Function'
            WHEN 'p' THEN 'Procedure'
            ELSE 'Unknown'
        END AS kind,
        1 AS sort_order
    FROM pg_proc p
    INNER JOIN pg_namespace n ON n.oid = p.pronamespace
    WHERE n.nspname = @schemaName AND p.proname = @objectName AND p.prokind IN ('f', 'p')
) kinds
ORDER BY sort_order
LIMIT 1;";

    public const string ViewDefinition = @"
SELECT pg_get_viewdef(c.oid, true)
FROM pg_class c
INNER JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE n.nspname = @schemaName AND c.relname = @objectName AND c.relkind = 'v';";

    public const string FunctionDefinition = @"
SELECT pg_get_functiondef(p.oid)
FROM pg_proc p
INNER JOIN pg_namespace n ON n.oid = p.pronamespace
WHERE n.nspname = @schemaName AND p.proname = @objectName AND p.prokind = 'f'
ORDER BY p.oid
LIMIT 1;";

    public const string ProcedureDefinition = @"
SELECT pg_get_functiondef(p.oid)
FROM pg_proc p
INNER JOIN pg_namespace n ON n.oid = p.pronamespace
WHERE n.nspname = @schemaName AND p.proname = @objectName AND p.prokind = 'p'
ORDER BY p.oid
LIMIT 1;";

    public const string TableColumns = @"
SELECT
    a.attname,
    format_type(a.atttypid, a.atttypmod) AS data_type,
    NOT a.attnotnull AS is_nullable,
    a.attnum
FROM pg_attribute a
INNER JOIN pg_class c ON c.oid = a.attrelid
INNER JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE n.nspname = @schemaName AND c.relname = @objectName AND c.relkind = 'r' AND a.attnum > 0 AND NOT a.attisdropped
ORDER BY a.attnum;";
}