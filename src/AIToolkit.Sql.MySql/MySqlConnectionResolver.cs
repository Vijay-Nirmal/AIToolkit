using MySqlConnector;
using System.Data.Common;

namespace AIToolkit.Sql.MySql;

/// <summary>
/// Resolves host-defined MySQL connection profiles into open connections for stateless tool calls.
/// </summary>
internal sealed class MySqlConnectionResolver : ISqlConnectionProfileCatalog, ISqlConnectionOpener
{
    private readonly MySqlConnectionProfile[] _profiles;
    private readonly MySqlConnectionFactory _connectionFactory;

    public MySqlConnectionResolver(
        IEnumerable<MySqlConnectionProfile>? profiles = null,
        MySqlConnectionFactory? connectionFactory = null)
    {
        _profiles = profiles?.ToArray() ?? Array.Empty<MySqlConnectionProfile>();
        _connectionFactory = connectionFactory ?? new MySqlConnectionFactory();
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
                        var server = builder.Port == 3306 ? builder.Server : $"{builder.Server}:{builder.Port}";
                        return new SqlConnectionProfileSummary(
                            profile.Name,
                            server,
                            string.IsNullOrWhiteSpace(builder.Database) ? null : builder.Database);
                    })
                .ToArray();

        return ValueTask.FromResult(profiles);
    }

    internal ValueTask<MySqlConnection> OpenConnectionAsync(
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
            throw new InvalidOperationException("No MySQL connections have been registered.");
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

    private static string BuildConnectionString(MySqlConnectionProfile profile, string? database)
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

    private static MySqlConnectionStringBuilder CreateConnectionStringBuilder(MySqlConnectionProfile profile) =>
        new(BuildConnectionString(profile, database: null));

    private static string BuildConnectionString(MySqlConnectionOptions options, string? database)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = options.Server,
            Port = options.Port,
            Database = string.IsNullOrWhiteSpace(database) ? options.EffectiveDatabase : database,
        };

        if (!string.IsNullOrWhiteSpace(options.UserId))
        {
            builder.UserID = options.UserId;
        }

        if (!string.IsNullOrWhiteSpace(options.Password))
        {
            builder.Password = options.Password;
        }

        if (options.SslMode is { } sslMode)
        {
            builder.SslMode = sslMode;
        }

        if (options.ConnectionTimeoutSeconds is uint timeout)
        {
            builder.ConnectionTimeout = timeout;
        }

        return builder.ConnectionString;
    }

    private static string OverrideDatabase(string connectionString, string database)
    {
        var builder = new MySqlConnectionStringBuilder(connectionString)
        {
            Database = database,
        };

        return builder.ConnectionString;
    }
}