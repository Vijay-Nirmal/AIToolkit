namespace AIToolkit.Sql;

/// <summary>
/// Captures the information needed to approve or reject a mutation-capable SQL operation.
/// </summary>
/// <param name="Target">The named connection target that would execute the SQL text.</param>
/// <param name="Query">The SQL text awaiting approval.</param>
/// <param name="Classification">The safety classification produced for the query.</param>
public sealed record SqlMutationApprovalRequest(
    SqlConnectionTarget Target,
    string Query,
    SqlQueryClassification Classification);

/// <summary>
/// Represents the decision returned by an <see cref="ISqlMutationApprover"/>.
/// </summary>
/// <param name="Approved"><see langword="true"/> when execution may continue; otherwise, <see langword="false"/>.</param>
/// <param name="Reason">An optional human-readable explanation for the decision.</param>
public sealed record SqlMutationApprovalDecision(bool Approved, string? Reason = null)
{
    /// <summary>
    /// Creates an approval decision that allows execution to continue.
    /// </summary>
    /// <param name="reason">An optional explanation for why the request was approved.</param>
    /// <returns>An approval decision with <see cref="Approved"/> set to <see langword="true"/>.</returns>
    public static SqlMutationApprovalDecision Allow(string? reason = null) => new(true, reason);

    /// <summary>
    /// Creates an approval decision that blocks execution.
    /// </summary>
    /// <param name="reason">An optional explanation for why the request was denied.</param>
    /// <returns>An approval decision with <see cref="Approved"/> set to <see langword="false"/>.</returns>
    public static SqlMutationApprovalDecision Deny(string? reason = null) => new(false, reason);
}

/// <summary>
/// Adapts a delegate into an <see cref="ISqlMutationApprover"/> implementation.
/// </summary>
/// <param name="callback">The delegate that will evaluate each approval request.</param>
public sealed class DelegateSqlMutationApprover(
    Func<SqlMutationApprovalRequest, CancellationToken, ValueTask<SqlMutationApprovalDecision>> callback)
    : ISqlMutationApprover
{
    private readonly Func<SqlMutationApprovalRequest, CancellationToken, ValueTask<SqlMutationApprovalDecision>> _callback =
        callback ?? throw new ArgumentNullException(nameof(callback));

    /// <inheritdoc />
    public ValueTask<SqlMutationApprovalDecision> ApproveAsync(
        SqlMutationApprovalRequest request,
        CancellationToken cancellationToken = default) => _callback(request, cancellationToken);
}

/// <summary>
/// Approves all mutation requests without additional checks.
/// </summary>
public sealed class AllowAllSqlMutationApprover : ISqlMutationApprover
{
    /// <inheritdoc />
    public ValueTask<SqlMutationApprovalDecision> ApproveAsync(
        SqlMutationApprovalRequest request,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(SqlMutationApprovalDecision.Allow("Approved by AllowAllSqlMutationApprover."));
}

/// <summary>
/// Rejects all mutation requests.
/// </summary>
public sealed class DenyAllSqlMutationApprover : ISqlMutationApprover
{
    /// <inheritdoc />
    public ValueTask<SqlMutationApprovalDecision> ApproveAsync(
        SqlMutationApprovalRequest request,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(SqlMutationApprovalDecision.Deny("Mutations are disabled for this tool host."));
}