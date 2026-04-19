namespace AIToolkit.Tools.Sql.Sqlite;

/// <summary>
/// Defines a named SQLite connection that can be surfaced to an AI model.
/// </summary>
/// <remarks>
/// Provide either <see cref="ConnectionString"/> or <see cref="ConnectionOptions"/>. The <see cref="Name"/> is the logical identifier used by
/// the generated <c>sqlite_*</c> tools, while the resolver keeps the actual file path or connection string hidden from the model.
/// </remarks>
public sealed class SqliteConnectionProfile
{
    /// <summary>
    /// Gets the logical name exposed to the model for this connection.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the raw SQLite connection string for this named connection.
    /// </summary>
    /// <remarks>
    /// Use this when the host already stores its final connection string outside the package.
    /// </remarks>
    public string? ConnectionString { get; init; }

    /// <summary>
    /// Gets the structured SQLite connection settings for this named connection.
    /// </summary>
    /// <remarks>
    /// Use this when you prefer a strongly typed configuration object over a raw connection string.
    /// </remarks>
    public SqliteConnectionOptions? ConnectionOptions { get; init; }
}
