namespace AIToolkit.Tools.Sql.PostgreSql;

/// <summary>
/// Thin PostgreSQL wrapper over the shared <see cref="global::AIToolkit.Tools.Sql.SqlQueryExecutor"/>.
/// </summary>
/// <remarks>
/// PostgreSQL currently uses the shared execution pipeline unchanged. The wrapper preserves a provider seam for future PostgreSQL-specific
/// result normalization without complicating the provider-neutral executor.
/// </remarks>
/// <param name="connectionResolver">Opens PostgreSQL connections for the shared executor.</param>
/// <param name="queryClassifier">Classifies PostgreSQL SQL text before execution.</param>
/// <param name="executionPolicy">Controls mutation handling, timeouts, and result limits.</param>
/// <param name="mutationApprover">Approves mutation-capable statements when required.</param>
internal sealed class PostgreSqlQueryExecutor(
    PostgreSqlConnectionResolver connectionResolver,
    ISqlQueryClassifier queryClassifier,
    SqlExecutionPolicy? executionPolicy = null,
    ISqlMutationApprover? mutationApprover = null)
    : global::AIToolkit.Tools.Sql.SqlQueryExecutor(connectionResolver, queryClassifier, executionPolicy, mutationApprover)
{
}
