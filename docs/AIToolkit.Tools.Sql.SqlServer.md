# AIToolkit.Tools.Sql.SqlServer

`AIToolkit.Tools.Sql.SqlServer` exposes SQL Server access as `Microsoft.Extensions.AI` functions with a stateless `mssql_*` tool surface.

## At a Glance

Most applications only need three things:

| Need | Recommended choice |
| --- | --- |
| One SQL Server target | `SqlServerTools.CreateFunctions(string connectionString, ...)` |
| Safe default behavior | `SqlExecutionPolicy.ReadOnly` |
| Multiple named targets | `SqlServerTools.CreateFunctions(IEnumerable<SqlServerConnectionProfile>, ...)` |

The tools are stateless. Your host defines one or more named SQL connections up front, and each tool call passes the `connectionName` it wants to use.

## Quick Start

Start with a single connection and the read-only policy:

```csharp
using AIToolkit.Tools.Sql;
using AIToolkit.Tools.Sql.SqlServer;

var tools = SqlServerTools.CreateFunctions(
    "Server=localhost\\MSSQLSERVER01;Database=master;Trusted_Connection=True;TrustServerCertificate=True;",
    executionPolicy: SqlExecutionPolicy.ReadOnly,
    connectionName: "local-sql");
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
var tools = SqlServerTools.CreateFunctions(
    connectionString: "Server=localhost\\MSSQLSERVER01;Database=Sales;Trusted_Connection=True;TrustServerCertificate=True;",
    executionPolicy: SqlExecutionPolicy.ReadOnly,
    connectionName: "sales");
```

### Multiple Named Connections

Use profiles when the model should choose between different logical targets:

```csharp
var tools = SqlServerTools.CreateFunctions(
[
    new SqlServerConnectionProfile
    {
        Name = "sales",
        ConnectionString = "Server=localhost\\MSSQLSERVER01;Database=Sales;Trusted_Connection=True;TrustServerCertificate=True;"
    },
    new SqlServerConnectionProfile
    {
        Name = "inventory",
        ConnectionOptions = new SqlServerConnectionOptions
        {
            Server = "localhost\\MSSQLSERVER01",
            Database = "Inventory",
            TrustServerCertificate = true
        }
    }
],
executionPolicy: SqlExecutionPolicy.ReadOnly);
```

In that model, the typical flow is:

1. The model calls `mssql_list_servers` to see available connection names.
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

## Configuration Reference

### CreateFunctions Overloads

| API | Use when | Notes |
| --- | --- | --- |
| `CreateFunctions(string connectionString, ...)` | You already have one complete connection string | Simplest way to expose a single named target |
| `CreateFunctions(IEnumerable<SqlServerConnectionProfile>, ...)` | You want multiple named targets or structured settings | Each profile becomes a `connectionName` visible to the model |

### SqlServerConnectionProfile

Provide either `ConnectionString` or `ConnectionOptions`.

| Property | Required | Description |
| --- | --- | --- |
| `Name` | Yes | Logical name exposed to the model and passed back as `connectionName` |
| `ConnectionString` | No | Raw SQL Server connection string |
| `ConnectionOptions` | No | Structured connection settings used to build the connection string |

### SqlServerConnectionOptions

| Property | Required | Default | Notes |
| --- | --- | --- | --- |
| `Server` | Yes | None | SQL Server instance, host name, or host and port |
| `Database` | No | `master` | Default database for the profile |
| `UserId` | No | `null` | Used for SQL authentication |
| `Password` | No | `null` | Used with `UserId` |
| `Authentication` | No | `null` | Passed through to the SQL client for modes such as Azure AD |
| `Encrypt` | No | `true` | Enables transport encryption |
| `TrustServerCertificate` | No | `true` | Trusts the server certificate without full chain validation |
| `ApplicationName` | No | `AIToolkit` | Application name reported to SQL Server |
| `ConnectTimeoutSeconds` | No | SQL client default | Connection timeout in seconds |

Authentication guidance:

| Scenario | What to set |
| --- | --- |
| Windows or integrated auth | Omit `UserId`, `Password`, and `Authentication` |
| SQL authentication | Set `UserId` and `Password` |
| Provider-specific auth mode | Set `Authentication` and any other required connection settings |

## Tool Reference

### Discovery and Metadata Tools

