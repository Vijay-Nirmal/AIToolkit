namespace AIToolkit.Sql.PostgreSql.Tests;

[TestClass]
public class PostgreSqlQueryClassifierTests
{
    private static readonly PostgreSqlQueryClassifier Classifier = new();

    public static IEnumerable<object[]> ReadOnlyQueries()
    {
        yield return ["SELECT * FROM public.orders", new[] { "SELECT" }];
        yield return ["WITH recent AS (SELECT 1 AS id) SELECT id FROM recent", new[] { "WITH" }];
        yield return ["SHOW search_path", new[] { "SHOW" }];
        yield return ["VALUES (1), (2)", new[] { "VALUES" }];
        yield return ["EXPLAIN SELECT * FROM public.orders", new[] { "EXPLAIN" }];
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
    [DataRow("UPDATE public.orders SET is_active = false")]
    [DataRow("COPY public.orders TO STDOUT")]
    [DataRow("SELECT * INTO orders_backup FROM public.orders")]
    [DataRow("VACUUM public.orders")]
    public void ClassifyMarksMutationQueriesAsApprovalRequired(string query)
    {
        var classification = Classifier.Classify(query);

        Assert.AreEqual(SqlStatementSafety.ApprovalRequired, classification.Safety);
        StringAssert.Contains(classification.Reason ?? string.Empty, "modify database state");
    }

    [TestMethod]
    [DataRow("DO $$ BEGIN END $$")]
    [DataRow("NOTIFY channel_name, 'refresh'")]
    [DataRow("LISTEN events")]
    public void ClassifyBlocksAdministrativeCommands(string query)
    {
        var classification = Classifier.Classify(query);

        Assert.AreEqual(SqlStatementSafety.Blocked, classification.Safety);
        StringAssert.Contains(classification.Reason ?? string.Empty, "blocked PostgreSQL commands");
    }

    [TestMethod]
    [DataRow("SELECT 'DROP', \"CREATE\" FROM public.orders")]
    [DataRow("-- DELETE FROM public.orders\nSELECT * FROM public.orders")]
    [DataRow("/* UPDATE public.orders SET is_active = false */ SELECT * FROM public.orders")]
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