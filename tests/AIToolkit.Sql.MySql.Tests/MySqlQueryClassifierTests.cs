namespace AIToolkit.Sql.MySql.Tests;

[TestClass]
public class MySqlQueryClassifierTests
{
    private static readonly MySqlQueryClassifier Classifier = new();

    public static IEnumerable<object[]> ReadOnlyQueries()
    {
        yield return ["SELECT * FROM orders", new[] { "SELECT" }];
        yield return ["SHOW TABLES", new[] { "SHOW" }];
        yield return ["DESCRIBE orders", new[] { "DESCRIBE" }];
        yield return ["EXPLAIN SELECT * FROM orders", new[] { "EXPLAIN" }];
        yield return ["WITH recent AS (SELECT 1 AS id) SELECT id FROM recent", new[] { "WITH" }];
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
    [DataRow("UPDATE orders SET status = 'CLOSED'")]
    [DataRow("INSERT INTO orders(id) VALUES (1)")]
    [DataRow("SELECT id INTO @order_id FROM orders LIMIT 1")]
    [DataRow("REPLACE INTO orders(id) VALUES (1)")]
    public void ClassifyMarksMutationQueriesAsApprovalRequired(string query)
    {
        var classification = Classifier.Classify(query);

        Assert.AreEqual(SqlStatementSafety.ApprovalRequired, classification.Safety);
        StringAssert.Contains(classification.Reason ?? string.Empty, "modify database state");
    }

    [TestMethod]
    [DataRow("INSTALL PLUGIN validate_password SONAME 'validate_password.so'")]
    [DataRow("SHUTDOWN")]
    [DataRow("UNINSTALL PLUGIN validate_password")]
    public void ClassifyBlocksAdministrativeCommands(string query)
    {
        var classification = Classifier.Classify(query);

        Assert.AreEqual(SqlStatementSafety.Blocked, classification.Safety);
        StringAssert.Contains(classification.Reason ?? string.Empty, "blocked MySQL commands");
    }

    [TestMethod]
    [DataRow("SELECT 'DROP', `CREATE` FROM orders")]
    [DataRow("# UPDATE orders SET status = 'CLOSED'\nSELECT * FROM orders")]
    [DataRow("/* DELETE FROM orders */ SELECT * FROM orders")]
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