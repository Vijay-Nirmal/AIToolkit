# AIToolkit

AIToolkit is a family of focused .NET packages for building AI tooling and integrations.

The repository currently includes a reusable SQL abstraction package plus SQL Server, PostgreSQL, MySQL, and SQLite provider packages for agent-ready database tooling.

## What You Get

These packages help you expose database capabilities as `Microsoft.Extensions.AI` functions without building your own tool surface from scratch.

In practical terms, you can:

- give an agent read-only access to inspect schema and run safe queries
- expose multiple named database targets without holding server-side session state
- optionally allow write queries behind a host-controlled approval check

## Choose a Package

| Package | Use when |
| --- | --- |
| `AIToolkit.Sql` | You want the shared abstractions and execution-policy types |
| `AIToolkit.Sql.SqlServer` | You need SQL Server `mssql_*` tools |
| `AIToolkit.Sql.PostgreSql` | You need PostgreSQL `pgsql_*` tools |
| `AIToolkit.Sql.MySql` | You need MySQL `mysql_*` tools |
| `AIToolkit.Sql.Sqlite` | You need SQLite `sqlite_*` tools |

Most applications reference `AIToolkit.Sql` plus one provider package.

## Getting Started

The easiest starting point is:

1. Add `AIToolkit.Sql` and the provider package for your database.
2. Create functions with a single connection string and `SqlExecutionPolicy.ReadOnly`.
3. Hand those functions to your AI host or agent runtime.

Example package install commands:

```bash
dotnet add package AIToolkit.Sql
dotnet add package AIToolkit.Sql.SqlServer
```

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

The same pattern applies to PostgreSQL, MySQL, and SQLite. The main thing that changes is the provider package, the connection string, and the tool-name prefix.

## Documentation

- Shared SQL abstractions: `src/AIToolkit.Sql/README.md`
- Package-specific usage: `src/AIToolkit.Sql.SqlServer/README.md`
- Package-specific usage: `src/AIToolkit.Sql.PostgreSql/README.md`
- Package-specific usage: `src/AIToolkit.Sql.MySql/README.md`
- Package-specific usage: `src/AIToolkit.Sql.Sqlite/README.md`
- End-user SQL Server guide: `docs/AIToolkit.Sql.SqlServer.md`
- End-user PostgreSQL guide: `docs/AIToolkit.Sql.PostgreSql.md`
- End-user MySQL guide: `docs/AIToolkit.Sql.MySql.md`
- End-user SQLite guide: `docs/AIToolkit.Sql.Sqlite.md`
- Contributor and repository guide: `docs/README.md`

## Samples

- `samples/AIToolkit.Sql.SqlServer.Sample` shows an interactive SQL Server agent with demo schema setup.
- `samples/AIToolkit.Sql.PostgreSql.Sample` shows the same pattern for PostgreSQL.
- `samples/AIToolkit.Sql.MySql.Sample` shows the same pattern for MySQL.
- `samples/AIToolkit.Sql.Sqlite.Sample` shows the same pattern for SQLite.