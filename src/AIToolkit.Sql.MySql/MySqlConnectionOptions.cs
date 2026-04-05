using MySqlConnector;

namespace AIToolkit.Sql.MySql;

/// <summary>
/// Describes a MySQL connection in structured form.
/// </summary>
public sealed class MySqlConnectionOptions
{
    /// <summary>
    /// Gets the MySQL server host name.
    /// </summary>
    public required string Server { get; init; }

    /// <summary>
    /// Gets the MySQL server port.
    /// </summary>
    public uint Port { get; init; } = 3306;

    /// <summary>
    /// Gets the default database.
    /// </summary>
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
    public MySqlSslMode? SslMode { get; init; }

    /// <summary>
    /// Gets the optional connection timeout, in seconds.
    /// </summary>
    public uint? ConnectionTimeoutSeconds { get; init; }

    internal string EffectiveDatabase => string.IsNullOrWhiteSpace(Database) ? "mysql" : Database;
}