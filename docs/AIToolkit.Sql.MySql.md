# AIToolkit.Sql.MySql

`AIToolkit.Sql.MySql` exposes MySQL access as `Microsoft.Extensions.AI` functions with a stateless `mysql_*` tool surface.

## At a Glance

Most applications only need three things:

| Need | Recommended choice |
| --- | --- |
| One MySQL target | `MySqlTools.CreateFunctions(string connectionString, ...)` |
| Safe default behavior | `SqlExecutionPolicy.ReadOnly` |
| Multiple named targets | `MySqlTools.CreateFunctions(IEnumerable<MySqlConnectionProfile>, ...)` |

The tools are stateless. Your host defines one or more named MySQL connections up front, and each tool call passes the `connectionName` it wants to use.

## Quick Start

Start with a single connection and the read-only policy:

```csharp
using AIToolkit.Sql;
using AIToolkit.Sql.MySql;

var tools = MySqlTools.CreateFunctions(
    "Server=localhost;Database=mysql;User ID=root;Password=password",
    executionPolicy: SqlExecutionPolicy.ReadOnly,
    connectionName: "local-mysql");
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
var tools = MySqlTools.CreateFunctions(
    connectionString: "Server=localhost;Database=app;User ID=root;Password=password",
    executionPolicy: SqlExecutionPolicy.ReadOnly,
    connectionName: "app-db");
```

### Multiple Named Connections

Use profiles when the model should choose between different logical targets:

```csharp
var tools = MySqlTools.CreateFunctions(
[
    new MySqlConnectionProfile
    {
        Name = "sales",
        ConnectionString = "Server=localhost;Database=sales;User ID=root;Password=password"
    },
    new MySqlConnectionProfile
    {
        Name = "inventory",
        ConnectionOptions = new MySqlConnectionOptions
        {
            Server = "localhost",
            Database = "inventory",
            UserId = "root",
            Password = "password"
        }
    }
],
executionPolicy: SqlExecutionPolicy.ReadOnly);
```

In that model, the typical flow is:

1. The model calls `mysql_list_servers` to see available connection names.
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

Example tool arguments for a database-scoped query:

```json
{
    "connectionName": "inventory",
    "database": "inventory",
    "query": "select count(*) as ProductCount from products"
}
```

## Configuration Reference

### CreateFunctions Overloads

| API | Use when | Notes |
| --- | --- | --- |
| `CreateFunctions(string connectionString, ...)` | You already have one complete connection string | Simplest way to expose a single named target |
| `CreateFunctions(IEnumerable<MySqlConnectionProfile>, ...)` | You want multiple named targets or structured settings | Each profile becomes a `connectionName` visible to the model |

### MySqlConnectionProfile

Provide either `ConnectionString` or `ConnectionOptions`.

| Property | Required | Description |
| --- | --- | --- |
| `Name` | Yes | Logical name exposed to the model and passed back as `connectionName` |
| `ConnectionString` | No | Raw MySQL connection string |
| `ConnectionOptions` | No | Structured connection settings used to build the connection string |

### MySqlConnectionOptions

| Property | Required | Default | Notes |
| --- | --- | --- | --- |
| `Server` | Yes | None | MySQL server host name |
| `Port` | No | `3306` | MySQL server port |
| `Database` | No | `mysql` | Default database for the profile |
| `UserId` | No | `null` | Login user name |
| `Password` | No | `null` | Password for `UserId` |
| `SslMode` | No | Provider default | Passed through to `MySqlConnector` |
| `ConnectionTimeoutSeconds` | No | Provider default | Connection timeout in seconds |

## Tool Reference

### Discovery and Metadata Tools