| Tool | Purpose | Required parameters | Optional parameters | Returns |
| --- | --- | --- | --- | --- |
| `mssql_list_servers` | Lists the named connection profiles exposed by the host | None | None | `servers[]` with `connectionName`, `server`, and default `database` |
| `mssql_list_databases` | Lists visible databases for a named connection | `connectionName` | None | `databases[]` |
| `mssql_list_schemas` | Lists schemas in the selected database | `connectionName` | `database` | `schemas[]` |
| `mssql_list_tables` | Lists tables in the selected database | `connectionName` | `database` | `tables[]` as `schema.name` |
| `mssql_list_views` | Lists views in the selected database | `connectionName` | `database` | `views[]` as `schema.name` |
| `mssql_list_functions` | Lists SQL functions in the selected database | `connectionName` | `database` | `functions[]` as `schema.name` |
| `mssql_list_procedures` | Lists stored procedures in the selected database | `connectionName` | `database` | `procedures[]` as `schema.name` |
| `mssql_get_object_definition` | Gets the definition for a schema object | `connectionName`, `objectName` | `schemaName`, `objectKind`, `database` | `definition` object |

### Query Tool

| Tool | Purpose | Required parameters | Optional parameters | Returns |
| --- | --- | --- | --- | --- |
| `mssql_explain_query` | Runs SQL Server actual-plan analysis for a read-only query and returns plan metrics plus raw XML | `connectionName`, `query` | `database` | `result` with timing, estimated cost, relation or index details, and raw XML showplan |
| `mssql_run_query` | Executes SQL subject to the current execution policy | `connectionName`, `query` | `database` | `result` with classification, result sets, row counts, and messages |

Notes for `mssql_get_object_definition`:

| Parameter | Meaning |
| --- | --- |
| `objectName` | Object name to resolve |
| `schemaName` | Optional schema hint |
| `objectKind` | Optional kind hint such as `table`, `view`, `function`, or `procedure` |
| `database` | Optional per-call database override |

## Query Result Shape

`mssql_run_query` returns a structured result rather than raw text.

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

`mssql_explain_query` is intended for performance analysis of read-only SQL Server queries. It executes the query with `SET STATISTICS XML ON` and returns the actual XML showplan produced by SQL Server.

| Field | Description |
| --- | --- |
| `query` | The original query text supplied by the caller |
| `explainQuery` | The T-SQL batch that was executed to collect the plan |
| `format` | The explain-plan format, currently `xml` |
| `classification` | The safety classification of the original query |
| `rootNode` | Summary of the root plan operator including estimated cost, actual rows, elapsed time, CPU time, and relation or index details when available |
| `planPayload` | The raw SQL Server XML showplan payload |

Use `mssql_explain_query` when the model needs to understand whether SQL Server chose a seek or scan, how much elapsed or CPU time the statement consumed, or which index the optimizer used.

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
    MaxRows = 200,
    MaxResultSets = 3,
    MaxStringLength = 4096,
    CommandTimeoutSeconds = 30,
};

var tools = SqlServerTools.CreateFunctions(
    connectionString: "Server=localhost\\MSSQLSERVER01;Database=Operations;Trusted_Connection=True;TrustServerCertificate=True;",
    executionPolicy: policy,
    mutationApprover: approver,
    connectionName: "operations");
```

### Mutation Approval Behavior

| Behavior | Current implementation |
| --- | --- |
| When approval runs | Inline during `mssql_run_query` when the query is mutation-capable and approval is required |
| Approval result | The approver must return `Allow` or `Deny` in that same tool call |
| Pending approvals | Not supported by this package |
| Stateless hosting | Approval must be checkable synchronously from durable external state or an external authorization service |
| Frontend approval flows | Build the durable workflow outside the package, then retry execution after approval is recorded |

### ISqlMutationApprover

Use `ISqlMutationApprover` when you want mutation-capable SQL to be allowed only after an explicit host-side approval check.

| Item | Details |
| --- | --- |
| Interface | `ValueTask<SqlMutationApprovalDecision> ApproveAsync(SqlMutationApprovalRequest request, CancellationToken cancellationToken = default)` |
| Called from | `mssql_run_query` only |
| Called when | The query is classified as `ApprovalRequired`, `AllowMutations` is `true`, and `RequireApprovalForMutations` is `true` |
| If no approver is registered | The query is rejected with the message `Mutation approval is required, but no ISqlMutationApprover is registered.` |
| If approval is denied | Execution stops and the decision reason is returned as the tool message |

### Approval Request Contents

Your approver receives a `SqlMutationApprovalRequest` with everything needed to make a synchronous decision.

| Field | Type | Meaning |
| --- | --- | --- |
| `Target.ConnectionName` | `string` | The named connection the tool call wants to use |
| `Target.Database` | `string?` | Optional per-call database override |
| `Query` | `string` | The raw SQL text awaiting approval |
| `Classification.StatementTypes` | `IReadOnlyList<string>` | The leading statement types detected in the batch |
| `Classification.Safety` | `SqlStatementSafety` | The computed safety level |
| `Classification.Reason` | `string?` | Optional explanation from classification |

### Returning a Decision

| Decision | Helper | Effect |
| --- | --- | --- |
| Approve | `SqlMutationApprovalDecision.Allow("reason")` | Query execution continues |
| Deny | `SqlMutationApprovalDecision.Deny("reason")` | Query execution is blocked and the reason is returned |

### Built-In Approver Types

| Type | Use when | Behavior |
| --- | --- | --- |
| `DelegateSqlMutationApprover` | You want a lightweight inline approval callback | Wraps a delegate as an approver |
| `AllowAllSqlMutationApprover` | You want all mutation-capable queries to proceed | Always returns approval |
| `DenyAllSqlMutationApprover` | You want mutation-capable queries to fail consistently | Always returns denial |

### Recommended Usage Pattern

For most hosts, the simplest approach is:

1. Set `AllowMutations = true`.
2. Keep `RequireApprovalForMutations = true`.
3. Register an `ISqlMutationApprover` that checks your rules or external durable approval state.
4. Return a clear denial reason when execution should not proceed.

Example using `DelegateSqlMutationApprover`:

```csharp
using AIToolkit.Tools.Sql;
using AIToolkit.Tools.Sql.SqlServer;

