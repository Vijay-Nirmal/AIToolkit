namespace AIToolkit.Tools.Sql.MySql;

/// <summary>
/// Thin MySQL wrapper over the shared <see cref="global::AIToolkit.Tools.Sql.SqlQueryExecutor"/>.
/// </summary>
internal sealed class MySqlQueryExecutor(
    MySqlConnectionResolver connectionResolver,
    ISqlQueryClassifier queryClassifier,
    SqlExecutionPolicy? executionPolicy = null,
    ISqlMutationApprover? mutationApprover = null)
    : global::AIToolkit.Tools.Sql.SqlQueryExecutor(connectionResolver, queryClassifier, executionPolicy, mutationApprover)
{
}