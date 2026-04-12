namespace AIToolkit.Tools.Sql.PostgreSql;

/// <summary>
/// Thin PostgreSQL wrapper over the shared <see cref="global::AIToolkit.Tools.Sql.SqlQueryExecutor"/>.
/// </summary>
internal sealed class PostgreSqlQueryExecutor(
    PostgreSqlConnectionResolver connectionResolver,
    ISqlQueryClassifier queryClassifier,
    SqlExecutionPolicy? executionPolicy = null,
    ISqlMutationApprover? mutationApprover = null)
    : global::AIToolkit.Tools.Sql.SqlQueryExecutor(connectionResolver, queryClassifier, executionPolicy, mutationApprover)
{
}