var policy = new SqlExecutionPolicy
{
    AllowMutations = true,
    RequireApprovalForMutations = true,
};

var approver = new DelegateSqlMutationApprover((request, cancellationToken) =>
{
    var statementTypes = request.Classification.StatementTypes;
    var isDelete = statementTypes.Any(static statementType =>
        string.Equals(statementType, "DELETE", StringComparison.OrdinalIgnoreCase));

    if (isDelete)
    {
        return ValueTask.FromResult(
            SqlMutationApprovalDecision.Deny("DELETE statements are not allowed by this host."));
    }

    if (!string.Equals(request.Target.ConnectionName, "operations", StringComparison.OrdinalIgnoreCase))
    {
        return ValueTask.FromResult(
            SqlMutationApprovalDecision.Deny("Mutations are only allowed on the operations connection."));
    }

    return ValueTask.FromResult(
        SqlMutationApprovalDecision.Allow("Approved by host mutation policy."));
});

var tools = SqlServerTools.CreateFunctions(
    connectionString: "Server=localhost\\MSSQLSERVER01;Database=Operations;Trusted_Connection=True;TrustServerCertificate=True;",
    executionPolicy: policy,
    mutationApprover: approver,
    connectionName: "operations");
```

### When to Implement a Custom Class

Use a dedicated `ISqlMutationApprover` class instead of `DelegateSqlMutationApprover` when you need dependency injection, durable approval lookups, or more involved policy logic.

Good examples include:

- Checking an approval table keyed by request or business operation.
- Calling an external authorization service before allowing writes.
- Enforcing environment-specific rules such as allowing mutations only in staging.
- Applying statement-specific rules based on connection, database, or detected statement types.

### Stateless Approval Guidance

This package does not create an approval workflow on your behalf. In stateless or multi-instance hosts, the approver must be able to answer immediately by reading durable external state.

Typical host-owned patterns include:

- Looking up a previously recorded approval in a database table.
- Validating a signed approval token supplied through your application workflow.
- Calling an external policy service that can make a synchronous allow or deny decision.

## Observability

If the tool invocation service provider can resolve an `ILoggerFactory` and `Information` logging is enabled, the package logs each tool call with:

| Logged field | Description |
| --- | --- |
| Tool name | The `mssql_*` function being invoked |
| Parameters | A JSON-serialized view of the tool arguments |

This is useful for debugging agent behavior and auditing what the model attempted to do.

## Sample

The console sample in `samples/AIToolkit.Tools.Sql.SqlServer.Sample/` shows a realistic end-to-end setup:

| Sample behavior | Details |
| --- | --- |
| Configuration | Reads SQL Server and OpenAI-compatible settings from `appsettings.json` and user secrets |
| Database bootstrap | Creates a retail demo database on startup |
| Tool registration | Registers the full SQL Server tool set |
| Host loop | Runs an interactive `Microsoft.Extensions.AI` chat flow |

## User Secrets

```bash
dotnet user-secrets set "OpenAI:ApiKey" "<your-api-key>" --project samples/AIToolkit.Tools.Sql.SqlServer.Sample
dotnet user-secrets set "OpenAI:Endpoint" "https://api.openai.com/v1/" --project samples/AIToolkit.Tools.Sql.SqlServer.Sample
dotnet user-secrets set "OpenAI:Model" "gpt-4o-mini" --project samples/AIToolkit.Tools.Sql.SqlServer.Sample
```