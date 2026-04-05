namespace AIToolkit.Sql.Tests;

[TestClass]
public class SqlMutationApproverTests
{
    [TestMethod]
    public async Task AllowAllApproverAlwaysApproves()
    {
        var approver = new AllowAllSqlMutationApprover();

        var decision = await approver.ApproveAsync(
            new SqlMutationApprovalRequest(
                new SqlConnectionTarget("connection-1"),
                "DELETE FROM dbo.Customers",
                new SqlQueryClassification(["DELETE"], SqlStatementSafety.ApprovalRequired)));

        Assert.IsTrue(decision.Approved);
    }

    [TestMethod]
    public async Task DelegateApproverUsesCallback()
    {
        var approver = new DelegateSqlMutationApprover(
            static (request, cancellationToken) =>
            {
                Assert.AreEqual("connection-2", request.Target.ConnectionName);
                return ValueTask.FromResult(SqlMutationApprovalDecision.Deny("Denied by test."));
            });

        var decision = await approver.ApproveAsync(
            new SqlMutationApprovalRequest(
                new SqlConnectionTarget("connection-2"),
                "UPDATE dbo.Customers SET IsActive = 0",
                new SqlQueryClassification(["UPDATE"], SqlStatementSafety.ApprovalRequired)));

        Assert.IsFalse(decision.Approved);
        Assert.AreEqual("Denied by test.", decision.Reason);
    }
}