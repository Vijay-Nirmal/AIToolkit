using Microsoft.Data.Sqlite;
using System.Data.Common;

namespace AIToolkit.Tools.Sql.Sqlite;

/// <summary>
/// Resolves host-defined SQLite connection profiles into open connections for stateless tool calls.
/// </summary>
/// <remarks>
/// SQLite does not use server and database selection in the same way as client/server providers. The resolver therefore treats the registered
/// data source as the connection target and leaves attached database selection to the metadata layer.
/// </remarks>
internal sealed class SqliteConnectionResolver : ISqlConnectionProfileCatalog, ISqlConnectionOpener
{
    private readonly SqliteConnectionProfile[] _profiles;
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteConnectionResolver(
        IEnumerable<SqliteConnectionProfile>? profiles = null,
        SqliteConnectionFactory? connectionFactory = null)
    {
        _profiles = profiles?.ToArray() ?? Array.Empty<SqliteConnectionProfile>();
        _connectionFactory = connectionFactory ?? new SqliteConnectionFactory();
    }

    public ValueTask<IReadOnlyList<SqlConnectionProfileSummary>> ListProfilesAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SqlConnectionProfileSummary> profiles =
            _profiles
                .Select(
                    profile =>
                    {
                        var builder = CreateConnectionStringBuilder(profile);
                        return new SqlConnectionProfileSummary(profile.Name, builder.DataSource, "main");
                    })
                .ToArray();

        return ValueTask.FromResult(profiles);
    }

    internal ValueTask<SqliteConnection> OpenConnectionAsync(SqlConnectionTarget target, CancellationToken cancellationToken = default) =>
        _connectionFactory.OpenConnectionAsync(ResolveConnectionString(target), cancellationToken);

    async ValueTask<DbConnection> ISqlConnectionOpener.OpenConnectionAsync(SqlConnectionTarget target, CancellationToken cancellationToken) =>
        await OpenConnectionAsync(target, cancellationToken).ConfigureAwait(false);

    private string ResolveConnectionString(SqlConnectionTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (_profiles.Length == 0)
        {
            throw new InvalidOperationException("No SQLite connections have been registered.");
        }

        if (string.IsNullOrWhiteSpace(target.ConnectionName))
        {
            throw new InvalidOperationException("connectionName must be provided.");
        }

        var profile = _profiles.FirstOrDefault(item => string.Equals(item.Name, target.ConnectionName, StringComparison.OrdinalIgnoreCase));

        return profile is null
            ? throw new InvalidOperationException($"Connection '{target.ConnectionName}' was not found.")
            : BuildConnectionString(profile);
    }

    private static string BuildConnectionString(SqliteConnectionProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.ConnectionString))
        {
            return profile.ConnectionString;
        }

        if (profile.ConnectionOptions is not null)
        {
            return BuildConnectionString(profile.ConnectionOptions);
        }

        throw new InvalidOperationException($"Connection '{profile.Name}' must define either ConnectionString or ConnectionOptions.");
    }

    private static SqliteConnectionStringBuilder CreateConnectionStringBuilder(SqliteConnectionProfile profile) =>
        new(BuildConnectionString(profile));

    private static string BuildConnectionString(SqliteConnectionOptions options)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = options.DataSource,
        };

        if (options.Mode is { } mode)
        {
            builder.Mode = mode;
        }

        if (options.Cache is { } cache)
        {
            builder.Cache = cache;
        }

        if (!string.IsNullOrWhiteSpace(options.Password))
        {
            builder.Password = options.Password;
        }

        return builder.ConnectionString;
    }
}