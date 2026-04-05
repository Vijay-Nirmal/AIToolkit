# Contributing

## Prerequisites

- .NET 10 SDK.
- Git.

## Local workflow

```bash
dotnet restore AIToolkit.slnx
dotnet build AIToolkit.slnx -c Release
dotnet test AIToolkit.slnx -c Release
```

## Package contribution checklist

1. Add the library under `src/`.
2. Add tests under `tests/`.
3. Add a sample under `samples/` if the API benefits from executable usage.
4. Add or update package-specific documentation.
5. Keep package references versionless in project files and use `Directory.Packages.props`.

## Pull requests

- Keep changes focused.
- Include tests for public API behavior.
- Update docs when behavior or package structure changes.