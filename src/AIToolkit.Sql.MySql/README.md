# AIToolkit.Sql.MySql

`AIToolkit.Sql.MySql` provides MySQL tools for agents and other AI hosts built on `Microsoft.Extensions.AI`.

The package exposes a provider-specific tool surface that includes:

- `mysql_list_servers`
- `mysql_list_databases`
- `mysql_list_schemas`
- `mysql_list_tables`
- `mysql_list_views`
- `mysql_list_functions`
- `mysql_list_procedures`
- `mysql_get_object_definition`
- `mysql_explain_query`
- `mysql_run_query`

## Example

```csharp
using AIToolkit.Sql;
using AIToolkit.Sql.MySql;

var tools = MySqlTools.CreateFunctions(
    "Server=localhost;Database=mysql;User ID=root;Password=password",
    executionPolicy: SqlExecutionPolicy.ReadOnly,
    connectionName: "local-mysql");
```

## Notes

- Register named connections in the host and let agents pass `connectionName` directly to each tool call.
- `mysql_explain_query` runs `EXPLAIN ANALYZE FORMAT=TREE` for read-only queries and returns the raw tree plus parsed iterator timing details when the server supports that feature.
- `mysql_run_query` classifies statements before execution and blocks or gates mutations based on `SqlExecutionPolicy`.
- For mutation-capable tool calls, provide an `ISqlMutationApprover` so writes require an explicit approval decision.