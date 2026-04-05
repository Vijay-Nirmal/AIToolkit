namespace AIToolkit.Sql.Sqlite.Tests;

[TestClass]
public class SqliteQueryClassifierTests
{
    private static readonly SqliteQueryClassifier Classifier = new();

    public static IEnumerable<object[]> ReadOnlyQueries()
    {
        yield return ["SELECT * FROM orders", new[] { "SELECT" }];
        yield return ["WITH recent AS (SELECT 1 AS id) SELECT id FROM recent", new[] { "WITH" }];
        yield return ["VALUES (1), (2)", new[] { "VALUES" }];
        yield return ["EXPLAIN QUERY PLAN SELECT * FROM orders", new[] { "EXPLAIN" }];
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
    [DataRow("ATTACH DATABASE 'analytics.db' AS analytics")]
    [DataRow("VACUUM")]
    public void ClassifyMarksMutationQueriesAsApprovalRequired(string query)
    {
        var classification = Classifier.Classify(query);

        Assert.AreEqual(SqlStatementSafety.ApprovalRequired, classification.Safety);
        StringAssert.Contains(classification.Reason ?? string.Empty, "modify database state");
    }

    [TestMethod]
    [DataRow("PRAGMA table_info(orders)")]
    [DataRow("SELECT load_extension('extension')")]
    public void ClassifyBlocksAdministrativeCommands(string query)
    {
        var classification = Classifier.Classify(query);

        Assert.AreEqual(SqlStatementSafety.Blocked, classification.Safety);
        StringAssert.Contains(classification.Reason ?? string.Empty, "blocked SQLite commands");
    }

    [TestMethod]
    [DataRow("SELECT 'DROP', [CREATE] FROM orders")]
    [DataRow("-- UPDATE orders SET status = 'CLOSED'\nSELECT * FROM orders")]
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