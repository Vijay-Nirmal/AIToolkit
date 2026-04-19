namespace AIToolkit.Tools.Sql.MySql;

/// <summary>
/// Defines a named MySQL connection that can be surfaced to an AI model.
/// </summary>
/// <remarks>
/// Provide either <see cref="ConnectionString"/> or <see cref="ConnectionOptions"/>. The <see cref="Name"/> is the logical identifier that
/// the generated <c>mysql_*</c> tools accept as <c>connectionName</c>.
/// </remarks>
public sealed class MySqlConnectionProfile
{
    /// <summary>
    /// Gets the logical name exposed to the model for this connection.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the raw MySQL connection string for this named connection.
    /// </summary>
    /// <remarks>
    /// Use this when the host already stores complete connection strings outside the package.
    /// </remarks>
    public string? ConnectionString { get; init; }

    /// <summary>
    /// Gets the structured MySQL connection settings for this named connection.
    /// </summary>
    /// <remarks>
    /// Use this when you prefer a strongly typed configuration object over a raw connection string.
    /// </remarks>
    public MySqlConnectionOptions? ConnectionOptions { get; init; }
}
