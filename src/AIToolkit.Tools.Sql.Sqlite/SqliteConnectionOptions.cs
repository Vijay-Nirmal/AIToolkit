using Microsoft.Data.Sqlite;

namespace AIToolkit.Tools.Sql.Sqlite;

/// <summary>
/// Describes a SQLite connection in structured form.
/// </summary>
public sealed class SqliteConnectionOptions
{
    /// <summary>
    /// Gets the SQLite data source path.
    /// </summary>
    public required string DataSource { get; init; }

    /// <summary>
    /// Gets the optional SQLite open mode.
    /// </summary>
    public SqliteOpenMode? Mode { get; init; }

    /// <summary>
    /// Gets the optional SQLite cache mode.
    /// </summary>
    public SqliteCacheMode? Cache { get; init; }

    /// <summary>
    /// Gets the optional SQLite password.
    /// </summary>
    public string? Password { get; init; }
}