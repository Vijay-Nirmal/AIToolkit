using Microsoft.Data.SqlClient;
using System.Data;
using System.Globalization;
using System.Text;

namespace AIToolkit.Tools.Sql.SqlServer;

/// <summary>
/// Implements SQL Server metadata discovery and object-definition lookup on top of the shared provider contracts.
/// </summary>
/// <remarks>
/// The shared package defines the metadata models, but the actual catalog queries and object-shape rules are provider-specific, so they stay here.
/// </remarks>
internal sealed class SqlServerMetadataProvider(SqlServerConnectionResolver connectionResolver) : ISqlMetadataProvider
{
    private readonly SqlServerConnectionResolver _connectionResolver = connectionResolver ?? throw new ArgumentNullException(nameof(connectionResolver));

    public async ValueTask<IReadOnlyList<SqlDatabaseInfo>> ListDatabasesAsync(
        SqlConnectionTarget target,
        CancellationToken cancellationToken = default)
    {
        var items = await ExecuteScalarStringsAsync(target, SqlServerCatalogQueries.ListDatabases, null, cancellationToken)
            .ConfigureAwait(false);

        return items.Select(static name => new SqlDatabaseInfo(name)).ToArray();
    }

    public async ValueTask<IReadOnlyList<SqlSchemaInfo>> ListSchemasAsync(
        SqlConnectionTarget target,
        CancellationToken cancellationToken = default)
    {
        var items = await ExecuteScalarStringsAsync(target, SqlServerCatalogQueries.ListSchemas, null, cancellationToken)
            .ConfigureAwait(false);

        return items.Select(static name => new SqlSchemaInfo(name)).ToArray();
    }

    public async ValueTask<IReadOnlyList<SqlTableInfo>> ListTablesAsync(
        SqlConnectionTarget target,
        CancellationToken cancellationToken = default)
    {
        var items = await ExecuteScalarStringsAsync(target, SqlServerCatalogQueries.ListTables, null, cancellationToken)
            .ConfigureAwait(false);

        return items.Select(static name => new SqlTableInfo(ParseObjectIdentifier(name))).ToArray();
    }

    public async ValueTask<IReadOnlyList<SqlViewInfo>> ListViewsAsync(
        SqlConnectionTarget target,
        CancellationToken cancellationToken = default)
    {
        var items = await ExecuteScalarStringsAsync(target, SqlServerCatalogQueries.ListViews, null, cancellationToken)
            .ConfigureAwait(false);

        return items.Select(static name => new SqlViewInfo(ParseObjectIdentifier(name))).ToArray();
    }

    public async ValueTask<IReadOnlyList<SqlRoutineInfo>> ListFunctionsAsync(
        SqlConnectionTarget target,
        CancellationToken cancellationToken = default)
    {
        var items = await ExecuteScalarStringsAsync(target, SqlServerCatalogQueries.ListFunctions, null, cancellationToken)
            .ConfigureAwait(false);

        return items.Select(static name => new SqlRoutineInfo(ParseObjectIdentifier(name), SqlRoutineKind.Function)).ToArray();
    }

    public async ValueTask<IReadOnlyList<SqlRoutineInfo>> ListProceduresAsync(
        SqlConnectionTarget target,
        CancellationToken cancellationToken = default)
    {
        var items = await ExecuteScalarStringsAsync(target, SqlServerCatalogQueries.ListProcedures, null, cancellationToken)
            .ConfigureAwait(false);

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
            SqlObjectKind.View => await GetSqlModuleDefinitionAsync(target, identifier, kind, "'V'", cancellationToken).ConfigureAwait(false),
            SqlObjectKind.Function => await GetSqlModuleDefinitionAsync(target, identifier, kind, "'FN', 'IF', 'TF'", cancellationToken).ConfigureAwait(false),
            SqlObjectKind.Procedure => await GetSqlModuleDefinitionAsync(target, identifier, kind, "'P', 'PC'", cancellationToken).ConfigureAwait(false),
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
            SqlServerCatalogQueries.ResolveObjectKind,
            [
                new SqlParameter("@schemaName", SqlDbType.NVarChar, 128) { Value = schemaName },
                new SqlParameter("@objectName", SqlDbType.NVarChar, 128) { Value = objectName },
            ],
            cancellationToken).ConfigureAwait(false);

