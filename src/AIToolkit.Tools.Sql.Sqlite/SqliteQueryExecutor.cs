namespace AIToolkit.Tools.Sql.Sqlite;

/// <summary>
/// Thin SQLite wrapper over the shared <see cref="global::AIToolkit.Tools.Sql.SqlQueryExecutor"/>.
/// </summary>
/// <remarks>
/// SQLite currently uses the shared execution pipeline unchanged. The wrapper exists so SQLite can introduce provider-specific result
/// normalization later without altering hosts that already depend on the provider assembly.
/// </remarks>
/// <param name="connectionResolver">Opens SQLite connections for the shared executor.</param>
/// <param name="queryClassifier">Classifies SQLite SQL text before execution.</param>
/// <param name="executionPolicy">Controls mutation handling, timeouts, and result limits.</param>
/// <param name="mutationApprover">Approves mutation-capable statements when required.</param>
internal sealed class SqliteQueryExecutor(
    SqliteConnectionResolver connectionResolver,
    ISqlQueryClassifier queryClassifier,
    SqlExecutionPolicy? executionPolicy = null,
    ISqlMutationApprover? mutationApprover = null)
    : global::AIToolkit.Tools.Sql.SqlQueryExecutor(connectionResolver, queryClassifier, executionPolicy, mutationApprover)
{
}
