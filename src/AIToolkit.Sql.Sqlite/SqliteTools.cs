using Microsoft.Extensions.AI;

namespace AIToolkit.Sql.Sqlite;

/// <summary>
/// Creates the SQLite <c>sqlite_*</c> tools used by Microsoft.Extensions.AI hosts.
/// </summary>
public static class SqliteTools
{
    public static IReadOnlyList<AIFunction> CreateFunctions(
        string connectionString,
        SqlExecutionPolicy? executionPolicy = null,
        ISqlMutationApprover? mutationApprover = null,
        string connectionName = "default") =>
        CreateFunctions([new SqliteConnectionProfile { Name = connectionName, ConnectionString = connectionString }], executionPolicy, mutationApprover);

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