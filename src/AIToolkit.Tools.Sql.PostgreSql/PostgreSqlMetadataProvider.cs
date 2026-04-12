using Npgsql;
using System.Globalization;
using System.Text;

namespace AIToolkit.Tools.Sql.PostgreSql;

/// <summary>
/// Implements PostgreSQL metadata discovery and object-definition lookup on top of the shared provider contracts.
/// </summary>
internal sealed class PostgreSqlMetadataProvider(PostgreSqlConnectionResolver connectionResolver) : ISqlMetadataProvider
{
    private readonly PostgreSqlConnectionResolver _connectionResolver = connectionResolver ?? throw new ArgumentNullException(nameof(connectionResolver));

    public async ValueTask<IReadOnlyList<SqlDatabaseInfo>> ListDatabasesAsync(
        SqlConnectionTarget target,
        CancellationToken cancellationToken = default)
    {
        var items = await ExecuteScalarStringsAsync(target, PostgreSqlCatalogQueries.ListDatabases, null, cancellationToken).ConfigureAwait(false);
        return items.Select(static name => new SqlDatabaseInfo(name)).ToArray();
    }

    public async ValueTask<IReadOnlyList<SqlSchemaInfo>> ListSchemasAsync(
        SqlConnectionTarget target,
        CancellationToken cancellationToken = default)
    {
        var items = await ExecuteScalarStringsAsync(target, PostgreSqlCatalogQueries.ListSchemas, null, cancellationToken).ConfigureAwait(false);
        return items.Select(static name => new SqlSchemaInfo(name)).ToArray();
    }

    public async ValueTask<IReadOnlyList<SqlTableInfo>> ListTablesAsync(
        SqlConnectionTarget target,
        CancellationToken cancellationToken = default)
    {
        var items = await ExecuteScalarStringsAsync(target, PostgreSqlCatalogQueries.ListTables, null, cancellationToken).ConfigureAwait(false);
        return items.Select(static name => new SqlTableInfo(ParseObjectIdentifier(name))).ToArray();
    }

    public async ValueTask<IReadOnlyList<SqlViewInfo>> ListViewsAsync(
        SqlConnectionTarget target,
        CancellationToken cancellationToken = default)
    {
        var items = await ExecuteScalarStringsAsync(target, PostgreSqlCatalogQueries.ListViews, null, cancellationToken).ConfigureAwait(false);
        return items.Select(static name => new SqlViewInfo(ParseObjectIdentifier(name))).ToArray();
    }

    public async ValueTask<IReadOnlyList<SqlRoutineInfo>> ListFunctionsAsync(
        SqlConnectionTarget target,
        CancellationToken cancellationToken = default)
    {
        var items = await ExecuteScalarStringsAsync(target, PostgreSqlCatalogQueries.ListFunctions, null, cancellationToken).ConfigureAwait(false);
        return items.Select(static name => new SqlRoutineInfo(ParseObjectIdentifier(name), SqlRoutineKind.Function)).ToArray();
    }

    public async ValueTask<IReadOnlyList<SqlRoutineInfo>> ListProceduresAsync(
        SqlConnectionTarget target,
        CancellationToken cancellationToken = default)
    {
        var items = await ExecuteScalarStringsAsync(target, PostgreSqlCatalogQueries.ListProcedures, null, cancellationToken).ConfigureAwait(false);
        return items.Select(static name => new SqlRoutineInfo(ParseObjectIdentifier(name), SqlRoutineKind.Procedure)).ToArray();
    }

