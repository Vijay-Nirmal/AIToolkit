# AIToolkit.Sql.SqlServer

`AIToolkit.Sql.SqlServer` provides SQL Server tools for agents and other AI hosts built on `Microsoft.Extensions.AI`.

The package exposes a reference-aligned tool surface that includes:

- `mssql_list_servers`
- `mssql_list_databases`
- `mssql_list_schemas`
- `mssql_list_tables`
- `mssql_list_views`
- `mssql_list_functions`
- `mssql_list_procedures`
- `mssql_get_object_definition`
- `mssql_explain_query`
- `mssql_run_query`

## Example

```csharp
using AIToolkit.Sql;
using AIToolkit.Sql.SqlServer;

var tools = SqlServerTools.CreateFunctions(
    "Server=localhost\\MSSQLSERVER01;Database=AIToolkit;Trusted_Connection=True;TrustServerCertificate=True;",
    executionPolicy: SqlExecutionPolicy.ReadOnly,
    connectionName: "local-sql");

foreach (var tool in tools)
{
    Console.WriteLine(tool.Name);
}
```

## Notes

- Register named connections in the host and let agents pass `connectionName` directly to each tool call.
- `mssql_explain_query` runs `SET STATISTICS XML ON` for read-only queries and returns the actual XML showplan plus parsed execution statistics when the login can access showplan output.
- `mssql_run_query` classifies statements before execution and blocks or gates mutations based on `SqlExecutionPolicy`.
- For mutation-capable tool calls, provide an `ISqlMutationApprover` so writes require an explicit approval decision.