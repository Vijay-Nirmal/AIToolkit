using Microsoft.Extensions.AI;

namespace AIToolkit.Sql.SqlServer;

/// <summary>
/// Creates the SQL Server <c>mssql_*</c> tools used by Microsoft.Extensions.AI hosts.
/// </summary>
/// <remarks>
/// The tool set created by this class is reference-aligned and currently includes:
/// <list type="bullet">
/// <item><description><c>mssql_list_servers</c></description></item>
/// <item><description><c>mssql_list_databases</c></description></item>
/// <item><description><c>mssql_list_schemas</c></description></item>
/// <item><description><c>mssql_list_tables</c></description></item>
/// <item><description><c>mssql_list_views</c></description></item>
/// <item><description><c>mssql_list_functions</c></description></item>
/// <item><description><c>mssql_list_procedures</c></description></item>
/// <item><description><c>mssql_get_object_definition</c></description></item>
/// <item><description><c>mssql_explain_query</c></description></item>
/// <item><description><c>mssql_run_query</c></description></item>
/// </list>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// var tools = SqlServerTools.CreateFunctions(
///     "Server=localhost\\MSSQLSERVER01;Database=master;Trusted_Connection=True;TrustServerCertificate=True;",
///     executionPolicy: SqlExecutionPolicy.ReadOnly,
///     connectionName: "local-sql");
/// ]]></code>
/// </example>
public static class SqlServerTools
{
    /// <summary>
    /// Creates the SQL Server tool set for a single named connection string.
    /// </summary>
    /// <param name="connectionString">The SQL Server connection string the tools should use when the model connects.</param>
    /// <param name="executionPolicy">The execution policy that controls read/write behavior and result limits.</param>
    /// <param name="mutationApprover">The approval component used when the execution policy requires mutation approval.</param>
    /// <param name="connectionName">The logical name exposed to the model for this connection.</param>
    /// <returns>The <c>mssql_*</c> tool functions ready to register with an AI host.</returns>
    /// <remarks>
    /// Use this overload when your application already has a final SQL Server connection string and only needs to expose one logical target.
    /// The created tools are stateless. Each tool call supplies <c>connectionName</c> directly, and database-scoped tools may also supply a
    /// per-call <c>database</c> override.
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// var tools = SqlServerTools.CreateFunctions(
    ///     connectionString: "Server=localhost\\MSSQLSERVER01;Database=Inventory;Trusted_Connection=True;TrustServerCertificate=True;",
    ///     executionPolicy: SqlExecutionPolicy.ReadOnly,
    ///     connectionName: "inventory");
    /// ]]></code>
    /// </example>
    public static IReadOnlyList<AIFunction> CreateFunctions(
        string connectionString,
        SqlExecutionPolicy? executionPolicy = null,
        ISqlMutationApprover? mutationApprover = null,
        string connectionName = "default") =>
        CreateFunctions(
            [
                new SqlServerConnectionProfile
                {
                    Name = connectionName,
                    ConnectionString = connectionString,
                }
            ],
            executionPolicy,
            mutationApprover);

    /// <summary>
    /// Creates the SQL Server tool set for a collection of named connections.
    /// </summary>
    /// <param name="profiles">The named SQL Server connections that may be selected by the model.</param>
    /// <param name="executionPolicy">The execution policy that controls read/write behavior and result limits.</param>
    /// <param name="mutationApprover">The approval component used when the execution policy requires mutation approval.</param>
    /// <returns>The <c>mssql_*</c> tool functions ready to register with an AI host.</returns>
    /// <remarks>
    /// Use this overload when the host needs to expose multiple named SQL Server targets.
    /// A model can discover them through <c>mssql_list_servers</c> and then pass the selected <c>connectionName</c> into each subsequent tool call.
    /// Each profile may be backed by either a raw connection string or structured <see cref="SqlServerConnectionOptions"/>.
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// var tools = SqlServerTools.CreateFunctions(
    /// [
    ///     new SqlServerConnectionProfile
    ///     {
    ///         Name = "sales",
    ///         ConnectionString = "Server=localhost\\MSSQLSERVER01;Database=Sales;Trusted_Connection=True;TrustServerCertificate=True;"
    ///     },
    ///     new SqlServerConnectionProfile
    ///     {
    ///         Name = "inventory",
    ///         ConnectionOptions = new SqlServerConnectionOptions
    ///         {
    ///             Server = "localhost\\MSSQLSERVER01",
    ///             Database = "Inventory",
    ///             TrustServerCertificate = true
    ///         }
    ///     }
    /// ],
    /// executionPolicy: SqlExecutionPolicy.ReadOnly);
    /// ]]></code>
    /// </example>
    public static IReadOnlyList<AIFunction> CreateFunctions(
        IEnumerable<SqlServerConnectionProfile> profiles,
        SqlExecutionPolicy? executionPolicy = null,
        ISqlMutationApprover? mutationApprover = null)
    {
        var connectionResolver = new SqlServerConnectionResolver(profiles, connectionFactory: null);
        return CreateFunctions(connectionResolver, executionPolicy, mutationApprover);
    }

    internal static IReadOnlyList<AIFunction> CreateFunctions(
        SqlServerConnectionResolver connectionResolver,
        SqlExecutionPolicy? executionPolicy = null,
        ISqlMutationApprover? mutationApprover = null)
    {
        ArgumentNullException.ThrowIfNull(connectionResolver);

        var queryClassifier = new SqlServerQueryClassifier();
        var metadataProvider = new SqlServerMetadataProvider(connectionResolver);
        var queryExecutor = new SqlServerQueryExecutor(
            connectionResolver,
            queryClassifier,
            executionPolicy,
            mutationApprover);

        var toolService = new SqlServerToolService(
            connectionResolver,
            metadataProvider,
            queryExecutor,
            connectionResolver,
            queryClassifier,
            executionPolicy);

        return new SqlServerAIFunctionFactory(toolService).CreateAll();
    }
}