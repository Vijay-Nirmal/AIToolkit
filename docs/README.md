# Documentation

This document is the developer-facing guide for the AIToolkit repository.

## Repository layout

- `src/` contains packable SDK-style library projects.
- `tests/` contains MSTest projects paired with packages under `src/`.
- `samples/` contains runnable sample applications for package usage.
- `docs/` contains repository-level notes and package documentation.

## Local development

```bash
dotnet restore AIToolkit.slnx
dotnet build AIToolkit.slnx -c Release
dotnet test AIToolkit.slnx -c Release
dotnet pack src/AIToolkit.Tools.Sql/AIToolkit.Tools.Sql.csproj -c Release
dotnet pack src/AIToolkit.Tools.Sql.PostgreSql/AIToolkit.Tools.Sql.PostgreSql.csproj -c Release
dotnet pack src/AIToolkit.Tools.Sql.MySql/AIToolkit.Tools.Sql.MySql.csproj -c Release
dotnet pack src/AIToolkit.Tools.Sql.SqlServer/AIToolkit.Tools.Sql.SqlServer.csproj -c Release
dotnet pack src/AIToolkit.Tools.Sql.Sqlite/AIToolkit.Tools.Sql.Sqlite.csproj -c Release
dotnet pack src/AIToolkit.Tools/AIToolkit.Tools.csproj -c Release
dotnet pack src/AIToolkit.Tools.PDF/AIToolkit.Tools.PDF.csproj -c Release
dotnet pack src/AIToolkit.Tools.Web/AIToolkit.Tools.Web.csproj -c Release
dotnet pack src/AIToolkit.Tools.Web.DuckDuckGo/AIToolkit.Tools.Web.DuckDuckGo.csproj -c Release
dotnet pack src/AIToolkit.Tools.Web.Google/AIToolkit.Tools.Web.Google.csproj -c Release
dotnet pack src/AIToolkit.Tools.Web.Bing/AIToolkit.Tools.Web.Bing.csproj -c Release
dotnet pack src/AIToolkit.Tools.Web.Brave/AIToolkit.Tools.Web.Brave.csproj -c Release
dotnet pack src/AIToolkit.Tools.Web.Tavily/AIToolkit.Tools.Web.Tavily.csproj -c Release
```

NuGet packages are emitted to `artifacts/packages/`.

## Package docs

- `AIToolkit.Tools.Sql.SqlServer`: see `docs/AIToolkit.Tools.Sql.SqlServer.md`
- `AIToolkit.Tools.Sql.PostgreSql`: see `docs/AIToolkit.Tools.Sql.PostgreSql.md`
- `AIToolkit.Tools.Sql.MySql`: see `docs/AIToolkit.Tools.Sql.MySql.md`
- `AIToolkit.Tools.Sql.Sqlite`: see `docs/AIToolkit.Tools.Sql.Sqlite.md`
- `AIToolkit.Tools`: see `docs/AIToolkit.Tools.md`
- `AIToolkit.Tools.PDF`: see `docs/AIToolkit.Tools.PDF.md`
- `AIToolkit.Tools.Web`: see `docs/AIToolkit.Tools.Web.md`
- `AIToolkit.Tools.Web.DuckDuckGo`: see `docs/AIToolkit.Tools.Web.DuckDuckGo.md`
- `AIToolkit.Tools.Web.Google`: see `docs/AIToolkit.Tools.Web.Google.md`
- `AIToolkit.Tools.Web.Bing`: see `docs/AIToolkit.Tools.Web.Bing.md`
- `AIToolkit.Tools.Web.Brave`: see `docs/AIToolkit.Tools.Web.Brave.md`
- `AIToolkit.Tools.Web.Tavily`: see `docs/AIToolkit.Tools.Web.Tavily.md`

## Project conventions

- Target .NET 10 for packages unless there is a documented reason to multi-target.
- Keep shared repo and package metadata in `Directory.Build.props`.
- Keep package versions in `Directory.Packages.props`.
- Put provider-neutral SQL contracts in `AIToolkit.Tools.Sql` and provider-specific behavior in packages such as `AIToolkit.Tools.Sql.SqlServer`.
- New packages should include a package-specific `README.md` under the project folder.
- Tests should use MSTest.
- Tests and samples stay non-packable.

## Adding a new package

1. Create the library project under `src/`.
2. Create the matching MSTest project under `tests/`.
3. Add a runnable sample under `samples/` if the API benefits from executable usage.
4. Add all projects to `AIToolkit.slnx`.
5. Add package documentation and update the root README if the public package list changes.

## Before first publish

Update `RepositoryUrl` in `Directory.Build.props` to the final public GitHub repository URL.