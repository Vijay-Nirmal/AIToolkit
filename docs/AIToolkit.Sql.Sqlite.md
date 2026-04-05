# AIToolkit.Sql.Sqlite

`AIToolkit.Sql.Sqlite` exposes SQLite access as `Microsoft.Extensions.AI` functions with a stateless `sqlite_*` tool surface.

## At a Glance

Most applications only need three things:

| Need | Recommended choice |
| --- | --- |
| One SQLite target | `SqliteTools.CreateFunctions(string connectionString, ...)` |
| Safe default behavior | `SqlExecutionPolicy.ReadOnly` |
| Multiple named targets | `SqliteTools.CreateFunctions(IEnumerable<SqliteConnectionProfile>, ...)` |

The tools are stateless. Your host defines one or more named SQLite connections up front, and each tool call passes the `connectionName` it wants to use.

## Quick Start

Start with a single connection and the read-only policy:

```csharp
using AIToolkit.Sql;
using AIToolkit.Sql.Sqlite;

var tools = SqliteTools.CreateFunctions(
    "Data Source=app.db",
    executionPolicy: SqlExecutionPolicy.ReadOnly,
    connectionName: "local-sqlite");
```

That gives the model a safe starter toolset for discovery and read queries:

| Capability | Available with `ReadOnly` |
| --- | --- |
| Discover named connections | Yes |
| Inspect attached catalogs, tables, and views | Yes |
| Read object definitions | Yes |
| Run `SELECT`-style queries | Yes |
| Run mutations such as `INSERT`, `UPDATE`, or `DROP` | No |

## Common Setup Patterns

### Single Connection

Use the string overload when your app already has a final connection string:

```csharp
var tools = SqliteTools.CreateFunctions(
    connectionString: "Data Source=app.db",
    executionPolicy: SqlExecutionPolicy.ReadOnly,
    connectionName: "app-db");
```

### Multiple Named Connections

Use profiles when the model should choose between different logical targets:

```csharp
var tools = SqliteTools.CreateFunctions(
[
    new SqliteConnectionProfile
    {
        Name = "app",
        ConnectionString = "Data Source=app.db"
    },
    new SqliteConnectionProfile
    {
        Name = "analytics",
        ConnectionOptions = new SqliteConnectionOptions
        {
            DataSource = "analytics.db"
        }
    }
],
executionPolicy: SqlExecutionPolicy.ReadOnly);
```

In that model, the typical flow is:

1. The model calls `sqlite_list_servers` to see available connection names.
2. It picks a `connectionName`.
3. It uses that same `connectionName` in later tool calls.

## Stateless Tool Model

This package does not hold open sessions, connection IDs, or in-memory conversation state.

| Behavior | What it means |
| --- | --- |
| Connection selection | Every tool call identifies a host-defined `connectionName` |
| Database selection | For SQLite, the optional `database` argument selects an attached catalog such as `main` or `temp` |
| Scaling model | Safe for stateless hosts and local-tool scenarios |
| Host responsibility | The host owns the named profiles and any external approval workflow |

Example tool arguments when you want to target the default attached catalog:

```json
{
    "connectionName": "app",
    "database": "main",
    "query": "select name from sqlite_schema where type = 'table' order by name"
}
```

## Configuration Reference

### CreateFunctions Overloads

| API | Use when | Notes |
| --- | --- | --- |
| `CreateFunctions(string connectionString, ...)` | You already have one complete connection string | Simplest way to expose a single named target |
| `CreateFunctions(IEnumerable<SqliteConnectionProfile>, ...)` | You want multiple named targets or structured settings | Each profile becomes a `connectionName` visible to the model |

### SqliteConnectionProfile

Provide either `ConnectionString` or `ConnectionOptions`.

| Property | Required | Description |
| --- | --- | --- |
| `Name` | Yes | Logical name exposed to the model and passed back as `connectionName` |
| `ConnectionString` | No | Raw SQLite connection string |
| `ConnectionOptions` | No | Structured connection settings used to build the connection string |

### SqliteConnectionOptions

| Property | Required | Default | Notes |
| --- | --- | --- | --- |
| `DataSource` | Yes | None | SQLite file path or data source |
| `Mode` | No | Provider default | Optional `SqliteOpenMode` |
| `Cache` | No | Provider default | Optional `SqliteCacheMode` |
| `Password` | No | `null` | Optional SQLite password when supported |

## Tool Reference

### Discovery and Metadata Tools

| Tool | Purpose | Required parameters | Optional parameters | Returns |
| --- | --- | --- | --- | --- |
| `sqlite_list_servers` | Lists the named connection profiles exposed by the host | None | None | `servers[]` with `connectionName`, `server`, and default `database` |
| `sqlite_list_databases` | Lists attached SQLite catalogs for a named connection | `connectionName` | None | `databases[]` |
| `sqlite_list_schemas` | Lists attached SQLite catalogs for a named connection | `connectionName` | `database` | `schemas[]` |
| `sqlite_list_tables` | Lists tables in the selected SQLite catalog | `connectionName` | `database` | `tables[]` as `catalog.name` |
| `sqlite_list_views` | Lists views in the selected SQLite catalog | `connectionName` | `database` | `views[]` as `catalog.name` |
| `sqlite_list_functions` | Lists SQLite functions known to metadata | `connectionName` | `database` | `functions[]`, currently empty |
| `sqlite_list_procedures` | Lists SQLite procedures known to metadata | `connectionName` | `database` | `procedures[]`, currently empty |
| `sqlite_get_object_definition` | Gets the definition for a table or view | `connectionName`, `objectName` | `schemaName`, `objectKind`, `database` | `definition` object |

### Query Tool

