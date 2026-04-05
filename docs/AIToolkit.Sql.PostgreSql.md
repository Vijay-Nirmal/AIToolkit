# AIToolkit.Sql.PostgreSql

`AIToolkit.Sql.PostgreSql` exposes PostgreSQL access as `Microsoft.Extensions.AI` functions with a stateless `pgsql_*` tool surface.

## At a Glance

Most applications only need three things:

| Need | Recommended choice |
| --- | --- |
| One PostgreSQL target | `PostgreSqlTools.CreateFunctions(string connectionString, ...)` |
| Safe default behavior | `SqlExecutionPolicy.ReadOnly` |
| Multiple named targets | `PostgreSqlTools.CreateFunctions(IEnumerable<PostgreSqlConnectionProfile>, ...)` |

The tools are stateless. Your host defines one or more named PostgreSQL connections up front, and each tool call passes the `connectionName` it wants to use.

## Quick Start

Start with a single connection and the read-only policy:

```csharp
using AIToolkit.Sql;
using AIToolkit.Sql.PostgreSql;

var tools = PostgreSqlTools.CreateFunctions(
    "Host=localhost;Database=postgres;Username=postgres;Password=postgres",
    executionPolicy: SqlExecutionPolicy.ReadOnly,
    connectionName: "local-pg");
```

That gives the model a safe starter toolset for discovery and read queries:

| Capability | Available with `ReadOnly` |
| --- | --- |
| Discover named connections | Yes |
| Inspect databases, schemas, tables, views, functions, and procedures | Yes |
| Read object definitions | Yes |
| Run `SELECT`-style queries | Yes |
| Run mutations such as `INSERT`, `UPDATE`, or `DROP` | No |

## Common Setup Patterns

### Single Connection

Use the string overload when your app already has a final connection string:

```csharp
var tools = PostgreSqlTools.CreateFunctions(
    connectionString: "Host=localhost;Database=app;Username=postgres;Password=postgres",
    executionPolicy: SqlExecutionPolicy.ReadOnly,
    connectionName: "app-db");
```

### Multiple Named Connections

Use profiles when the model should choose between different logical targets:

```csharp
var tools = PostgreSqlTools.CreateFunctions(
[
    new PostgreSqlConnectionProfile
    {
        Name = "sales",
        ConnectionString = "Host=localhost;Database=sales;Username=postgres;Password=postgres"
    },
    new PostgreSqlConnectionProfile
    {
        Name = "inventory",
        ConnectionOptions = new PostgreSqlConnectionOptions
        {
            Host = "localhost",
            Database = "inventory",
            Username = "postgres",
            Password = "postgres"
        }
    }
],
executionPolicy: SqlExecutionPolicy.ReadOnly);
```

In that model, the typical flow is:

1. The model calls `pgsql_list_servers` to see available connection names.
2. It picks a `connectionName`.
3. It uses that same `connectionName` in later tool calls.

## Stateless Tool Model

This package does not hold open sessions, connection IDs, or in-memory conversation state.

| Behavior | What it means |
| --- | --- |
| Connection selection | Every tool call identifies a host-defined `connectionName` |
| Database selection | Database-scoped tools can also take an optional `database` override |
| Scaling model | Safe for stateless hosts such as ASP.NET Core and multi-instance apps |
| Host responsibility | The host owns the named profiles and any external approval workflow |

Example tool arguments for a per-call database override:

```json
{
    "connectionName": "analytics",
    "database": "warehouse",
    "query": "select current_database(), current_schema()"
}
```

## Configuration Reference

### CreateFunctions Overloads

| API | Use when | Notes |
| --- | --- | --- |
| `CreateFunctions(string connectionString, ...)` | You already have one complete connection string | Simplest way to expose a single named target |
| `CreateFunctions(IEnumerable<PostgreSqlConnectionProfile>, ...)` | You want multiple named targets or structured settings | Each profile becomes a `connectionName` visible to the model |

### PostgreSqlConnectionProfile

Provide either `ConnectionString` or `ConnectionOptions`.

| Property | Required | Description |
| --- | --- | --- |
| `Name` | Yes | Logical name exposed to the model and passed back as `connectionName` |
| `ConnectionString` | No | Raw PostgreSQL connection string |
| `ConnectionOptions` | No | Structured connection settings used to build the connection string |

### PostgreSqlConnectionOptions

| Property | Required | Default | Notes |
| --- | --- | --- | --- |
| `Host` | Yes | None | PostgreSQL host name |
| `Port` | No | `5432` | PostgreSQL port |
| `Database` | No | `postgres` | Default database for the profile |
| `Username` | No | `null` | PostgreSQL user name |
| `Password` | No | `null` | Password for `Username` |
| `SslMode` | No | Provider default | Passed through to `Npgsql` |
| `ApplicationName` | No | `AIToolkit` | Application name reported to PostgreSQL |
| `TimeoutSeconds` | No | Provider default | Connection timeout in seconds |

## Tool Reference

### Discovery and Metadata Tools

| Tool | Purpose | Required parameters | Optional parameters | Returns |
| --- | --- | --- | --- | --- |
| `pgsql_list_servers` | Lists the named connection profiles exposed by the host | None | None | `servers[]` with `connectionName`, `server`, and default `database` |
| `pgsql_list_databases` | Lists visible databases for a named connection | `connectionName` | None | `databases[]` |
| `pgsql_list_extensions` | Lists installed PostgreSQL extensions in the selected database | `connectionName` | `database` | `extensions[]` with name, version, schema, and description |
| `pgsql_list_schemas` | Lists schemas in the selected database | `connectionName` | `database` | `schemas[]` |
| `pgsql_list_tables` | Lists tables in the selected database | `connectionName` | `database` | `tables[]` as `schema.name` |
| `pgsql_list_views` | Lists views in the selected database | `connectionName` | `database` | `views[]` as `schema.name` |
| `pgsql_list_functions` | Lists SQL functions in the selected database | `connectionName` | `database` | `functions[]` as `schema.name` |
| `pgsql_list_procedures` | Lists stored procedures in the selected database | `connectionName` | `database` | `procedures[]` as `schema.name` |
| `pgsql_get_object_definition` | Gets the definition for a schema object | `connectionName`, `objectName` | `schemaName`, `objectKind`, `database` | `definition` object |

