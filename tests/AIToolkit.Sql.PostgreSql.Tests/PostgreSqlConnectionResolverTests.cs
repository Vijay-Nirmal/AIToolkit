using Npgsql;

namespace AIToolkit.Sql.PostgreSql.Tests;

[TestClass]
public class PostgreSqlConnectionResolverTests
{
    [TestMethod]
    public async Task OpenConnectionUsesNamedConnectionAndDatabaseOverride()
    {
        var resolver = new PostgreSqlConnectionResolver(
            [
                new PostgreSqlConnectionProfile
                {
                    Name = "local",
                    ConnectionOptions = new PostgreSqlConnectionOptions
                    {
                        Host = "localhost",
                        Database = "sales",
                        Username = "postgres",
                        Password = "postgres"
                    }
                }
            ],
            new FakeConnectionFactory());

        await using var connection = await resolver.OpenConnectionAsync(new SqlConnectionTarget("local", "inventory"));
        var builder = new NpgsqlConnectionStringBuilder(connection.ConnectionString);

        Assert.AreEqual("localhost", builder.Host);
        Assert.AreEqual("inventory", builder.Database);
    }

    [TestMethod]
    public async Task OpenConnectionUsesNamedConnectionStringProfile()
    {
        var resolver = new PostgreSqlConnectionResolver(
            [
                new PostgreSqlConnectionProfile
                {
                    Name = "retail",
                    ConnectionString = "Host=localhost;Database=retaildemo;Username=postgres;Password=postgres"
                }
            ],
            new FakeConnectionFactory());

        await using var connection = await resolver.OpenConnectionAsync(new SqlConnectionTarget("retail"));
        var builder = new NpgsqlConnectionStringBuilder(connection.ConnectionString);

        Assert.AreEqual("localhost", builder.Host);
        Assert.AreEqual("retaildemo", builder.Database);
        Assert.AreEqual("postgres", builder.Username);
    }

    private sealed class FakeConnectionFactory : PostgreSqlConnectionFactory
    {
        public override ValueTask<NpgsqlConnection> OpenConnectionAsync(
            string connectionString,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(CreateConnection(connectionString));
    }
}