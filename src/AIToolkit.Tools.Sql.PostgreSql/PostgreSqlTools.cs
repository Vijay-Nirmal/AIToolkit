using Microsoft.Extensions.AI;

namespace AIToolkit.Tools.Sql.PostgreSql;

/// <summary>
/// Creates the PostgreSQL <c>pgsql_*</c> tools used by Microsoft.Extensions.AI hosts.
/// </summary>
/// <remarks>
/// The tool set includes discovery tools, metadata lookup, execution, installed extension discovery, and explain-plan analysis for read-only queries.
/// </remarks>
/// <seealso cref="PostgreSqlConnectionProfile"/>
/// <seealso cref="SqlExecutionPolicy"/>
public static class PostgreSqlTools
{
    /// <summary>
    /// Creates the PostgreSQL tool set for a single named connection string.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string to expose through the generated tools.</param>
    /// <param name="executionPolicy">The execution policy that controls query safety, approval, and result limits.</param>
    /// <param name="mutationApprover">The component that approves mutation-capable statements when the execution policy requires it.</param>
    /// <param name="connectionName">The logical name the model supplies back to the stateless tools.</param>
    /// <returns>The <c>pgsql_*</c> functions ready to register with an AI host.</returns>
    public static IReadOnlyList<AIFunction> CreateFunctions(
        string connectionString,
        SqlExecutionPolicy? executionPolicy = null,
        ISqlMutationApprover? mutationApprover = null,
        string connectionName = "default") =>
        CreateFunctions(
            [
                new PostgreSqlConnectionProfile
                {
                    Name = connectionName,
                    ConnectionString = connectionString,
                }
            ],
            executionPolicy,
            mutationApprover);

    /// <summary>
    /// Creates the PostgreSQL tool set for a collection of named connections.
    /// </summary>
    /// <param name="profiles">The named PostgreSQL profiles that a model may discover and select at runtime.</param>
    /// <param name="executionPolicy">The execution policy that controls query safety, approval, and result limits.</param>
    /// <param name="mutationApprover">The component that approves mutation-capable statements when the execution policy requires it.</param>
    /// <returns>The <c>pgsql_*</c> functions ready to register with an AI host.</returns>
    /// <remarks>
    /// Each generated tool call remains stateless and resolves the selected profile through <see cref="PostgreSqlConnectionResolver"/> before
    /// delegating to provider-specific metadata, query, or explain services.
    /// </remarks>
    public static IReadOnlyList<AIFunction> CreateFunctions(
        IEnumerable<PostgreSqlConnectionProfile> profiles,
        SqlExecutionPolicy? executionPolicy = null,
        ISqlMutationApprover? mutationApprover = null)
    {
        var connectionResolver = new PostgreSqlConnectionResolver(profiles, connectionFactory: null);
        return CreateFunctions(connectionResolver, executionPolicy, mutationApprover);
    }

    internal static IReadOnlyList<AIFunction> CreateFunctions(
        PostgreSqlConnectionResolver connectionResolver,
        SqlExecutionPolicy? executionPolicy = null,
        ISqlMutationApprover? mutationApprover = null)
    {
        ArgumentNullException.ThrowIfNull(connectionResolver);

        var queryClassifier = new PostgreSqlQueryClassifier();
        var metadataProvider = new PostgreSqlMetadataProvider(connectionResolver);
        var queryExecutor = new PostgreSqlQueryExecutor(
            connectionResolver,
            queryClassifier,
            executionPolicy,
            mutationApprover);

        var toolService = new PostgreSqlToolService(
            connectionResolver,
            metadataProvider,
            queryExecutor,
            connectionResolver,
            queryClassifier,
            executionPolicy);

        return new PostgreSqlAIFunctionFactory(toolService).CreateAll();
    }
}
