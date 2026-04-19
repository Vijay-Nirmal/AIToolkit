using Microsoft.Data.Sqlite;

namespace AIToolkit.Tools.Sql.Sqlite;

/// <summary>
/// Describes a SQLite connection in structured form.
/// </summary>
/// <remarks>
/// Use this type when you prefer strongly typed configuration over a raw connection string. The resolver converts these values into a
/// <see cref="SqliteConnectionStringBuilder"/> each time a named profile is opened for a tool call.
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// var profile = new SqliteConnectionProfile
/// {
///     Name = "main-db",
///     ConnectionOptions = new SqliteConnectionOptions
///     {
///         DataSource = "app.db",
///         Mode = SqliteOpenMode.ReadWriteCreate
///     }
/// };
/// ]]></code>
/// </example>
public sealed class SqliteConnectionOptions
{
    /// <summary>
    /// Gets the SQLite data source path or URI.
    /// </summary>
    public required string DataSource { get; init; }

    /// <summary>
    /// Gets the optional SQLite open mode.
    /// </summary>
    /// <remarks>
    /// Leave this unset to let the provider use its default open behavior.
    /// </remarks>
    public SqliteOpenMode? Mode { get; init; }

    /// <summary>
    /// Gets the optional SQLite cache mode.
    /// </summary>
    /// <remarks>
    /// This influences how connections share caches for the selected data source.
    /// </remarks>
    public SqliteCacheMode? Cache { get; init; }

    /// <summary>
    /// Gets the optional SQLite password.
    /// </summary>
    /// <remarks>
    /// This is only meaningful when the underlying SQLite build and configuration support password-protected databases.
    /// </remarks>
    public string? Password { get; init; }
}
