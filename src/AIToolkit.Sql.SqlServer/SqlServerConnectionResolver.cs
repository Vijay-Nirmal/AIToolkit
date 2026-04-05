using Microsoft.Data.SqlClient;
using System.Data.Common;

namespace AIToolkit.Sql.SqlServer;

/// <summary>
/// Resolves host-defined SQL Server connection profiles into open connections for stateless tool calls.
/// </summary>
/// <remarks>
/// This type is the SQL Server implementation of both profile discovery and connection opening. It stays provider-specific because connection
/// string building, authentication options, and database overrides differ across ADO.NET providers.
/// </remarks>
internal sealed class SqlServerConnectionResolver : ISqlConnectionProfileCatalog, ISqlConnectionOpener
{
    private readonly SqlServerConnectionProfile[] _profiles;
    private readonly SqlServerConnectionFactory _connectionFactory;

    public SqlServerConnectionResolver(
        IEnumerable<SqlServerConnectionProfile>? profiles = null,
        SqlServerConnectionFactory? connectionFactory = null)
    {
        _profiles = profiles?.ToArray() ?? Array.Empty<SqlServerConnectionProfile>();
        _connectionFactory = connectionFactory ?? new SqlServerConnectionFactory();
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
                        return new SqlConnectionProfileSummary(
                            profile.Name,
                            builder.DataSource,
                            string.IsNullOrWhiteSpace(builder.InitialCatalog) ? null : builder.InitialCatalog);
                    })
                .ToArray();

        return ValueTask.FromResult(profiles);
    }

    internal ValueTask<SqlConnection> OpenConnectionAsync(
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
            throw new InvalidOperationException("No SQL Server connections have been registered.");
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

    private static string BuildConnectionString(SqlServerConnectionProfile profile, string? database)
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

    private static SqlConnectionStringBuilder CreateConnectionStringBuilder(SqlServerConnectionProfile profile) =>
        new(BuildConnectionString(profile, database: null));

    private static string BuildConnectionString(SqlServerConnectionOptions options, string? database)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = options.Server,
            InitialCatalog = string.IsNullOrWhiteSpace(database) ? options.EffectiveDatabase : database,
            Encrypt = options.Encrypt,
            TrustServerCertificate = options.TrustServerCertificate,
            ApplicationName = options.ApplicationName,
        };

        if (options.ConnectTimeoutSeconds is int connectTimeoutSeconds)
        {
            builder.ConnectTimeout = connectTimeoutSeconds;
        }

        if (!string.IsNullOrWhiteSpace(options.Authentication))
        {
            builder["Authentication"] = options.Authentication;

            if (!string.IsNullOrWhiteSpace(options.UserId))
            {
                builder.UserID = options.UserId;
            }

            if (!string.IsNullOrWhiteSpace(options.Password))
            {
                builder.Password = options.Password;
            }
        }
        else if (options.UsesIntegratedSecurity)
        {
            builder.IntegratedSecurity = true;
        }
        else
        {
            builder.UserID = options.UserId;
            builder.Password = options.Password;
        }

        return builder.ConnectionString;
    }

    private static string OverrideDatabase(string connectionString, string database)
    {
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = database,
        };

        return builder.ConnectionString;
    }
}