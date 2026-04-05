namespace AIToolkit.Sql;

/// <summary>
/// Identifies the named connection a stateless SQL tool call should use.
/// </summary>
/// <param name="ConnectionName">The host-defined logical connection name.</param>
/// <param name="Database">An optional database override for the individual tool call.</param>
public sealed record SqlConnectionTarget(
    string ConnectionName,
    string? Database = null);

/// <summary>
/// Summarizes a named SQL connection that a host exposes to AI tools.
/// </summary>
/// <param name="ConnectionName">The logical name the host uses for the connection.</param>
/// <param name="Server">The target server for the named connection.</param>
/// <param name="Database">The default database, if one is configured.</param>
public sealed record SqlConnectionProfileSummary(
    string ConnectionName,
    string Server,
    string? Database = null);