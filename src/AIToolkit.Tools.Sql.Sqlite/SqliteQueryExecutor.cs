namespace AIToolkit.Tools.Sql.Sqlite;

/// <summary>
/// Thin SQLite wrapper over the shared <see cref="global::AIToolkit.Tools.Sql.SqlQueryExecutor"/>.
/// </summary>
internal sealed class SqliteQueryExecutor(
    SqliteConnectionResolver connectionResolver,
    ISqlQueryClassifier queryClassifier,
    SqlExecutionPolicy? executionPolicy = null,
    ISqlMutationApprover? mutationApprover = null)
    : global::AIToolkit.Tools.Sql.SqlQueryExecutor(connectionResolver, queryClassifier, executionPolicy, mutationApprover)
{
}