namespace AIToolkit.Sql.MySql;

/// <summary>
/// Thin MySQL wrapper over the shared <see cref="global::AIToolkit.Sql.SqlQueryExecutor"/>.
/// </summary>
internal sealed class MySqlQueryExecutor(
    MySqlConnectionResolver connectionResolver,
    ISqlQueryClassifier queryClassifier,
    SqlExecutionPolicy? executionPolicy = null,
    ISqlMutationApprover? mutationApprover = null)
    : global::AIToolkit.Sql.SqlQueryExecutor(connectionResolver, queryClassifier, executionPolicy, mutationApprover)
{
}