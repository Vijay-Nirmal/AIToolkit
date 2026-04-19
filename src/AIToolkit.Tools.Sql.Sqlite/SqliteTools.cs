using Microsoft.Extensions.AI;

namespace AIToolkit.Tools.Sql.Sqlite;

/// <summary>
/// Creates the SQLite <c>sqlite_*</c> tools used by Microsoft.Extensions.AI hosts.
/// </summary>
/// <remarks>
/// SQLite shares the common SQL abstractions from the <c>AIToolkit.Tools.Sql</c> package, but this provider assembly owns the connection resolver,
/// metadata provider, classifier, explain-plan interpretation, and the stable <c>sqlite_*</c> tool names. Each tool call is stateless: the
/// model supplies a logical connection name, the resolver opens the file-backed database, and the assembled services collaborate to classify,
/// inspect, or execute the requested SQL operation.
/// </remarks>
/// <seealso cref="SqliteConnectionProfile"/>
/// <seealso cref="SqlExecutionPolicy"/>
public static class SqliteTools
{
    /// <summary>
    /// Creates the SQLite tool set for a single named connection string.
    /// </summary>
    /// <param name="connectionString">The SQLite connection string to expose through the generated tools.</param>
    /// <param name="executionPolicy">The execution policy that controls query safety, approval, and result limits.</param>
    /// <param name="mutationApprover">The component that approves mutation-capable statements when the execution policy requires it.</param>
    /// <param name="connectionName">The logical name the model supplies back to the stateless tools.</param>
    /// <returns>The <c>sqlite_*</c> functions ready to register with an AI host.</returns>
    /// <remarks>
    /// Use this overload when the host has only one SQLite database to expose. SQLite treats the file or data source as the primary target, and
    /// per-call <c>database</c> values are typically used to select attached catalogs such as <c>main</c> or <c>temp</c>.
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// var tools = SqliteTools.CreateFunctions(
    ///     connectionString: "Data Source=app.db",
    ///     executionPolicy: SqlExecutionPolicy.ReadOnly,
    ///     connectionName: "local-db");
    /// ]]></code>
    /// </example>
    public static IReadOnlyList<AIFunction> CreateFunctions(
        string connectionString,
        SqlExecutionPolicy? executionPolicy = null,
        ISqlMutationApprover? mutationApprover = null,
        string connectionName = "default") =>
        CreateFunctions([new SqliteConnectionProfile { Name = connectionName, ConnectionString = connectionString }], executionPolicy, mutationApprover);

    /// <summary>
    /// Creates the SQLite tool set for a collection of named connections.
    /// </summary>
    /// <param name="profiles">The named SQLite profiles that a model may discover and select at runtime.</param>
    /// <param name="executionPolicy">The execution policy that controls query safety, approval, and result limits.</param>
    /// <param name="mutationApprover">The component that approves mutation-capable statements when the execution policy requires it.</param>
    /// <returns>The <c>sqlite_*</c> functions ready to register with an AI host.</returns>
    /// <remarks>
    /// Use this overload to expose multiple local files or attached-database configurations. The generated tools rely on
    /// <see cref="SqliteConnectionResolver"/> for profile selection and on <see cref="SqliteToolService"/> for provider-specific behavior.
    /// </remarks>
    public static IReadOnlyList<AIFunction> CreateFunctions(
        IEnumerable<SqliteConnectionProfile> profiles,
        SqlExecutionPolicy? executionPolicy = null,
        ISqlMutationApprover? mutationApprover = null)
    {
        var connectionResolver = new SqliteConnectionResolver(profiles, connectionFactory: null);
        return CreateFunctions(connectionResolver, executionPolicy, mutationApprover);
    }

    internal static IReadOnlyList<AIFunction> CreateFunctions(
        SqliteConnectionResolver connectionResolver,
        SqlExecutionPolicy? executionPolicy = null,
        ISqlMutationApprover? mutationApprover = null)
    {
        ArgumentNullException.ThrowIfNull(connectionResolver);

        var queryClassifier = new SqliteQueryClassifier();
        var metadataProvider = new SqliteMetadataProvider(connectionResolver);
        var queryExecutor = new SqliteQueryExecutor(connectionResolver, queryClassifier, executionPolicy, mutationApprover);
        var toolService = new SqliteToolService(connectionResolver, metadataProvider, queryExecutor, connectionResolver, queryClassifier, executionPolicy);
        return new SqliteAIFunctionFactory(toolService).CreateAll();
    }
}
