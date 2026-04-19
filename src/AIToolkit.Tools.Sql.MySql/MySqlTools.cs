using Microsoft.Extensions.AI;

namespace AIToolkit.Tools.Sql.MySql;

/// <summary>
/// Creates the MySQL <c>mysql_*</c> tools used by Microsoft.Extensions.AI hosts.
/// </summary>
/// <remarks>
/// This provider package composes the shared SQL abstractions with MySQL-specific connection resolution, metadata queries, query
/// classification, and explain-plan parsing. The resulting tool set stays stateless by using logical connection names that the model can
/// discover and reuse across calls.
/// </remarks>
/// <seealso cref="MySqlConnectionProfile"/>
/// <seealso cref="SqlExecutionPolicy"/>
public static class MySqlTools
{
    /// <summary>
    /// Creates the MySQL tool set for a single named connection string.
    /// </summary>
    /// <param name="connectionString">The MySQL connection string to expose through the generated tools.</param>
    /// <param name="executionPolicy">The execution policy that controls query safety, approval, and result limits.</param>
    /// <param name="mutationApprover">The component that approves mutation-capable statements when the execution policy requires it.</param>
    /// <param name="connectionName">The logical name the model supplies back to the stateless tools.</param>
    /// <returns>The <c>mysql_*</c> functions ready to register with an AI host.</returns>
    public static IReadOnlyList<AIFunction> CreateFunctions(
        string connectionString,
        SqlExecutionPolicy? executionPolicy = null,
        ISqlMutationApprover? mutationApprover = null,
        string connectionName = "default") =>
        CreateFunctions(
            [
                new MySqlConnectionProfile
                {
                    Name = connectionName,
                    ConnectionString = connectionString,
                }
            ],
            executionPolicy,
            mutationApprover);

    /// <summary>
    /// Creates the MySQL tool set for a collection of named connections.
    /// </summary>
    /// <param name="profiles">The named MySQL profiles that a model may discover and select at runtime.</param>
    /// <param name="executionPolicy">The execution policy that controls query safety, approval, and result limits.</param>
    /// <param name="mutationApprover">The component that approves mutation-capable statements when the execution policy requires it.</param>
    /// <returns>The <c>mysql_*</c> functions ready to register with an AI host.</returns>
    /// <remarks>
    /// Each generated tool call passes through <see cref="MySqlConnectionResolver"/>, <see cref="MySqlMetadataProvider"/>, and
    /// <see cref="MySqlToolService"/> so hosts can expose multiple logical MySQL targets without maintaining conversational state.
    /// </remarks>
    public static IReadOnlyList<AIFunction> CreateFunctions(
        IEnumerable<MySqlConnectionProfile> profiles,
        SqlExecutionPolicy? executionPolicy = null,
        ISqlMutationApprover? mutationApprover = null)
    {
        var connectionResolver = new MySqlConnectionResolver(profiles, connectionFactory: null);
        return CreateFunctions(connectionResolver, executionPolicy, mutationApprover);
    }

    internal static IReadOnlyList<AIFunction> CreateFunctions(
        MySqlConnectionResolver connectionResolver,
        SqlExecutionPolicy? executionPolicy = null,
        ISqlMutationApprover? mutationApprover = null)
    {
        ArgumentNullException.ThrowIfNull(connectionResolver);

        var queryClassifier = new MySqlQueryClassifier();
        var metadataProvider = new MySqlMetadataProvider(connectionResolver);
        var queryExecutor = new MySqlQueryExecutor(connectionResolver, queryClassifier, executionPolicy, mutationApprover);
        var toolService = new MySqlToolService(connectionResolver, metadataProvider, queryExecutor, connectionResolver, queryClassifier, executionPolicy);
        return new MySqlAIFunctionFactory(toolService).CreateAll();
    }
}
