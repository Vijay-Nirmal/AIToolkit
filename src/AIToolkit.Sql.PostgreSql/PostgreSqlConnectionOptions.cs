using Npgsql;

namespace AIToolkit.Sql.PostgreSql;

/// <summary>
/// Describes a PostgreSQL connection in structured form.
/// </summary>
/// <remarks>
/// Use this type when you want to configure a connection without supplying a raw connection string.
/// The package converts these settings into a PostgreSQL connection string whenever a stateless tool call resolves the named profile.
/// </remarks>
public sealed class PostgreSqlConnectionOptions
{
    /// <summary>
    /// Gets the PostgreSQL host name.
    /// </summary>
    public required string Host { get; init; }

    /// <summary>
    /// Gets the PostgreSQL port.
    /// </summary>
    public int Port { get; init; } = 5432;

    /// <summary>
    /// Gets the default database to connect to.
    /// </summary>
    public string? Database { get; init; }

    /// <summary>
    /// Gets the PostgreSQL user name.
    /// </summary>
    public string? Username { get; init; }

    /// <summary>
    /// Gets the password for <see cref="Username"/>.
    /// </summary>
    public string? Password { get; init; }

    /// <summary>
    /// Gets the SSL mode.
    /// </summary>
    public SslMode? SslMode { get; init; }

    /// <summary>
    /// Gets the application name reported to PostgreSQL.
    /// </summary>
    public string ApplicationName { get; init; } = "AIToolkit";

    /// <summary>
    /// Gets the optional connection timeout, in seconds.
    /// </summary>
    public int? TimeoutSeconds { get; init; }

    internal string EffectiveDatabase => string.IsNullOrWhiteSpace(Database) ? "postgres" : Database;
}