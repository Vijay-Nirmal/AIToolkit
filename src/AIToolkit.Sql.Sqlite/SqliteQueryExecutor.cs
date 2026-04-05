namespace AIToolkit.Sql.Sqlite;

/// <summary>
/// Thin SQLite wrapper over the shared <see cref="global::AIToolkit.Sql.SqlQueryExecutor"/>.
/// </summary>
internal sealed class SqliteQueryExecutor(
    SqliteConnectionResolver connectionResolver,
    ISqlQueryClassifier queryClassifier,
    SqlExecutionPolicy? executionPolicy = null,
    ISqlMutationApprover? mutationApprover = null)
    : global::AIToolkit.Sql.SqlQueryExecutor(connectionResolver, queryClassifier, executionPolicy, mutationApprover)
{
}