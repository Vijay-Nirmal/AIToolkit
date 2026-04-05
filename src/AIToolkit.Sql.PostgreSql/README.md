# AIToolkit.Sql.PostgreSql

`AIToolkit.Sql.PostgreSql` provides PostgreSQL tools for agents and other AI hosts built on `Microsoft.Extensions.AI`.

The package exposes a provider-specific tool surface that includes:

- `pgsql_list_servers`
- `pgsql_list_databases`
- `pgsql_list_extensions`
- `pgsql_list_schemas`
- `pgsql_list_tables`
- `pgsql_list_views`
- `pgsql_list_functions`
- `pgsql_list_procedures`
- `pgsql_get_object_definition`
- `pgsql_explain_query`
- `pgsql_run_query`

## Example

```csharp
using AIToolkit.Sql;
using AIToolkit.Sql.PostgreSql;

var tools = PostgreSqlTools.CreateFunctions(
    "Host=localhost;Database=postgres;Username=postgres;Password=postgres",
    executionPolicy: SqlExecutionPolicy.ReadOnly,
    connectionName: "local-pg");

foreach (var tool in tools)
{
    Console.WriteLine(tool.Name);
}
```

## Notes

- Register named connections in the host and let agents pass `connectionName` directly to each tool call.
- `pgsql_list_extensions` reports installed extensions for the selected PostgreSQL database.
- `pgsql_explain_query` runs `EXPLAIN (ANALYZE, VERBOSE, BUFFERS, SETTINGS, WAL, FORMAT JSON)` for read-only queries and returns both raw plan JSON and summary metrics.
- `pgsql_run_query` classifies statements before execution and blocks or gates mutations based on `SqlExecutionPolicy`.
- For mutation-capable tool calls, provide an `ISqlMutationApprover` so writes require an explicit approval decision.