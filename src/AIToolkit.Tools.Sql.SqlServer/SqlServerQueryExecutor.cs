using System.Data.Common;

namespace AIToolkit.Tools.Sql.SqlServer;

/// <summary>
/// Thin SQL Server wrapper over the shared <see cref="global::AIToolkit.Tools.Sql.SqlQueryExecutor"/>.
/// </summary>
/// <remarks>
/// The shared executor now owns the provider-neutral execution pipeline. This wrapper remains so SQL Server can keep its historical
/// <c>sql_variant</c> fallback for result column type names without pushing that detail into other providers.
/// </remarks>
/// <param name="connectionResolver">Opens SQL Server connections for the shared executor.</param>
/// <param name="queryClassifier">Classifies T-SQL text before execution.</param>
/// <param name="executionPolicy">Controls mutation handling, timeouts, and result limits.</param>
/// <param name="mutationApprover">Approves mutation-capable statements when required.</param>
internal sealed class SqlServerQueryExecutor(
    SqlServerConnectionResolver connectionResolver,
    ISqlQueryClassifier queryClassifier,
    SqlExecutionPolicy? executionPolicy = null,
    ISqlMutationApprover? mutationApprover = null)
    : global::AIToolkit.Tools.Sql.SqlQueryExecutor(connectionResolver, queryClassifier, executionPolicy, mutationApprover)
{
    /// <inheritdoc />
    protected override string GetDataTypeName(DbColumn column, int ordinal) =>
        column.DataTypeName ?? column.DataType?.Name ?? "sql_variant";
}
