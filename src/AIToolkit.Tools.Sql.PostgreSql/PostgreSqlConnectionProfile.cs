namespace AIToolkit.Tools.Sql.PostgreSql;

/// <summary>
/// Defines a named PostgreSQL connection that can be surfaced to an AI model.
/// </summary>
/// <remarks>
/// Provide either <see cref="ConnectionString"/> or <see cref="ConnectionOptions"/>.
/// The <see cref="Name"/> is what the model passes back through stateless tool calls as <c>connectionName</c>.
/// </remarks>
public sealed class PostgreSqlConnectionProfile
{
    /// <summary>
    /// Gets the logical name exposed to the model for this connection.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the raw PostgreSQL connection string for this named connection.
    /// </summary>
    /// <remarks>
    /// Use this when the host already stores complete connection strings outside the package.
    /// </remarks>
    public string? ConnectionString { get; init; }

    /// <summary>
    /// Gets the structured PostgreSQL connection settings for this named connection.
    /// </summary>
    /// <remarks>
    /// Use this when you prefer a strongly typed configuration object over a raw connection string.
    /// </remarks>
    public PostgreSqlConnectionOptions? ConnectionOptions { get; init; }
}
