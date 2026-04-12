namespace AIToolkit.Tools.Sql.SqlServer;

/// <summary>
/// Defines a named SQL Server connection that can be surfaced to an AI model.
/// </summary>
/// <remarks>
/// Provide either <see cref="ConnectionString"/> or <see cref="ConnectionOptions"/>.
/// The <see cref="Name"/> is what the model passes back through stateless tool calls as <c>connectionName</c>.
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// var profiles = new[]
/// {
///     new SqlServerConnectionProfile
///     {
///         Name = "sales",
///         ConnectionString = "Server=localhost\\MSSQLSERVER01;Database=Sales;Trusted_Connection=True;TrustServerCertificate=True;"
///     },
///     new SqlServerConnectionProfile
///     {
///         Name = "warehouse",
///         ConnectionOptions = new SqlServerConnectionOptions
///         {
///             Server = "localhost\\MSSQLSERVER01",
///             Database = "Warehouse",
///             TrustServerCertificate = true
///         }
///     }
/// };
/// ]]></code>
/// </example>
public sealed class SqlServerConnectionProfile
{
    /// <summary>
    /// Gets the logical name exposed to the model for this connection.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the raw SQL Server connection string for this named connection.
    /// </summary>
    /// <remarks>
    /// This is the simplest option when your host already manages complete connection strings outside of the package.
    /// </remarks>
    public string? ConnectionString { get; init; }

    /// <summary>
    /// Gets the structured SQL Server connection settings for this named connection.
    /// </summary>
    /// <remarks>
    /// Use this when you prefer a strongly-typed configuration object instead of a raw connection string.
    /// </remarks>
    public SqlServerConnectionOptions? ConnectionOptions { get; init; }
}