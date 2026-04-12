namespace AIToolkit.Tools.Sql;

/// <summary>
/// Indicates whether SQL text is safe to execute immediately, requires approval, or must be blocked.
/// </summary>
public enum SqlStatementSafety
{
    /// <summary>The SQL text is classified as a safe read-only operation.</summary>
    ReadOnly = 0,

    /// <summary>The SQL text may mutate state and should be approved before execution.</summary>
    ApprovalRequired,

    /// <summary>The SQL text is not allowed to execute.</summary>
    Blocked,
}

/// <summary>
/// Describes the classification result for a SQL statement or batch.
/// </summary>
/// <param name="StatementTypes">The leading SQL statement types detected in the batch.</param>
/// <param name="Safety">The overall safety classification.</param>
/// <param name="Reason">An optional explanation for blocked or approval-required classifications.</param>
public sealed record SqlQueryClassification(
    IReadOnlyList<string> StatementTypes,
    SqlStatementSafety Safety,
    string? Reason = null);

/// <summary>
/// Describes one result column returned by a SQL execution.
/// </summary>
/// <param name="Name">The column name.</param>
/// <param name="DataType">The provider-specific SQL type name.</param>
/// <param name="Ordinal">The zero-based column ordinal.</param>
/// <param name="IsNullable"><see langword="true"/> when the column accepts null values.</param>
public sealed record SqlResultColumn(
    string Name,
    string DataType,
    int Ordinal,
    bool IsNullable);

/// <summary>
/// Represents one tabular result set returned by a SQL execution.
/// </summary>
/// <param name="Columns">The result-set columns.</param>
/// <param name="Rows">The materialized rows.</param>
/// <param name="IsTruncated"><see langword="true"/> when more rows existed than were returned.</param>
/// <param name="TotalRowCount">The total number of rows observed while reading the result set.</param>
public sealed record SqlResultSet(
    IReadOnlyList<SqlResultColumn> Columns,
    IReadOnlyList<Dictionary<string, object?>> Rows,
    bool IsTruncated = false,
    int TotalRowCount = 0);

/// <summary>
/// Represents the outcome of executing SQL text.
/// </summary>
/// <param name="Success"><see langword="true"/> when the execution completed successfully.</param>
/// <param name="Classification">The classification used to decide whether execution could proceed.</param>
/// <param name="ResultSets">The materialized result sets, if any.</param>
/// <param name="RecordsAffected">The provider-reported affected row count when available.</param>
/// <param name="Message">An optional diagnostic message for failures or warnings.</param>
public sealed record SqlExecuteQueryResult(
    bool Success,
    SqlQueryClassification Classification,
    IReadOnlyList<SqlResultSet> ResultSets,
    int? RecordsAffected = null,
    string? Message = null);

/// <summary>
/// Summarizes timing information returned by an execution plan.
/// </summary>
/// <param name="PlanningTimeMs">The total planning time in milliseconds, when available.</param>
/// <param name="ExecutionTimeMs">The total execution time in milliseconds, when available.</param>
/// <param name="PlanningCpuTimeMs">The CPU time spent during planning or compilation, when available.</param>
/// <param name="ExecutionCpuTimeMs">The CPU time spent during execution, when available.</param>
/// <param name="ActualStartupTimeMs">The time before the root node produced its first row, when available.</param>
/// <param name="ActualTotalTimeMs">The total time spent in the root node, when available.</param>
/// <param name="ActualRows">The number of rows the root node actually produced, when available.</param>
/// <param name="ActualLoops">The number of loops the root node actually ran, when available.</param>
public sealed record SqlExplainTimingStatistics(
    double? PlanningTimeMs,
    double? ExecutionTimeMs,
    double? PlanningCpuTimeMs,
    double? ExecutionCpuTimeMs,
    double? ActualStartupTimeMs,
    double? ActualTotalTimeMs,
    double? ActualRows,
    int? ActualLoops);

