# AIToolkit.Sql.Sqlite

`AIToolkit.Sql.Sqlite` provides SQLite tools for agents and other AI hosts built on `Microsoft.Extensions.AI`.

The package exposes a provider-specific tool surface that includes:

- `sqlite_list_servers`
- `sqlite_list_databases`
- `sqlite_list_schemas`
- `sqlite_list_tables`
- `sqlite_list_views`
- `sqlite_list_functions`
- `sqlite_list_procedures`
- `sqlite_get_object_definition`
- `sqlite_explain_query`
- `sqlite_run_query`

## Example

```csharp
using AIToolkit.Sql;
using AIToolkit.Sql.Sqlite;

var tools = SqliteTools.CreateFunctions(
    "Data Source=app.db",
    executionPolicy: SqlExecutionPolicy.ReadOnly,
    connectionName: "local-sqlite");
```

## Notes

- Register named connections in the host and let agents pass `connectionName` directly to each tool call.
- SQLite does not expose stored procedures, so those tools return empty results.
- `sqlite_explain_query` runs `EXPLAIN QUERY PLAN` for read-only queries and returns high-level planner output such as scans, searches, covering-index usage, and temporary b-tree usage.
- `sqlite_run_query` classifies statements before execution and blocks or gates mutations based on `SqlExecutionPolicy`.