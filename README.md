# AIToolkit

AIToolkit is a family of focused .NET packages for building AI tooling and integrations.

The repository currently includes a reusable SQL abstraction package plus SQL Server, PostgreSQL, MySQL, and SQLite provider packages for agent-ready database tooling, a generic workspace tools package for file, shell, search, notebook, and task operations, a generic document-tools package with Word and Google Docs providers built around canonical AsciiDoc, and a generic web-tools package with pluggable search providers.

## What You Get

These packages help you expose database, workspace, and web capabilities as `Microsoft.Extensions.AI` functions without building your own tool surface from scratch.

In practical terms, you can:

- give an agent read-only access to inspect schema and run safe queries
- expose multiple named database targets without holding server-side session state
- optionally allow write queries behind a host-controlled approval check
- expose file, shell, search, notebook, and task operations through a generic workspace tool surface
- expose document read, write, edit, and content-search operations through a canonical AsciiDoc abstraction for supported document formats
- expose `web_fetch` and provider-driven `web_search` tools with consistent result shapes and prompt guidance

## Choose a Package

| Package | Use when |
| --- | --- |
| `AIToolkit.Tools.Sql` | You want the shared abstractions and execution-policy types |
| `AIToolkit.Tools.Sql.SqlServer` | You need SQL Server `mssql_*` tools |
| `AIToolkit.Tools.Sql.PostgreSql` | You need PostgreSQL `pgsql_*` tools |
| `AIToolkit.Tools.Sql.MySql` | You need MySQL `mysql_*` tools |
| `AIToolkit.Tools.Sql.Sqlite` | You need SQLite `sqlite_*` tools |
| `AIToolkit.Tools` | You need generic `workspace_*` and `task_*` tools for shell, files, search, notebooks, and shared task tracking |
| `AIToolkit.Tools.Document` | You need generic `document_*` tools and a provider-neutral document conversion contract based on canonical AsciiDoc |
| `AIToolkit.Tools.Document.Word` | You need Microsoft Word `.docx`/`.docm`/`.dotx`/`.dotm` support behind the `document_*` tool surface |
| `AIToolkit.Tools.Document.GoogleDocs` | You need hosted Google Docs support behind the `document_*` tool surface, including local `.gdoc` shortcut files for workspace search |
| `AIToolkit.Tools.PDF` | You want first-party PDF extraction that plugs into `workspace_read_file` and returns page text plus embedded images |
| `AIToolkit.Tools.Web` | You need generic `web_fetch` and `web_search` tools plus the base web abstractions |
| `AIToolkit.Tools.Web.DuckDuckGo` | You want DuckDuckGo HTML search as the `web_search` backend |
| `AIToolkit.Tools.Web.Google` | You want Google Custom Search as the `web_search` backend |
| `AIToolkit.Tools.Web.Bing` | You want Bing Web Search as the `web_search` backend |
| `AIToolkit.Tools.Web.Brave` | You want Brave Search as the `web_search` backend |
| `AIToolkit.Tools.Web.Tavily` | You want Tavily Search as the `web_search` backend |

Most applications reference `AIToolkit.Tools.Sql` plus one SQL provider package, use `AIToolkit.Tools` directly for generic workspace and task operations, use `AIToolkit.Tools.Document` plus a document provider such as `AIToolkit.Tools.Document.Word` or `AIToolkit.Tools.Document.GoogleDocs`, or use `AIToolkit.Tools.Web` plus one web-search provider package.

## Getting Started

The easiest starting point is:

1. Add `AIToolkit.Tools.Sql` and the provider package for your database.
2. Create functions with a single connection string and `SqlExecutionPolicy.ReadOnly`.
3. Hand those functions to your AI host or agent runtime.

Example package install commands:

```bash
dotnet add package AIToolkit.Tools.Sql
dotnet add package AIToolkit.Tools.Sql.SqlServer
dotnet add package AIToolkit.Tools
dotnet add package AIToolkit.Tools.Document
dotnet add package AIToolkit.Tools.Document.Word
dotnet add package AIToolkit.Tools.Document.GoogleDocs
dotnet add package AIToolkit.Tools.PDF
dotnet add package AIToolkit.Tools.Web
dotnet add package AIToolkit.Tools.Web.DuckDuckGo
dotnet add package AIToolkit.Tools.Web.Brave
```

## Example

