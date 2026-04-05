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
dotnet pack src/AIToolkit.Sql/AIToolkit.Sql.csproj -c Release
dotnet pack src/AIToolkit.Sql.PostgreSql/AIToolkit.Sql.PostgreSql.csproj -c Release
dotnet pack src/AIToolkit.Sql.MySql/AIToolkit.Sql.MySql.csproj -c Release
dotnet pack src/AIToolkit.Sql.SqlServer/AIToolkit.Sql.SqlServer.csproj -c Release
dotnet pack src/AIToolkit.Sql.Sqlite/AIToolkit.Sql.Sqlite.csproj -c Release
```

NuGet packages are emitted to `artifacts/packages/`.

## Package docs

- `AIToolkit.Sql.SqlServer`: see `docs/AIToolkit.Sql.SqlServer.md`
- `AIToolkit.Sql.PostgreSql`: see `docs/AIToolkit.Sql.PostgreSql.md`
- `AIToolkit.Sql.MySql`: see `docs/AIToolkit.Sql.MySql.md`
- `AIToolkit.Sql.Sqlite`: see `docs/AIToolkit.Sql.Sqlite.md`

## Project conventions

- Target .NET 10 for packages unless there is a documented reason to multi-target.
- Keep shared repo and package metadata in `Directory.Build.props`.
- Keep package versions in `Directory.Packages.props`.
- Put provider-neutral SQL contracts in `AIToolkit.Sql` and provider-specific behavior in packages such as `AIToolkit.Sql.SqlServer`.
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