| Tool | Purpose | Required parameters | Optional parameters | Returns |
| --- | --- | --- | --- | --- |
| `sqlite_explain_query` | Runs `EXPLAIN QUERY PLAN` for a read-only SQLite query and returns high-level planner output | `connectionName`, `query` | `database` | `result` with parsed planner summary and serialized plan rows |
| `sqlite_run_query` | Executes SQL subject to the current execution policy | `connectionName`, `query` | `database` | `result` with classification, result sets, row counts, and messages |

Example query against the `main` catalog:

```json
{
    "connectionName": "app",
    "database": "main",
    "query": "select count(*) as OrderCount from Orders"
}
```

## Query Result Shape

`sqlite_run_query` returns a structured result rather than raw text.

| Field | Description |
| --- | --- |
| `success` | Whether execution completed successfully |
| `classification.statementTypes` | Leading SQL statement types detected in the batch |
| `classification.safety` | One of `ReadOnly`, `ApprovalRequired`, or `Blocked` |
| `classification.reason` | Explanation when a statement is blocked or requires approval |
| `resultSets` | Materialized result sets |
| `resultSets[].columns` | Column metadata including name, SQL type, ordinal, and nullability |
| `resultSets[].rows` | Returned rows as key-value objects |
| `resultSets[].isTruncated` | Whether more rows existed than were returned |
| `resultSets[].totalRowCount` | Total rows observed while reading the result set |
| `recordsAffected` | Provider-reported affected row count when available |
| `message` | Error, warning, or diagnostic message |

## Explain Query Result Shape

`sqlite_explain_query` is intended for planner inspection of read-only SQLite queries. It executes `EXPLAIN QUERY PLAN` and returns the planner rows in a serialized payload plus a compact summary of the first planner step.

| Field | Description |
| --- | --- |
| `query` | The original query text supplied by the caller |
| `explainQuery` | The exact SQLite `EXPLAIN QUERY PLAN` statement that was executed |
| `format` | The explain-plan format, currently `tabular-json` |
| `classification` | The safety classification of the original query |
| `rootNode` | Summary of the first planner row, including the scan or search mode and any detected index name |
| `planPayload` | A JSON-serialized copy of the planner rows returned by SQLite |

SQLite does not provide runtime iterator timings through `EXPLAIN QUERY PLAN`, so this tool is best for identifying scans, searches, covering-index use, nested loop order, and temporary b-tree usage rather than measuring elapsed execution statistics.

## Safety

### Recommended Starting Policy

`SqlExecutionPolicy.ReadOnly` is the best default for analyst-style agents, copilots, and prototypes.

### SqlExecutionPolicy Reference

| Property | Default | Purpose |
| --- | --- | --- |
| `AllowMutations` | `false` | Allows write-capable SQL to run |
| `RequireApprovalForMutations` | `true` | Requires an `ISqlMutationApprover` for mutation-capable SQL |
| `MaxRows` | `200` | Maximum rows materialized per result set |
| `MaxResultSets` | `3` | Maximum result sets materialized from one execution |
| `MaxStringLength` | `4096` | Maximum characters kept for one scalar value |
| `CommandTimeoutSeconds` | `30` | SQL command timeout |

### Mutation Approval Behavior

| Behavior | Current implementation |
| --- | --- |
| When approval runs | Inline during `sqlite_run_query` when the query is mutation-capable and approval is required |
| Approval result | The approver must return `Allow` or `Deny` in that same tool call |
| Pending approvals | Not supported by this package |
| Stateless hosting | Approval must be checkable synchronously from durable external state or an external authorization service |
| Frontend approval flows | Build the durable workflow outside the package, then retry execution after approval is recorded |

### ISqlMutationApprover

Use `ISqlMutationApprover` when you want mutation-capable SQL to be allowed only after an explicit host-side approval check.

| Item | Details |
| --- | --- |
| Interface | `ValueTask<SqlMutationApprovalDecision> ApproveAsync(SqlMutationApprovalRequest request, CancellationToken cancellationToken = default)` |
| Called from | `sqlite_run_query` only |
| Called when | The query is classified as `ApprovalRequired`, `AllowMutations` is `true`, and `RequireApprovalForMutations` is `true` |
| If no approver is registered | The query is rejected with the message `Mutation approval is required, but no ISqlMutationApprover is registered.` |
| If approval is denied | Execution stops and the decision reason is returned as the tool message |

Minimal approver example:

```csharp
using AIToolkit.Sql;

sealed class AllowListedMutationApprover : ISqlMutationApprover
{
    public ValueTask<SqlMutationApprovalDecision> ApproveAsync(
        SqlMutationApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.ConnectionName == "maintenance")
        {
            return ValueTask.FromResult(SqlMutationApprovalDecision.Allow("Approved by host policy."));
        }

        return ValueTask.FromResult(SqlMutationApprovalDecision.Deny("Mutations are not allowed for this connection."));
    }
}
```

## Provider Notes

| Area | SQLite behavior |
| --- | --- |
| Server model | `server` in summaries is the configured `DataSource` |
| Catalog model | `database` and `schema` map to attached SQLite catalogs such as `main` and `temp` |
| Explain plans | `sqlite_explain_query` uses `EXPLAIN QUERY PLAN`, whose raw output format may vary between SQLite releases |
| Routine metadata | Function and procedure list tools return empty results |
| Object definitions | Read from `sqlite_schema` |
| Definition lookup | Supports tables and views only |

## Observability

If the tool invocation service provider can resolve an `ILoggerFactory` and `Information` logging is enabled, the package logs each tool call with:

| Logged field | Description |
| --- | --- |
| Tool name | The `sqlite_*` function being invoked |
| Parameters | A JSON-serialized view of the tool arguments |