```csharp
using AIToolkit.Tools.Sql;
using AIToolkit.Tools.Sql.SqlServer;

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

For generic workspace operations, use `WorkspaceTools.CreateFunctions(...)` from `AIToolkit.Tools`. Add `TaskTools.CreateFunctions(...)` when you also want the separate `task_*` toolset.

For generic document operations, use `DocumentTools.CreateFunctions(...)` from `AIToolkit.Tools.Document` and register a provider handler such as `WordDocumentTools.CreateHandler()` from `AIToolkit.Tools.Document.Word` or `GoogleDocsDocumentTools.CreateHandler()` from `AIToolkit.Tools.Document.GoogleDocs`.

For PDF extraction in `workspace_read_file`, add `AIToolkit.Tools.PDF` and register `PdfWorkspaceTools.CreateFileHandler()` in `WorkspaceToolsOptions.FileHandlers`.

For generic web operations, use `WebTools.CreateFunctions(...)` from `AIToolkit.Tools.Web` and supply an `IWebSearchProvider` from one of the web provider packages.

## Documentation

- Shared SQL abstractions: `src/AIToolkit.Tools.Sql/README.md`
- Package-specific usage: `src/AIToolkit.Tools.Sql.SqlServer/README.md`
- Package-specific usage: `src/AIToolkit.Tools.Sql.PostgreSql/README.md`
- Package-specific usage: `src/AIToolkit.Tools.Sql.MySql/README.md`
- Package-specific usage: `src/AIToolkit.Tools.Sql.Sqlite/README.md`
- Package-specific usage: `src/AIToolkit.Tools/README.md`
- Package-specific usage: `src/AIToolkit.Tools.Document/README.md`
- Package-specific usage: `src/AIToolkit.Tools.Document.GoogleDocs/README.md`
- Package-specific usage: `src/AIToolkit.Tools.Document.Word/README.md`
- Package-specific usage: `src/AIToolkit.Tools.PDF/README.md`
- Package-specific usage: `src/AIToolkit.Tools.Web/README.md`
- Package-specific usage: `src/AIToolkit.Tools.Web.DuckDuckGo/README.md`
- Package-specific usage: `src/AIToolkit.Tools.Web.Google/README.md`
- Package-specific usage: `src/AIToolkit.Tools.Web.Bing/README.md`
- Package-specific usage: `src/AIToolkit.Tools.Web.Brave/README.md`
- Package-specific usage: `src/AIToolkit.Tools.Web.Tavily/README.md`
- End-user SQL Server guide: `docs/AIToolkit.Tools.Sql.SqlServer.md`
- End-user PostgreSQL guide: `docs/AIToolkit.Tools.Sql.PostgreSql.md`
- End-user MySQL guide: `docs/AIToolkit.Tools.Sql.MySql.md`
- End-user SQLite guide: `docs/AIToolkit.Tools.Sql.Sqlite.md`
- End-user workspace tools guide: `docs/AIToolkit.Tools.md`
- End-user document tools guide: `docs/AIToolkit.Tools.Document.md`
- End-user Google Docs guide: `docs/AIToolkit.Tools.Document.GoogleDocs.md`
- End-user Word document guide: `docs/AIToolkit.Tools.Document.Word.md`
- End-user PDF workspace guide: `docs/AIToolkit.Tools.PDF.md`
- End-user web tools guide: `docs/AIToolkit.Tools.Web.md`
- End-user DuckDuckGo web-search guide: `docs/AIToolkit.Tools.Web.DuckDuckGo.md`
- End-user Google web-search guide: `docs/AIToolkit.Tools.Web.Google.md`
- End-user Bing web-search guide: `docs/AIToolkit.Tools.Web.Bing.md`
- End-user Brave web-search guide: `docs/AIToolkit.Tools.Web.Brave.md`
- End-user Tavily web-search guide: `docs/AIToolkit.Tools.Web.Tavily.md`
- Contributor and repository guide: `docs/README.md`

## Samples

- `samples/AIToolkit.Tools.Sql.SqlServer.Sample` shows an interactive SQL Server agent with demo schema setup.
- `samples/AIToolkit.Tools.Sql.PostgreSql.Sample` shows the same pattern for PostgreSQL.
- `samples/AIToolkit.Tools.Sql.MySql.Sample` shows the same pattern for MySQL.
- `samples/AIToolkit.Tools.Sql.Sqlite.Sample` shows the same pattern for SQLite.
- `samples/AIToolkit.Tools.Sample` shows an interactive agent built on a real `IChatClient` with the combined `workspace_*` and `task_*` toolsets.
- `samples/AIToolkit.Tools.Sample` also demonstrates wiring in `AIToolkit.Tools.PDF` so `workspace_read_file` can extract text and images from PDFs.
- `samples/AIToolkit.Tools.Document.GoogleDocs.Sample` shows a hosted Google Docs workflow that seeds remote documents, writes local `.gdoc` shortcut files, and then reads them back through the generic `document_*` tools.
- `samples/AIToolkit.Tools.Document.Word.Sample` shows an interactive agent that can read, write, edit, and discover Word documents through canonical AsciiDoc.
- `samples/AIToolkit.Tools.Web.Sample` shows an interactive agent that can switch between DuckDuckGo, Google, Bing, Brave, and Tavily for the `web_search` backend.