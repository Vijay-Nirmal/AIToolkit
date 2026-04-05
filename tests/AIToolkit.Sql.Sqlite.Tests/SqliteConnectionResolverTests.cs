using Microsoft.Data.Sqlite;

namespace AIToolkit.Sql.Sqlite.Tests;

[TestClass]
public class SqliteConnectionResolverTests
{
    [TestMethod]
    public async Task OpenConnectionUsesNamedConnectionOptionsProfile()
    {
        var resolver = new SqliteConnectionResolver(
            [new SqliteConnectionProfile { Name = "local", ConnectionOptions = new SqliteConnectionOptions { DataSource = "app.db" } }],
            new FakeConnectionFactory());

        await using var connection = await resolver.OpenConnectionAsync(new SqlConnectionTarget("local", "ignored"));
        var builder = new SqliteConnectionStringBuilder(connection.ConnectionString);

        Assert.AreEqual("app.db", builder.DataSource);
    }

    [TestMethod]
    public async Task OpenConnectionUsesNamedConnectionStringProfile()
    {
        var resolver = new SqliteConnectionResolver(
            [new SqliteConnectionProfile { Name = "retail", ConnectionString = "Data Source=retail.db" }],
            new FakeConnectionFactory());

        await using var connection = await resolver.OpenConnectionAsync(new SqlConnectionTarget("retail"));
        var builder = new SqliteConnectionStringBuilder(connection.ConnectionString);

        Assert.AreEqual("retail.db", builder.DataSource);
    }

    private sealed class FakeConnectionFactory : SqliteConnectionFactory
    {
        public override ValueTask<SqliteConnection> OpenConnectionAsync(string connectionString, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(CreateConnection(connectionString));
    }
}