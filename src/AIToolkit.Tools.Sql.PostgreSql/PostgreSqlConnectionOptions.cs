using Npgsql;

namespace AIToolkit.Tools.Sql.PostgreSql;

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
    /// <remarks>
    /// The default value is <c>5432</c>.
    /// </remarks>
    public int Port { get; init; } = 5432;

    /// <summary>
    /// Gets the default database to connect to.
    /// </summary>
    /// <remarks>
    /// When omitted, the provider falls back to <c>postgres</c>.
    /// </remarks>
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
    /// <remarks>
    /// Leave this unset to let Npgsql use its default TLS behavior.
    /// </remarks>
    public SslMode? SslMode { get; init; }

    /// <summary>
    /// Gets the application name reported to PostgreSQL.
    /// </summary>
    /// <remarks>
    /// The default value is <c>AIToolkit</c>.
    /// </remarks>
    public string ApplicationName { get; init; } = "AIToolkit";

    /// <summary>
    /// Gets the optional connection timeout, in seconds.
    /// </summary>
    /// <remarks>
    /// When <see langword="null"/>, Npgsql uses its own timeout default.
    /// </remarks>
    public int? TimeoutSeconds { get; init; }

    internal string EffectiveDatabase => string.IsNullOrWhiteSpace(Database) ? "postgres" : Database;
}