| Tool | Purpose | Required parameters | Optional parameters | Returns |
| --- | --- | --- | --- | --- |
| `mysql_list_servers` | Lists the named connection profiles exposed by the host | None | None | `servers[]` with `connectionName`, `server`, and default `database` |
| `mysql_list_databases` | Lists visible databases for a named connection | `connectionName` | None | `databases[]` |
| `mysql_list_schemas` | Lists schemas in the selected database | `connectionName` | `database` | `schemas[]` |
| `mysql_list_tables` | Lists tables in the selected database | `connectionName` | `database` | `tables[]` as `schema.name` |
| `mysql_list_views` | Lists views in the selected database | `connectionName` | `database` | `views[]` as `schema.name` |
| `mysql_list_functions` | Lists SQL functions in the selected database | `connectionName` | `database` | `functions[]` as `schema.name` |
| `mysql_list_procedures` | Lists stored procedures in the selected database | `connectionName` | `database` | `procedures[]` as `schema.name` |
| `mysql_get_object_definition` | Gets the definition for a schema object | `connectionName`, `objectName` | `schemaName`, `objectKind`, `database` | `definition` object |

### Query Tool

| Tool | Purpose | Required parameters | Optional parameters | Returns |
| --- | --- | --- | --- | --- |
| `mysql_explain_query` | Runs MySQL explain-plan analysis for a read-only query and returns tree output plus iterator timing details | `connectionName`, `query` | `database` | `result` with parsed root-node timing and the raw tree plan text |
| `mysql_run_query` | Executes SQL subject to the current execution policy | `connectionName`, `query` | `database` | `result` with classification, result sets, row counts, and messages |

Notes for `mysql_get_object_definition`:

| Parameter | Meaning |
| --- | --- |
| `objectName` | Object name to resolve |
| `schemaName` | Optional schema hint |
| `objectKind` | Optional kind hint such as `table`, `view`, `function`, or `procedure` |
| `database` | Optional per-call database override |

## Query Result Shape

`mysql_run_query` returns a structured result rather than raw text.

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

`mysql_explain_query` is intended for performance analysis of read-only MySQL queries. It executes `EXPLAIN ANALYZE FORMAT=TREE` and returns both the raw plan text and a parsed summary of the root iterator.

| Field | Description |
| --- | --- |
| `query` | The original query text supplied by the caller |
| `explainQuery` | The exact MySQL `EXPLAIN ANALYZE` statement that was executed |
| `format` | The explain-plan format, currently `text` |
| `classification` | The safety classification of the original query |
| `rootNode` | Summary of the root iterator including estimated cost, estimated rows, actual startup time, actual total time, rows, and loops when MySQL reports them |
| `planPayload` | The raw MySQL tree plan text |

Use `mysql_explain_query` when the model needs to understand the optimizer’s chosen iterator tree, root cost, row estimates, or runtime iterator timings.

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
| When approval runs | Inline during `mysql_run_query` when the query is mutation-capable and approval is required |
| Approval result | The approver must return `Allow` or `Deny` in that same tool call |
| Pending approvals | Not supported by this package |
| Stateless hosting | Approval must be checkable synchronously from durable external state or an external authorization service |
| Frontend approval flows | Build the durable workflow outside the package, then retry execution after approval is recorded |

### ISqlMutationApprover

Use `ISqlMutationApprover` when you want mutation-capable SQL to be allowed only after an explicit host-side approval check.

| Item | Details |
| --- | --- |
| Interface | `ValueTask<SqlMutationApprovalDecision> ApproveAsync(SqlMutationApprovalRequest request, CancellationToken cancellationToken = default)` |
| Called from | `mysql_run_query` only |
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

| Area | MySQL behavior |
| --- | --- |
| Explain support | `mysql_explain_query` depends on server support for `EXPLAIN ANALYZE`; older servers may reject it |
| Schema model | MySQL treats schemas and databases as the same logical namespace |
| `mysql_list_schemas` | Reports the current database selection |
| Table definitions | Reconstructed from `INFORMATION_SCHEMA.COLUMNS` |
| Routine definitions | Read from `INFORMATION_SCHEMA.ROUTINES` when available |

## Observability

If the tool invocation service provider can resolve an `ILoggerFactory` and `Information` logging is enabled, the package logs each tool call with:

| Logged field | Description |
| --- | --- |
| Tool name | The `mysql_*` function being invoked |
| Parameters | A JSON-serialized view of the tool arguments |