namespace AIToolkit.Sql.PostgreSql;

/// <summary>
/// Thin PostgreSQL wrapper over the shared <see cref="global::AIToolkit.Sql.SqlQueryExecutor"/>.
/// </summary>
internal sealed class PostgreSqlQueryExecutor(
    PostgreSqlConnectionResolver connectionResolver,
    ISqlQueryClassifier queryClassifier,
    SqlExecutionPolicy? executionPolicy = null,
    ISqlMutationApprover? mutationApprover = null)
    : global::AIToolkit.Sql.SqlQueryExecutor(connectionResolver, queryClassifier, executionPolicy, mutationApprover)
{
}