/// <summary>
/// Summarizes buffer usage reported by an execution plan.
/// </summary>
/// <param name="SharedHitBlocks">Shared blocks found in cache.</param>
/// <param name="SharedReadBlocks">Shared blocks read from disk.</param>
/// <param name="SharedDirtiedBlocks">Shared blocks dirtied by the statement.</param>
/// <param name="SharedWrittenBlocks">Shared blocks written by the statement.</param>
/// <param name="LocalHitBlocks">Local blocks found in cache.</param>
/// <param name="LocalReadBlocks">Local blocks read from disk.</param>
/// <param name="LocalDirtiedBlocks">Local blocks dirtied by the statement.</param>
/// <param name="LocalWrittenBlocks">Local blocks written by the statement.</param>
/// <param name="TempReadBlocks">Temporary blocks read from disk.</param>
/// <param name="TempWrittenBlocks">Temporary blocks written to disk.</param>
public sealed record SqlExplainBufferStatistics(
    long? SharedHitBlocks,
    long? SharedReadBlocks,
    long? SharedDirtiedBlocks,
    long? SharedWrittenBlocks,
    long? LocalHitBlocks,
    long? LocalReadBlocks,
    long? LocalDirtiedBlocks,
    long? LocalWrittenBlocks,
    long? TempReadBlocks,
    long? TempWrittenBlocks);

/// <summary>
/// Summarizes write-ahead logging activity reported by an execution plan.
/// </summary>
/// <param name="Records">The number of WAL records generated.</param>
/// <param name="FullPageImages">The number of full-page images generated.</param>
/// <param name="Bytes">The total WAL bytes generated.</param>
public sealed record SqlExplainWalStatistics(
    long? Records,
    long? FullPageImages,
    long? Bytes);

/// <summary>
/// Summarizes the root node of an execution plan.
/// </summary>
/// <param name="NodeType">The PostgreSQL node type, such as Seq Scan or Hash Join.</param>
/// <param name="RelationName">The relation referenced by the root node, when available.</param>
/// <param name="Schema">The relation schema, when available.</param>
/// <param name="Alias">The relation alias, when available.</param>
/// <param name="IndexName">The index name used by the root node, when available.</param>
/// <param name="JoinType">The join type, when available.</param>
/// <param name="Strategy">The execution strategy, when available.</param>
/// <param name="StartupCost">The estimated startup cost.</param>
/// <param name="TotalCost">The estimated total cost.</param>
/// <param name="PlanRows">The estimated row count.</param>
/// <param name="PlanWidth">The estimated row width.</param>
/// <param name="WorkersPlanned">The number of planned workers, when available.</param>
/// <param name="WorkersLaunched">The number of launched workers, when available.</param>
/// <param name="Filter">The filter expression, when available.</param>
/// <param name="IndexCondition">The index condition, when available.</param>
/// <param name="HashCondition">The hash condition, when available.</param>
/// <param name="MergeCondition">The merge condition, when available.</param>
/// <param name="Timing">The timing statistics for the root node and overall statement.</param>
/// <param name="Buffers">The buffer usage statistics.</param>
/// <param name="Wal">The WAL statistics.</param>
public sealed record SqlExplainNodeSummary(
    string? NodeType,
    string? RelationName,
    string? Schema,
    string? Alias,
    string? IndexName,
    string? JoinType,
    string? Strategy,
    decimal? StartupCost,
    decimal? TotalCost,
    long? PlanRows,
    int? PlanWidth,
    int? WorkersPlanned,
    int? WorkersLaunched,
    string? Filter,
    string? IndexCondition,
    string? HashCondition,
    string? MergeCondition,
    SqlExplainTimingStatistics Timing,
    SqlExplainBufferStatistics Buffers,
    SqlExplainWalStatistics Wal);

/// <summary>
/// Represents the output of a provider-specific explain-plan tool.
/// </summary>
/// <param name="Query">The original query text supplied by the caller.</param>
/// <param name="ExplainQuery">The provider-specific explain statement that was executed.</param>
/// <param name="Format">The explain-plan format returned by the provider.</param>
/// <param name="Classification">The safety classification of the original query text.</param>
/// <param name="RootNode">A summary of the root plan node and key performance statistics.</param>
/// <param name="PlanPayload">The raw explain plan payload returned by the provider, such as JSON, XML, or provider text output.</param>
public sealed record SqlExplainQueryResult(
    string Query,
    string ExplainQuery,
    string Format,
    SqlQueryClassification Classification,
    SqlExplainNodeSummary? RootNode,
    string PlanPayload);