### Query Tool

| Tool | Purpose | Required parameters | Optional parameters | Returns |
| --- | --- | --- | --- | --- |
| `pgsql_explain_query` | Runs PostgreSQL `EXPLAIN ANALYZE` for a read-only query and returns plan metrics plus raw JSON | `connectionName`, `query` | `database` | `result` with timing, buffer statistics, WAL statistics, and raw plan JSON |
| `pgsql_run_query` | Executes SQL subject to the current execution policy | `connectionName`, `query` | `database` | `result` with classification, result sets, row counts, and messages |

Example extension lookup:

```json
{
    "connectionName": "analytics",
    "database": "warehouse"
}
```

Example explain request:

```json
{
    "connectionName": "analytics",
    "database": "warehouse",
    "query": "select * from public.orders where order_date >= current_date - interval '30 days'"
}
```

Notes for `pgsql_get_object_definition`:

| Parameter | Meaning |
| --- | --- |
| `objectName` | Object name to resolve |
| `schemaName` | Optional schema hint |
| `objectKind` | Optional kind hint such as `table`, `view`, `function`, or `procedure` |
| `database` | Optional per-call database override |

## Query Result Shape

`pgsql_run_query` returns a structured result rather than raw text.

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

Result size is controlled by `SqlExecutionPolicy`, so large result sets may be truncated intentionally.

## Explain Query Result Shape

`pgsql_explain_query` is intended for performance analysis of read-only PostgreSQL queries. It executes an `EXPLAIN` statement with `ANALYZE`, `VERBOSE`, `BUFFERS`, `SETTINGS`, `WAL`, and `FORMAT JSON` enabled.

| Field | Description |
| --- | --- |
| `query` | The original query text supplied by the caller |
| `explainQuery` | The exact PostgreSQL `EXPLAIN` statement that was executed |
| `format` | The explain-plan format, currently `json` |
| `classification` | The safety classification of the original query |
| `rootNode` | Summary of the root plan node including estimated cost, actual timing, buffer usage, and WAL statistics |
| `planPayload` | The raw PostgreSQL JSON plan payload for deeper analysis |

Use `pgsql_explain_query` when the model needs to answer questions such as why a query is slow, whether PostgreSQL chose a sequential scan, or how much buffer activity the statement generated.

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

Read-only behavior summary:

| Statement type | Behavior under `ReadOnly` |
| --- | --- |
| `SELECT` and other read-only queries | Allowed |
| Metadata tools | Allowed |
| `INSERT`, `UPDATE`, `DELETE`, `MERGE` | Blocked |
| `CREATE`, `ALTER`, `DROP`, `TRUNCATE` | Blocked |

### Enabling Mutations

If your host needs write behavior, create a custom policy:

```csharp
var policy = new SqlExecutionPolicy
{
    AllowMutations = true,
    RequireApprovalForMutations = true,
};

var tools = PostgreSqlTools.CreateFunctions(
    connectionString: "Host=localhost;Database=operations;Username=postgres;Password=postgres",
    executionPolicy: policy,
    mutationApprover: approver,
    connectionName: "operations");
```

### Mutation Approval Behavior

| Behavior | Current implementation |
| --- | --- |
| When approval runs | Inline during `pgsql_run_query` when the query is mutation-capable and approval is required |
| Approval result | The approver must return `Allow` or `Deny` in that same tool call |
| Pending approvals | Not supported by this package |
| Stateless hosting | Approval must be checkable synchronously from durable external state or an external authorization service |
| Frontend approval flows | Build the durable workflow outside the package, then retry execution after approval is recorded |

### ISqlMutationApprover

Use `ISqlMutationApprover` when you want mutation-capable SQL to be allowed only after an explicit host-side approval check.

| Item | Details |
| --- | --- |
| Interface | `ValueTask<SqlMutationApprovalDecision> ApproveAsync(SqlMutationApprovalRequest request, CancellationToken cancellationToken = default)` |
| Called from | `pgsql_run_query` only |
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
        if (request.ConnectionName == "operations")
        {
            return ValueTask.FromResult(SqlMutationApprovalDecision.Allow("Approved by host policy."));
        }

        return ValueTask.FromResult(SqlMutationApprovalDecision.Deny("Mutations are not allowed for this connection."));
    }
}
```

## Provider Notes

| Area | PostgreSQL behavior |
| --- | --- |
| Default schema | `public` when a schema is not specified for object-definition lookup |
| Extensions | Extensions are database-scoped, so `pgsql_list_extensions` may return different results for different `database` values |
| Routine definitions | Uses PostgreSQL catalog helpers such as `pg_get_functiondef` |
| Views | View definitions are reconstructed from `pg_get_viewdef` |
| Server summaries | Server is reported as `host` or `host:port` |

## Observability

If the tool invocation service provider can resolve an `ILoggerFactory` and `Information` logging is enabled, the package logs each tool call with:

| Logged field | Description |
| --- | --- |
| Tool name | The `pgsql_*` function being invoked |
| Parameters | A JSON-serialized view of the tool arguments |