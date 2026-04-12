namespace AIToolkit.Tools.Sql;

/// <summary>
/// Controls what kinds of SQL commands may execute and how large the returned results may be.
/// </summary>
/// <remarks>
/// This policy is evaluated only when a tool eventually executes SQL text, primarily through <c>mssql_run_query</c>.
/// Connection-discovery and metadata tools remain available regardless of the mutation settings because they do not execute arbitrary
/// user-supplied write operations. Common examples include:
/// <list type="bullet">
/// <item><description><c>mssql_list_servers</c></description></item>
/// <item><description><c>mssql_list_tables</c></description></item>
/// <item><description><c>mssql_get_object_definition</c></description></item>
/// </list>
/// </remarks>
public sealed class SqlExecutionPolicy
{
    /// <summary>
    /// Gets a value indicating whether statements that can mutate database state are allowed to run.
    /// </summary>
    /// <remarks>
    /// The default value is <see langword="false"/>.
    /// </remarks>
    public bool AllowMutations { get; init; }

    /// <summary>
    /// Gets a value indicating whether mutation-capable statements must be approved by an <see cref="ISqlMutationApprover"/>.
    /// </summary>
    /// <remarks>
    /// The default value is <see langword="true"/>.
    /// </remarks>
    public bool RequireApprovalForMutations { get; init; } = true;

    /// <summary>
    /// Gets the maximum number of rows to materialize for a single result set before truncation is reported.
    /// </summary>
    /// <remarks>
    /// The default value is <c>200</c> rows.
    /// </remarks>
    public int MaxRows { get; init; } = 200;

    /// <summary>
    /// Gets the maximum number of result sets to materialize from one execution.
    /// </summary>
    /// <remarks>
    /// The default value is <c>3</c> result sets.
    /// </remarks>
    public int MaxResultSets { get; init; } = 3;

    /// <summary>
    /// Gets the maximum string length to keep for a single scalar value before truncation is applied.
    /// </summary>
    /// <remarks>
    /// The default value is <c>4096</c> characters.
    /// </remarks>
    public int MaxStringLength { get; init; } = 4096;

    /// <summary>
    /// Gets the command timeout in seconds used when executing SQL commands.
    /// </summary>
    /// <remarks>
    /// The default value is <c>30</c> seconds.
    /// </remarks>
    public int CommandTimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Gets a read-only execution policy that blocks mutations and uses the default size limits.
    /// </summary>
    /// <remarks>
    /// Under this policy, the SQL Server package still exposes the full discovery tool set:
    /// <list type="bullet">
    /// <item><description><c>mssql_list_servers</c></description></item>
    /// <item><description><c>mssql_list_databases</c></description></item>
    /// <item><description><c>mssql_list_schemas</c></description></item>
    /// <item><description><c>mssql_list_tables</c></description></item>
    /// <item><description><c>mssql_list_views</c></description></item>
    /// <item><description><c>mssql_list_functions</c></description></item>
    /// <item><description><c>mssql_list_procedures</c></description></item>
    /// <item><description><c>mssql_get_object_definition</c></description></item>
    /// </list>
    /// <c>mssql_run_query</c> remains available as well, but only for statements classified as read-only.
    /// Statements containing write-oriented operations such as the following are rejected:
    /// <list type="bullet">
    /// <item><description><c>INSERT</c></description></item>
    /// <item><description><c>UPDATE</c></description></item>
    /// <item><description><c>DELETE</c></description></item>
    /// <item><description><c>MERGE</c></description></item>
    /// <item><description><c>CREATE</c></description></item>
    /// <item><description><c>ALTER</c></description></item>
    /// <item><description><c>DROP</c></description></item>
    /// <item><description><c>TRUNCATE</c></description></item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// var tools = SqlServerTools.CreateFunctions(
    ///     "Server=localhost\\MSSQLSERVER01;Database=Sales;Trusted_Connection=True;TrustServerCertificate=True;",
    ///     executionPolicy: SqlExecutionPolicy.ReadOnly,
    ///     connectionName: "sales");
    /// 
    /// // The model can connect, inspect schemas, and run SELECT-style queries,
    /// // but write operations are blocked.
    /// ]]></code>
    /// </example>
    public static SqlExecutionPolicy ReadOnly { get; } = new();
}