# AIToolkit Agent Guide

## Repository purpose

This repository hosts .NET 10 NuGet packages for AIToolkit. Treat `src/` projects as publishable packages and keep supporting assets in `tests/`, `samples/`, and `docs/`.

## Layout

- `src/<PackageName>/` contains one packable SDK-style library project.
- `tests/<PackageName>.Tests/` contains tests for the package.
- `samples/<PackageName>.Sample/` contains a runnable sample for the package when useful.
- `docs/` contains repository-level documentation.

## Build and validation

```bash
dotnet restore AIToolkit.slnx
dotnet build AIToolkit.slnx -c Release
dotnet test AIToolkit.slnx -c Release
dotnet pack src/<PackageName>/<PackageName>.csproj -c Release
```

## Conventions

- Target `.NET 10` unless there is a documented reason to multi-target.
- Keep package versions in `Directory.Packages.props`, not individual project files.
- Shared repo and NuGet metadata belongs in `Directory.Build.props`.
- Every new package should have a clear package `README.md` in its project directory.
- Prefer small public APIs with tests and a sample that proves the intended usage.
- Update `docs/` and the root `README.md` when the package list or repo workflow changes.
- Prefer `public static` entry-point classes for package tool factories and keep implementation types `internal` where possible.
- For AI function packages, keep tool behavior in an internal service and keep stable tool names and descriptions in a separate factory class.
- Tool methods exposed through `AIFunctionFactory` should accept optional `IServiceProvider` and `CancellationToken` parameters so DI and cancellation flow through `AIFunctionArguments.Services` and `InvokeAsync` automatically.
- Prefer structured result records with `Success` and `Message` fields over ad hoc strings for tool responses.
- All public Classes, methods, and properties should have proper XML doc comments with examples and other details on how the feature works in it's not start forward.
- Every class (Public or Internal) should have a summary xml comment describing it's purpose and usage

## Documentation guidance

- Start package docs with a high-level, beginner-friendly section that helps most users get running quickly.
- Move detailed reference material lower in the document so the opening sections stay focused.
- Prefer tables for configuration, tool surfaces, defaults, and behavior comparisons.
- Make docs reflect the real API surface and current behavior; do not document options or workflows the code does not support.
- Call out provider-specific limitations and behavior differences explicitly, especially when they differ from SQL Server or from other providers.
- Keep examples realistic and minimal, then add deeper detail later in the document when needed.

## Publishing notes

- Packable projects live under `src/`.
- Tests and samples must remain non-packable.
- Update `RepositoryUrl` in `Directory.Build.props` before the first public package release.