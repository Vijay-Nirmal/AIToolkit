using Npgsql;
using System.Data.Common;

namespace AIToolkit.Tools.Sql.PostgreSql;

/// <summary>
/// Resolves host-defined PostgreSQL connection profiles into open connections for stateless tool calls.
/// </summary>
/// <remarks>
/// This type is the PostgreSQL implementation of both profile discovery and connection opening. It applies optional per-call database
/// overrides and keeps provider-specific connection string rules close to the Npgsql client library. It is the bridge between the shared
/// <see cref="ISqlConnectionProfileCatalog"/> and <see cref="ISqlConnectionOpener"/> abstractions and the PostgreSQL-specific
/// <see cref="NpgsqlConnectionStringBuilder"/> behavior used by this provider package.
/// </remarks>
/// <param name="profiles">The PostgreSQL profiles that may be selected by name.</param>
/// <param name="connectionFactory">The factory used to create and open <see cref="NpgsqlConnection"/> instances.</param>
internal sealed class PostgreSqlConnectionResolver : ISqlConnectionProfileCatalog, ISqlConnectionOpener
{
    private readonly PostgreSqlConnectionProfile[] _profiles;
    private readonly PostgreSqlConnectionFactory _connectionFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSqlConnectionResolver"/> class.
    /// </summary>
    /// <param name="profiles">The named PostgreSQL connection profiles that tools may resolve at runtime.</param>
    /// <param name="connectionFactory">
    /// The factory used to materialize <see cref="NpgsqlConnection"/> instances. Tests commonly replace this to avoid live database access.
    /// </param>
    public PostgreSqlConnectionResolver(
        IEnumerable<PostgreSqlConnectionProfile>? profiles = null,
        PostgreSqlConnectionFactory? connectionFactory = null)
    {
        _profiles = profiles?.ToArray() ?? Array.Empty<PostgreSqlConnectionProfile>();
        _connectionFactory = connectionFactory ?? new PostgreSqlConnectionFactory();
    }

    /// <summary>
    /// Lists the registered PostgreSQL profiles in a model-friendly summary format.
    /// </summary>
    /// <param name="cancellationToken">A token that can cancel the asynchronous operation.</param>
    /// <returns>
    /// A summary list containing each profile name plus the resolved server and default database that the shared SQL tools expose to the model.
    /// </returns>
    /// <remarks>
    /// This method normalizes the server text so the default PostgreSQL port is omitted from the display string while non-default ports are
    /// preserved.
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

    private static NpgsqlConnectionStringBuilder CreateConnectionStringBuilder(PostgreSqlConnectionProfile profile) =>
        new(BuildConnectionString(profile, database: null));

    private static string BuildConnectionString(PostgreSqlConnectionOptions options, string? database)
    {
        // Structured options are converted here so Npgsql-specific defaults such as the fallback database and application name
        // stay local to the PostgreSQL provider implementation.
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
