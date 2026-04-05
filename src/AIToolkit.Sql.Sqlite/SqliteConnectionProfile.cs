namespace AIToolkit.Sql.Sqlite;

/// <summary>
/// Defines a named SQLite connection that can be surfaced to an AI model.
/// </summary>
public sealed class SqliteConnectionProfile
{
    /// <summary>
    /// Gets the logical name exposed to the model for this connection.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the raw SQLite connection string for this named connection.
    /// </summary>
    public string? ConnectionString { get; init; }

    /// <summary>
    /// Gets the structured SQLite connection settings for this named connection.
    /// </summary>
    public SqliteConnectionOptions? ConnectionOptions { get; init; }
}