using Microsoft.Extensions.AI;

namespace AIToolkit.Tools.Sql.MySql;

/// <summary>
/// Creates the MySQL <c>mysql_*</c> tools used by Microsoft.Extensions.AI hosts.
/// </summary>
public static class MySqlTools
{
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