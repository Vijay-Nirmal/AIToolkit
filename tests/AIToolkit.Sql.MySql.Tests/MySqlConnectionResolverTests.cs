using MySqlConnector;

namespace AIToolkit.Sql.MySql.Tests;

[TestClass]
public class MySqlConnectionResolverTests
{
    [TestMethod]
    public async Task OpenConnectionUsesNamedConnectionAndDatabaseOverride()
    {
        var resolver = new MySqlConnectionResolver(
            [
                new MySqlConnectionProfile
                {
                    Name = "local",
                    ConnectionOptions = new MySqlConnectionOptions
                    {
                        Server = "localhost",
                        Database = "sales",
                        UserId = "root",
                        Password = "password"
                    }
                }
            ],
            new FakeConnectionFactory());

        await using var connection = await resolver.OpenConnectionAsync(new SqlConnectionTarget("local", "inventory"));
        var builder = new MySqlConnectionStringBuilder(connection.ConnectionString);

        Assert.AreEqual("localhost", builder.Server);
        Assert.AreEqual("inventory", builder.Database);
    }

    [TestMethod]
    public async Task OpenConnectionUsesNamedConnectionStringProfile()
    {
        var resolver = new MySqlConnectionResolver(
            [new MySqlConnectionProfile { Name = "retail", ConnectionString = "Server=localhost;Database=retaildemo;User ID=root;Password=password" }],
            new FakeConnectionFactory());

        await using var connection = await resolver.OpenConnectionAsync(new SqlConnectionTarget("retail"));
        var builder = new MySqlConnectionStringBuilder(connection.ConnectionString);

        Assert.AreEqual("localhost", builder.Server);
        Assert.AreEqual("retaildemo", builder.Database);
        Assert.AreEqual("root", builder.UserID);
    }

    private sealed class FakeConnectionFactory : MySqlConnectionFactory
    {
        public override ValueTask<MySqlConnection> OpenConnectionAsync(string connectionString, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(CreateConnection(connectionString));
    }
}