    public async ValueTask<SqlObjectDefinition> GetObjectDefinitionAsync(
        SqlConnectionTarget target,
        string? schemaName,
        string objectName,
        SqlObjectKind objectKind,
        CancellationToken cancellationToken = default)
    {
        var identifier = NormalizeIdentifier(schemaName, objectName);
        var kind = objectKind == SqlObjectKind.Unknown
            ? await ResolveObjectKindAsync(target, identifier.Schema!, identifier.Name, cancellationToken).ConfigureAwait(false)
            : objectKind;

        return kind switch
        {
            SqlObjectKind.Table => await GetTableDefinitionAsync(target, identifier, cancellationToken).ConfigureAwait(false),
            SqlObjectKind.View => await GetViewDefinitionAsync(target, identifier, cancellationToken).ConfigureAwait(false),
            SqlObjectKind.Function => await GetRoutineDefinitionAsync(target, identifier, kind, PostgreSqlCatalogQueries.FunctionDefinition, cancellationToken).ConfigureAwait(false),
            SqlObjectKind.Procedure => await GetRoutineDefinitionAsync(target, identifier, kind, PostgreSqlCatalogQueries.ProcedureDefinition, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Object kind '{kind}' is not supported for definition lookup."),
        };
    }

    public async ValueTask<SqlSchemaOverview> GetSchemaOverviewAsync(
        SqlConnectionTarget target,
        CancellationToken cancellationToken = default)
    {
        var tables = await ListTablesAsync(target, cancellationToken).ConfigureAwait(false);
        var views = await ListViewsAsync(target, cancellationToken).ConfigureAwait(false);
        var functions = await ListFunctionsAsync(target, cancellationToken).ConfigureAwait(false);
        var procedures = await ListProceduresAsync(target, cancellationToken).ConfigureAwait(false);
        return new SqlSchemaOverview(tables, views, functions, procedures);
    }

    private async ValueTask<SqlObjectKind> ResolveObjectKindAsync(
        SqlConnectionTarget target,
        string schemaName,
        string objectName,
        CancellationToken cancellationToken)
    {
        var items = await ExecuteScalarStringsAsync(
            target,
            PostgreSqlCatalogQueries.ResolveObjectKind,
            [
                new NpgsqlParameter("schemaName", schemaName),
                new NpgsqlParameter("objectName", objectName),
            ],
            cancellationToken).ConfigureAwait(false);

        return items.Count switch
        {
            0 => throw new InvalidOperationException($"Object '{schemaName}.{objectName}' was not found."),
            _ => Enum.TryParse<SqlObjectKind>(items[0], ignoreCase: true, out var kind) ? kind : SqlObjectKind.Unknown,
        };
    }

    private async ValueTask<SqlObjectDefinition> GetViewDefinitionAsync(
        SqlConnectionTarget target,
        SqlObjectIdentifier identifier,
        CancellationToken cancellationToken)
    {
        var items = await ExecuteScalarStringsAsync(
            target,
            PostgreSqlCatalogQueries.ViewDefinition,
            [
                new NpgsqlParameter("schemaName", identifier.Schema!),
                new NpgsqlParameter("objectName", identifier.Name),
            ],
            cancellationToken).ConfigureAwait(false);

        if (items.Count == 0)
        {
            throw new InvalidOperationException($"No definition exists for object '{identifier.FullyQualifiedName}'.");
        }

        var definition = $"CREATE VIEW \"{identifier.Schema}\".\"{identifier.Name}\" AS{Environment.NewLine}{items[0]}";
        return new SqlObjectDefinition(identifier, SqlObjectKind.View, definition);
    }

    private async ValueTask<SqlObjectDefinition> GetRoutineDefinitionAsync(
        SqlConnectionTarget target,
        SqlObjectIdentifier identifier,
        SqlObjectKind kind,
        string query,
        CancellationToken cancellationToken)
    {
        var items = await ExecuteScalarStringsAsync(
            target,
            query,
            [
                new NpgsqlParameter("schemaName", identifier.Schema!),
                new NpgsqlParameter("objectName", identifier.Name),
            ],
            cancellationToken).ConfigureAwait(false);

        if (items.Count == 0)
        {
            throw new InvalidOperationException($"No definition exists for object '{identifier.FullyQualifiedName}'.");
        }

        return new SqlObjectDefinition(identifier, kind, items[0]);
    }

    private async ValueTask<SqlObjectDefinition> GetTableDefinitionAsync(
        SqlConnectionTarget target,
        SqlObjectIdentifier identifier,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionResolver.OpenConnectionAsync(target, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = PostgreSqlCatalogQueries.TableColumns;
        command.Parameters.Add(new NpgsqlParameter("schemaName", identifier.Schema!));
        command.Parameters.Add(new NpgsqlParameter("objectName", identifier.Name));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var columns = new List<string>();

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var columnName = reader.GetString(0);
            var dataType = reader.GetString(1);
            var isNullable = reader.GetBoolean(2);
            columns.Add($"    \"{columnName}\" {dataType} {(isNullable ? "NULL" : "NOT NULL")}");
        }

        if (columns.Count == 0)
        {
            throw new InvalidOperationException($"Table '{identifier.FullyQualifiedName}' was not found.");
        }

        var builder = new StringBuilder();
        builder.Append("CREATE TABLE \"");
        builder.Append(identifier.Schema);
        builder.Append("\".\"");
        builder.Append(identifier.Name);
        builder.AppendLine("\" (");
        builder.AppendLine(string.Join("," + Environment.NewLine, columns));
        builder.Append(')');

        return new SqlObjectDefinition(identifier, SqlObjectKind.Table, builder.ToString());
    }

    private async ValueTask<IReadOnlyList<string>> ExecuteScalarStringsAsync(
        SqlConnectionTarget target,
        string query,
        IEnumerable<NpgsqlParameter>? parameters,
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

    private static SqlObjectIdentifier NormalizeIdentifier(string? schemaName, string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName))
        {
            throw new ArgumentException("Object name is required.", nameof(objectName));
        }

        if (string.IsNullOrWhiteSpace(schemaName) && objectName.Contains('.', StringComparison.Ordinal))
        {
            return ParseObjectIdentifier(objectName);
        }

        return new SqlObjectIdentifier(string.IsNullOrWhiteSpace(schemaName) ? "public" : schemaName, objectName);
    }

    private static SqlObjectIdentifier ParseObjectIdentifier(string name)
    {
        var parts = name.Split('.', 2, StringSplitOptions.TrimEntries);
        return parts.Length == 2
            ? new SqlObjectIdentifier(parts[0], parts[1])
            : new SqlObjectIdentifier("public", name);
    }
}