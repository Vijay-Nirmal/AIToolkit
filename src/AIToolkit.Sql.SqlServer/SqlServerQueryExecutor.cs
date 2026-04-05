using System.Data.Common;

namespace AIToolkit.Sql.SqlServer;

/// <summary>
/// Thin SQL Server wrapper over the shared <see cref="global::AIToolkit.Sql.SqlQueryExecutor"/>.
/// </summary>
/// <remarks>
/// The shared executor now owns the provider-neutral execution pipeline. This wrapper remains so SQL Server can keep its historical
/// <c>sql_variant</c> fallback for result column type names without pushing that detail into other providers.
/// </remarks>
internal sealed class SqlServerQueryExecutor(
    SqlServerConnectionResolver connectionResolver,
    ISqlQueryClassifier queryClassifier,
    SqlExecutionPolicy? executionPolicy = null,
    ISqlMutationApprover? mutationApprover = null)
    : global::AIToolkit.Sql.SqlQueryExecutor(connectionResolver, queryClassifier, executionPolicy, mutationApprover)
{
    protected override string GetDataTypeName(DbColumn column, int ordinal) =>
        column.DataTypeName ?? column.DataType?.Name ?? "sql_variant";
}