namespace AIToolkit.Tools.Sql.MySql;

/// <summary>
/// Defines a named MySQL connection that can be surfaced to an AI model.
/// </summary>
public sealed class MySqlConnectionProfile
{
    /// <summary>
    /// Gets the logical name exposed to the model for this connection.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the raw MySQL connection string for this named connection.
    /// </summary>
    public string? ConnectionString { get; init; }

    /// <summary>
    /// Gets the structured MySQL connection settings for this named connection.
    /// </summary>
    public MySqlConnectionOptions? ConnectionOptions { get; init; }
}