        return items.Count switch
        {
            0 => throw new InvalidOperationException($"Object '{schemaName}.{objectName}' was not found."),
            _ => Enum.TryParse<SqlObjectKind>(items[0], ignoreCase: true, out var kind) ? kind : SqlObjectKind.Unknown,
        };
    }

    private async ValueTask<SqlObjectDefinition> GetSqlModuleDefinitionAsync(
        SqlConnectionTarget target,
        SqlObjectIdentifier identifier,
        SqlObjectKind kind,
        string typeFilter,
        CancellationToken cancellationToken)
    {
        var query = SqlServerCatalogQueries.SqlModuleDefinition.Replace("{0}", typeFilter, StringComparison.Ordinal);
        var items = await ExecuteScalarStringsAsync(
            target,
            query,
            [
                new SqlParameter("@schemaName", SqlDbType.NVarChar, 128) { Value = identifier.Schema! },
                new SqlParameter("@objectName", SqlDbType.NVarChar, 128) { Value = identifier.Name },
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
        command.CommandText = SqlServerCatalogQueries.TableColumns;
        command.Parameters.Add(new SqlParameter("@schemaName", SqlDbType.NVarChar, 128) { Value = identifier.Schema! });
        command.Parameters.Add(new SqlParameter("@objectName", SqlDbType.NVarChar, 128) { Value = identifier.Name });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var columns = new List<string>();

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var columnName = reader.GetString(0);
            var dataType = FormatColumnType(reader);
            var isNullable = string.Equals(reader.GetString(2), "YES", StringComparison.OrdinalIgnoreCase);
            columns.Add($"    [{columnName}] {dataType} {(isNullable ? "NULL" : "NOT NULL")}");
        }

        if (columns.Count == 0)
        {
            throw new InvalidOperationException($"Table '{identifier.FullyQualifiedName}' was not found.");
        }

        var builder = new StringBuilder();
        builder.Append("CREATE TABLE [");
        builder.Append(identifier.Schema);
        builder.Append("].[");
        builder.Append(identifier.Name);
        builder.AppendLine("] (");
        builder.AppendLine(string.Join("," + Environment.NewLine, columns));
        builder.Append(')');

        return new SqlObjectDefinition(identifier, SqlObjectKind.Table, builder.ToString());
    }

    private async ValueTask<IReadOnlyList<string>> ExecuteScalarStringsAsync(
        SqlConnectionTarget target,
        string query,
        IEnumerable<SqlParameter>? parameters,
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

        return new SqlObjectIdentifier(string.IsNullOrWhiteSpace(schemaName) ? "dbo" : schemaName, objectName);
    }

    private static SqlObjectIdentifier ParseObjectIdentifier(string name)
    {
        var parts = name.Split('.', 2, StringSplitOptions.TrimEntries);
        return parts.Length == 2
            ? new SqlObjectIdentifier(parts[0], parts[1])
            : new SqlObjectIdentifier("dbo", name);
    }

    private static string FormatColumnType(SqlDataReader reader)
    {
        var dataType = reader.GetString(1);
        var maxLength = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4);
        var precision = reader.IsDBNull(5) ? (byte?)null : reader.GetByte(5);
        var scale = reader.IsDBNull(6) ? (int?)null : Convert.ToInt32(reader.GetValue(6), CultureInfo.InvariantCulture);

        return dataType switch
        {
            "nvarchar" or "varchar" or "nchar" or "char" or "varbinary" or "binary" => maxLength switch
            {
                null => dataType,
                -1 => $"{dataType}(MAX)",
                _ => $"{dataType}({maxLength})",
            },
            "decimal" or "numeric" when precision is not null && scale is not null =>
                $"{dataType}({precision.Value},{scale.Value})",
            _ => dataType,
        };
    }
}