namespace AIToolkit.Tools.Sql.MySql;

/// <summary>
/// Thin MySQL wrapper over the shared <see cref="global::AIToolkit.Tools.Sql.SqlQueryExecutor"/>.
/// </summary>
/// <remarks>
/// MySQL currently uses the shared execution pipeline unchanged. The wrapper preserves a provider seam for future MySQL-specific result
/// normalization without complicating the provider-neutral executor.
/// </remarks>
/// <param name="connectionResolver">Opens MySQL connections for the shared executor.</param>
/// <param name="queryClassifier">Classifies MySQL SQL text before execution.</param>
/// <param name="executionPolicy">Controls mutation handling, timeouts, and result limits.</param>
/// <param name="mutationApprover">Approves mutation-capable statements when required.</param>
internal sealed class MySqlQueryExecutor(
    MySqlConnectionResolver connectionResolver,
    ISqlQueryClassifier queryClassifier,
    SqlExecutionPolicy? executionPolicy = null,
    ISqlMutationApprover? mutationApprover = null)
    : global::AIToolkit.Tools.Sql.SqlQueryExecutor(connectionResolver, queryClassifier, executionPolicy, mutationApprover)
{
}
