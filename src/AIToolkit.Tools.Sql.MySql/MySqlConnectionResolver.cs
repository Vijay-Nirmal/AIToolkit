using MySqlConnector;
using System.Data.Common;

namespace AIToolkit.Tools.Sql.MySql;

/// <summary>
/// Resolves host-defined MySQL connection profiles into open connections for stateless tool calls.
/// </summary>
/// <remarks>
/// This type is the MySQL implementation of both profile discovery and connection opening. It applies optional per-call database overrides and
/// keeps provider-specific connection string rules close to the MySQL client library. It is the bridge between the shared
/// <see cref="ISqlConnectionProfileCatalog"/> and <see cref="ISqlConnectionOpener"/> abstractions and the MySQL-specific
/// <see cref="MySqlConnectionStringBuilder"/> rules used by the provider package.
/// </remarks>
/// <param name="profiles">The MySQL profiles that may be selected by name.</param>
/// <param name="connectionFactory">The factory used to create and open <see cref="MySqlConnection"/> instances.</param>
internal sealed class MySqlConnectionResolver : ISqlConnectionProfileCatalog, ISqlConnectionOpener
{
    private readonly MySqlConnectionProfile[] _profiles;
    private readonly MySqlConnectionFactory _connectionFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="MySqlConnectionResolver"/> class.
    /// </summary>
    /// <param name="profiles">The named MySQL connection profiles that tools may resolve at runtime.</param>
    /// <param name="connectionFactory">
    /// The factory used to materialize <see cref="MySqlConnection"/> instances. Tests commonly replace this to avoid live database access.
    /// </param>
    public MySqlConnectionResolver(
        IEnumerable<MySqlConnectionProfile>? profiles = null,
        MySqlConnectionFactory? connectionFactory = null)
    {
        _profiles = profiles?.ToArray() ?? Array.Empty<MySqlConnectionProfile>();
        _connectionFactory = connectionFactory ?? new MySqlConnectionFactory();
    }

    /// <summary>
    /// Lists the registered MySQL profiles in a model-friendly summary format.
    /// </summary>
    /// <param name="cancellationToken">A token that can cancel the asynchronous operation.</param>
    /// <returns>
    /// A summary list containing each profile name plus the resolved server and default database that the shared SQL tools expose to the model.
    /// </returns>
    /// <remarks>
    /// This method normalizes the server text so the default MySQL port is omitted from the display string while non-default ports are preserved.
    /// </remarks>
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
            // Raw connection strings stay authoritative; the resolver only swaps the database when a tool call explicitly overrides it.
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
        // Structured options are converted here so provider-specific defaults such as the fallback database remain close to the
        // MySQL client library instead of leaking into the shared SQL abstractions.
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
