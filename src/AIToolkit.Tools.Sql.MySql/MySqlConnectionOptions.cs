using MySqlConnector;

namespace AIToolkit.Tools.Sql.MySql;

/// <summary>
/// Describes a MySQL connection in structured form.
/// </summary>
/// <remarks>
/// Use this type when you prefer strongly typed configuration over a raw connection string. The resolver converts these values into a
/// <see cref="MySqlConnectionStringBuilder"/> whenever a named profile is opened for a tool call.
/// </remarks>
public sealed class MySqlConnectionOptions
{
    /// <summary>
    /// Gets the MySQL server host name.
    /// </summary>
    public required string Server { get; init; }

    /// <summary>
    /// Gets the MySQL server port.
    /// </summary>
    /// <remarks>
    /// The default value is <c>3306</c>.
    /// </remarks>
    public uint Port { get; init; } = 3306;

    /// <summary>
    /// Gets the default database.
    /// </summary>
    /// <remarks>
    /// When omitted, the provider falls back to <c>mysql</c> for metadata-oriented operations.
    /// </remarks>
    public string? Database { get; init; }

    /// <summary>
    /// Gets the login user name.
    /// </summary>
    public string? UserId { get; init; }

    /// <summary>
    /// Gets the password for <see cref="UserId"/>.
    /// </summary>
    public string? Password { get; init; }

    /// <summary>
    /// Gets the SSL mode.
    /// </summary>
    /// <remarks>
    /// Leave this unset to let the MySQL client use its default TLS behavior.
    /// </remarks>
    public MySqlSslMode? SslMode { get; init; }

    /// <summary>
    /// Gets the optional connection timeout, in seconds.
    /// </summary>
    /// <remarks>
    /// When <see langword="null"/>, the MySQL client uses its own timeout default.
    /// </remarks>
    public uint? ConnectionTimeoutSeconds { get; init; }

    internal string EffectiveDatabase => string.IsNullOrWhiteSpace(Database) ? "mysql" : Database;
}
