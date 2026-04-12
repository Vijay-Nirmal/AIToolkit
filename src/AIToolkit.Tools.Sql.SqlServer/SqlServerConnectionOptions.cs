namespace AIToolkit.Tools.Sql.SqlServer;

/// <summary>
/// Describes a SQL Server connection in structured form.
/// </summary>
/// <remarks>
/// Use this type when you want to configure a connection without supplying a raw connection string.
/// The package converts these settings into a SQL Server connection string whenever a stateless tool call resolves the named profile.
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// var profile = new SqlServerConnectionProfile
/// {
///     Name = "reporting",
///     ConnectionOptions = new SqlServerConnectionOptions
///     {
///         Server = "localhost\\MSSQLSERVER01",
///         Database = "Reporting",
///         TrustServerCertificate = true,
///         ApplicationName = "MyAnalyticsHost"
///     }
/// };
/// ]]></code>
/// </example>
public sealed class SqlServerConnectionOptions
{
    /// <summary>
    /// Gets the SQL Server instance name, host name, or host and port combination.
    /// </summary>
    public required string Server { get; init; }

    /// <summary>
    /// Gets the default database to connect to.
    /// </summary>
    /// <remarks>
    /// When omitted, the provider defaults to <c>master</c>.
    /// </remarks>
    public string? Database { get; init; }

    /// <summary>
    /// Gets the SQL login user name.
    /// </summary>
    /// <remarks>
    /// The default value is <see langword="null"/>.
    /// Leave this unset when using integrated authentication or a provider-specific authentication mode.
    /// </remarks>
    public string? UserId { get; init; }

    /// <summary>
    /// Gets the password for <see cref="UserId"/> when SQL authentication is used.
    /// </summary>
    /// <remarks>
    /// The default value is <see langword="null"/>.
    /// </remarks>
    public string? Password { get; init; }

    /// <summary>
    /// Gets the SQL Server authentication mode to place in the connection string.
    /// </summary>
    /// <remarks>
    /// The default value is <see langword="null"/>.
    /// This is passed through to the SQL client <c>Authentication</c> setting for scenarios such as Azure AD.
    /// When omitted, the provider chooses integrated security or SQL authentication based on the other properties.
    /// Typical examples include values such as <c>Active Directory Default</c> or <c>Active Directory Managed Identity</c>
    /// when those modes are supported by the configured SQL client.
    /// </remarks>
    public string? Authentication { get; init; }

    /// <summary>
    /// Gets a value indicating whether transport encryption is enabled.
    /// </summary>
    /// <remarks>
    /// The default value is <see langword="true"/>.
    /// </remarks>
    public bool Encrypt { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether the server certificate is trusted without full chain validation.
    /// </summary>
    /// <remarks>
    /// The default value is <see langword="true"/>.
    /// </remarks>
    public bool TrustServerCertificate { get; init; } = true;

    /// <summary>
    /// Gets the application name reported to SQL Server.
    /// </summary>
    /// <remarks>
    /// The default value is <c>AIToolkit</c>.
    /// </remarks>
    public string ApplicationName { get; init; } = "AIToolkit";

    /// <summary>
    /// Gets the optional connection timeout, in seconds.
    /// </summary>
    /// <remarks>
    /// The default value is <see langword="null"/>, which lets the SQL client use its own timeout default.
    /// </remarks>
    public int? ConnectTimeoutSeconds { get; init; }

    internal bool UsesIntegratedSecurity =>
        string.IsNullOrWhiteSpace(UserId) && string.IsNullOrWhiteSpace(Authentication);

    internal string EffectiveDatabase => string.IsNullOrWhiteSpace(Database) ? "master" : Database;
}