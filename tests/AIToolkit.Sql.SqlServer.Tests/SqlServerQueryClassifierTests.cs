namespace AIToolkit.Sql.SqlServer.Tests;

[TestClass]
public class SqlServerQueryClassifierTests
{
    private static readonly SqlServerQueryClassifier Classifier = new();

    public static IEnumerable<object[]> ReadOnlyQueries()
    {
        yield return ["SELECT TOP (10) * FROM dbo.Customers", new[] { "SELECT" }];
        yield return ["WITH recent AS (SELECT 1 AS Id) SELECT Id FROM recent", new[] { "WITH" }];
        yield return ["DECLARE @CustomerId INT = 1; SELECT @CustomerId AS CustomerId", new[] { "DECLARE", "SELECT" }];
        yield return ["SET NOCOUNT ON; SELECT 1 AS Value", new[] { "SET", "SELECT" }];
    }

    [TestMethod]
    [DynamicData(nameof(ReadOnlyQueries))]
    public void ClassifyMarksReadOnlyQueriesAsReadOnly(string query, string[] expectedStatementTypes)
    {
        var classification = Classifier.Classify(query);

        CollectionAssert.AreEqual(expectedStatementTypes, classification.StatementTypes.ToArray());
        Assert.AreEqual(SqlStatementSafety.ReadOnly, classification.Safety);
        Assert.IsNull(classification.Reason);
    }

    [TestMethod]
    [DataRow("UPDATE dbo.Customers SET Name = 'X'")]
    [DataRow("INSERT INTO dbo.Customers(Name) VALUES ('X')")]
    [DataRow("SELECT * INTO dbo.CustomersBackup FROM dbo.Customers")]
    [DataRow("USE master")]
    public void ClassifyMarksMutationQueriesAsApprovalRequired(string query)
    {
        var classification = Classifier.Classify(query);

        Assert.AreEqual(SqlStatementSafety.ApprovalRequired, classification.Safety);
        StringAssert.Contains(classification.Reason ?? string.Empty, "modify");
    }

    [TestMethod]
    [DataRow("EXEC xp_cmdshell 'dir'")]
    [DataRow("DBCC CHECKDB")]
    [DataRow("OPENROWSET(BULK 'file', SINGLE_CLOB) AS Source")]
    public void ClassifyBlocksAdministrativeCommands(string query)
    {
        var classification = Classifier.Classify(query);

        Assert.AreEqual(SqlStatementSafety.Blocked, classification.Safety);
        StringAssert.Contains(classification.Reason ?? string.Empty, "blocked administrative commands");
    }

    [TestMethod]
    [DataRow("SELECT 'DROP', [CREATE] FROM dbo.Customers")]
    [DataRow("-- UPDATE dbo.Customers\nSELECT * FROM dbo.Customers")]
    [DataRow("/* DELETE FROM dbo.Customers */ SELECT * FROM dbo.Customers")]
    public void ClassifyIgnoresKeywordsInsideQuotedContentAndComments(string query)
    {
        var classification = Classifier.Classify(query);

        Assert.AreEqual(SqlStatementSafety.ReadOnly, classification.Safety);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void ClassifyBlocksEmptyInput(string query)
    {
        var classification = Classifier.Classify(query);

        Assert.AreEqual(SqlStatementSafety.Blocked, classification.Safety);
        Assert.AreEqual("Query text is required.", classification.Reason);
    }
}