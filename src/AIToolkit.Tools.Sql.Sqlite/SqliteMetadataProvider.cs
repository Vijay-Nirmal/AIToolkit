using Microsoft.Data.Sqlite;
using System.Globalization;

namespace AIToolkit.Tools.Sql.Sqlite;

/// <summary>
/// Implements SQLite metadata discovery and object-definition lookup on top of the shared provider contracts.
/// </summary>
internal sealed class SqliteMetadataProvider(SqliteConnectionResolver connectionResolver) : ISqlMetadataProvider
{
    private readonly SqliteConnectionResolver _connectionResolver = connectionResolver ?? throw new ArgumentNullException(nameof(connectionResolver));

    public async ValueTask<IReadOnlyList<SqlDatabaseInfo>> ListDatabasesAsync(SqlConnectionTarget target, CancellationToken cancellationToken = default)
    {
        var items = await ReadDatabaseNamesAsync(target, cancellationToken).ConfigureAwait(false);
        return items.Select(static name => new SqlDatabaseInfo(name)).ToArray();
    }

    public async ValueTask<IReadOnlyList<SqlSchemaInfo>> ListSchemasAsync(SqlConnectionTarget target, CancellationToken cancellationToken = default)
    {
        var items = await ReadDatabaseNamesAsync(target, cancellationToken).ConfigureAwait(false);
        return items.Select(static name => new SqlSchemaInfo(name)).ToArray();
    }

    public async ValueTask<IReadOnlyList<SqlTableInfo>> ListTablesAsync(SqlConnectionTarget target, CancellationToken cancellationToken = default)
    {
        var catalog = GetCatalogName(target);
        var items = await ExecuteScalarStringsAsync(target, SqliteCatalogQueries.ListObjects(catalog, "table"), null, cancellationToken).ConfigureAwait(false);
        return items.Select(name => new SqlTableInfo(new SqlObjectIdentifier(catalog, name))).ToArray();
    }

    public async ValueTask<IReadOnlyList<SqlViewInfo>> ListViewsAsync(SqlConnectionTarget target, CancellationToken cancellationToken = default)
    {
        var catalog = GetCatalogName(target);
        var items = await ExecuteScalarStringsAsync(target, SqliteCatalogQueries.ListObjects(catalog, "view"), null, cancellationToken).ConfigureAwait(false);
        return items.Select(name => new SqlViewInfo(new SqlObjectIdentifier(catalog, name))).ToArray();
    }

    public ValueTask<IReadOnlyList<SqlRoutineInfo>> ListFunctionsAsync(SqlConnectionTarget target, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<SqlRoutineInfo>>(Array.Empty<SqlRoutineInfo>());

    public ValueTask<IReadOnlyList<SqlRoutineInfo>> ListProceduresAsync(SqlConnectionTarget target, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<SqlRoutineInfo>>(Array.Empty<SqlRoutineInfo>());

    public async ValueTask<SqlObjectDefinition> GetObjectDefinitionAsync(
        SqlConnectionTarget target,
        string? schemaName,
        string objectName,
        SqlObjectKind objectKind,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(objectName))
        {
            throw new ArgumentException("Object name is required.", nameof(objectName));
        }

        var catalog = !string.IsNullOrWhiteSpace(schemaName) ? ValidateCatalogName(schemaName) : GetCatalogName(target);
        var identifier = new SqlObjectIdentifier(catalog, objectName);
        var kind = objectKind == SqlObjectKind.Unknown
            ? await ResolveObjectKindAsync(target, catalog, objectName, cancellationToken).ConfigureAwait(false)
            : objectKind;

        if (kind is not SqlObjectKind.Table and not SqlObjectKind.View)
        {
            throw new InvalidOperationException($"Object kind '{kind}' is not supported for SQLite definition lookup.");
        }

        var objectType = kind == SqlObjectKind.Table ? "table" : "view";
        var items = await ExecuteScalarStringsAsync(
            target,
            SqliteCatalogQueries.ObjectDefinition(catalog, objectType),
            [new SqliteParameter("$objectName", objectName)],
            cancellationToken).ConfigureAwait(false);

        if (items.Count == 0)
        {
            throw new InvalidOperationException($"No definition exists for object '{identifier.FullyQualifiedName}'.");
        }

        return new SqlObjectDefinition(identifier, kind, items[0]);
    }

    public async ValueTask<SqlSchemaOverview> GetSchemaOverviewAsync(SqlConnectionTarget target, CancellationToken cancellationToken = default)
    {
        var tables = await ListTablesAsync(target, cancellationToken).ConfigureAwait(false);
        var views = await ListViewsAsync(target, cancellationToken).ConfigureAwait(false);
        return new SqlSchemaOverview(tables, views, Array.Empty<SqlRoutineInfo>(), Array.Empty<SqlRoutineInfo>());
    }

    private async ValueTask<SqlObjectKind> ResolveObjectKindAsync(
        SqlConnectionTarget target,
        string catalog,
        string objectName,
        CancellationToken cancellationToken)
    {
        var items = await ExecuteScalarStringsAsync(
            target,
            SqliteCatalogQueries.ResolveObjectKind(catalog),
            [new SqliteParameter("$objectName", objectName)],
            cancellationToken).ConfigureAwait(false);

        return items.Count switch
        {
            0 => throw new InvalidOperationException($"Object '{catalog}.{objectName}' was not found."),
            _ => Enum.TryParse<SqlObjectKind>(items[0], ignoreCase: true, out var kind) ? kind : SqlObjectKind.Unknown,
        };
    }

    private async ValueTask<IReadOnlyList<string>> ReadDatabaseNamesAsync(SqlConnectionTarget target, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionResolver.OpenConnectionAsync(target, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = SqliteCatalogQueries.DatabaseList;

        var values = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (reader.IsDBNull(1))
            {
                continue;
            }

            values.Add(reader.GetString(1));
        }

        return values;
    }

    private async ValueTask<IReadOnlyList<string>> ExecuteScalarStringsAsync(
        SqlConnectionTarget target,
        string query,
        IEnumerable<SqliteParameter>? parameters,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionResolver.OpenConnectionAsync(target, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = query;

        if (parameters is not null)
        {
            command.Parameters.AddRange(parameters.ToArray());
        }

        var values = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (reader.IsDBNull(0))
            {
                continue;
            }

            values.Add(Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture) ?? string.Empty);
        }

        return values;
    }

    private static string GetCatalogName(SqlConnectionTarget target) =>
        ValidateCatalogName(string.IsNullOrWhiteSpace(target.Database) ? "main" : target.Database);

    private static string ValidateCatalogName(string catalog)
    {
        foreach (var character in catalog)
        {
            if (!(char.IsLetterOrDigit(character) || character == '_'))
            {
                throw new InvalidOperationException("SQLite catalog names must contain only letters, digits, or underscores.");
            }
        }

        return catalog;
    }
}