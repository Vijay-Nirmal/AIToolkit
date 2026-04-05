using Npgsql;
using System.Data.Common;

namespace AIToolkit.Sql.PostgreSql;

/// <summary>
/// Resolves host-defined PostgreSQL connection profiles into open connections for stateless tool calls.
/// </summary>
internal sealed class PostgreSqlConnectionResolver : ISqlConnectionProfileCatalog, ISqlConnectionOpener
{
    private readonly PostgreSqlConnectionProfile[] _profiles;
    private readonly PostgreSqlConnectionFactory _connectionFactory;

    public PostgreSqlConnectionResolver(
        IEnumerable<PostgreSqlConnectionProfile>? profiles = null,
        PostgreSqlConnectionFactory? connectionFactory = null)
    {
        _profiles = profiles?.ToArray() ?? Array.Empty<PostgreSqlConnectionProfile>();
        _connectionFactory = connectionFactory ?? new PostgreSqlConnectionFactory();
    }

    public ValueTask<IReadOnlyList<SqlConnectionProfileSummary>> ListProfilesAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SqlConnectionProfileSummary> profiles =
            _profiles
                .Select(
                    profile =>
                    {
                        var builder = CreateConnectionStringBuilder(profile);
                        var host = string.IsNullOrWhiteSpace(builder.Host) ? "localhost" : builder.Host;
                        var server = builder.Port == 5432 ? host : $"{host}:{builder.Port}";
                        return new SqlConnectionProfileSummary(
                            profile.Name,
                            server,
                            string.IsNullOrWhiteSpace(builder.Database) ? null : builder.Database);
                    })
                .ToArray();

        return ValueTask.FromResult(profiles);
    }

    internal ValueTask<NpgsqlConnection> OpenConnectionAsync(
        SqlConnectionTarget target,
        CancellationToken cancellationToken = default) =>
        _connectionFactory.OpenConnectionAsync(ResolveConnectionString(target), cancellationToken);

    async ValueTask<DbConnection> ISqlConnectionOpener.OpenConnectionAsync(
        SqlConnectionTarget target,
        CancellationToken cancellationToken) =>
        await OpenConnectionAsync(target, cancellationToken).ConfigureAwait(false);

    private string ResolveConnectionString(SqlConnectionTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (_profiles.Length == 0)
        {
            throw new InvalidOperationException("No PostgreSQL connections have been registered.");
        }

        if (string.IsNullOrWhiteSpace(target.ConnectionName))
        {
            throw new InvalidOperationException("connectionName must be provided.");
        }

        var profile = _profiles.FirstOrDefault(
            item => string.Equals(item.Name, target.ConnectionName, StringComparison.OrdinalIgnoreCase));

        return profile is null
            ? throw new InvalidOperationException($"Connection '{target.ConnectionName}' was not found.")
            : BuildConnectionString(profile, target.Database);
    }

    private static string BuildConnectionString(PostgreSqlConnectionProfile profile, string? database)
    {
        if (!string.IsNullOrWhiteSpace(profile.ConnectionString))
        {
            return string.IsNullOrWhiteSpace(database)
                ? profile.ConnectionString
                : OverrideDatabase(profile.ConnectionString, database);
        }

        if (profile.ConnectionOptions is not null)
        {
            return BuildConnectionString(profile.ConnectionOptions, database);
        }

        throw new InvalidOperationException($"Connection '{profile.Name}' must define either ConnectionString or ConnectionOptions.");
    }

    private static NpgsqlConnectionStringBuilder CreateConnectionStringBuilder(PostgreSqlConnectionProfile profile) =>
        new(BuildConnectionString(profile, database: null));

    private static string BuildConnectionString(PostgreSqlConnectionOptions options, string? database)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = options.Host,
            Port = options.Port,
            Database = string.IsNullOrWhiteSpace(database) ? options.EffectiveDatabase : database,
            ApplicationName = options.ApplicationName,
        };

        if (!string.IsNullOrWhiteSpace(options.Username))
        {
            builder.Username = options.Username;
        }

        if (!string.IsNullOrWhiteSpace(options.Password))
        {
            builder.Password = options.Password;
        }

        if (options.SslMode is { } sslMode)
        {
            builder.SslMode = sslMode;
        }

        if (options.TimeoutSeconds is int timeoutSeconds)
        {
            builder.Timeout = timeoutSeconds;
        }

        return builder.ConnectionString;
    }

    private static string OverrideDatabase(string connectionString, string database)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = database,
        };

        return builder.ConnectionString;
    }
}