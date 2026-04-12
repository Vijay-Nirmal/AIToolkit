using Microsoft.Extensions.AI;

namespace AIToolkit.Tools.Sql.PostgreSql;

/// <summary>
/// Creates the PostgreSQL <c>pgsql_*</c> tools used by Microsoft.Extensions.AI hosts.
/// </summary>
/// <remarks>
/// The tool set includes discovery tools, metadata lookup, execution, installed extension discovery, and explain-plan analysis for read-only queries.
/// </remarks>
public static class PostgreSqlTools
{
    /// <summary>
    /// Creates the PostgreSQL tool set for a single named connection string.
    /// </summary>
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