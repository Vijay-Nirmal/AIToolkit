using Microsoft.Data.SqlClient;

namespace AIToolkit.Tools.Sql.SqlServer.Tests;

[TestClass]
public class SqlServerConnectionResolverTests
{
    [TestMethod]
    public async Task OpenConnectionUsesNamedConnectionAndDatabaseOverride()
    {
        var resolver = new SqlServerConnectionResolver(
            [
                new SqlServerConnectionProfile
                {
                    Name = "local",
                    ConnectionOptions = new SqlServerConnectionOptions
                    {
                        Server = "localhost",
                        Database = "Sales",
                        UserId = "sa",
                        Password = "Password123!"
                    }
                }
            ],
            new FakeConnectionFactory());

        await using var connection = await resolver.OpenConnectionAsync(new SqlConnectionTarget("local", "Inventory"));
        var builder = new SqlConnectionStringBuilder(connection.ConnectionString);

        Assert.AreEqual("localhost", builder.DataSource);
        Assert.AreEqual("Inventory", builder.InitialCatalog);
    }

    [TestMethod]
    public async Task OpenConnectionUsesNamedConnectionStringProfile()
    {
        var resolver = new SqlServerConnectionResolver(
            [
                new SqlServerConnectionProfile
                {
                    Name = "retail",
                    ConnectionString = "Server=localhost;Database=RetailDemo;User Id=sa;Password=Password123!;TrustServerCertificate=True;"
                }
            ],
            new FakeConnectionFactory());

        await using var connection = await resolver.OpenConnectionAsync(new SqlConnectionTarget("retail"));
        var builder = new SqlConnectionStringBuilder(connection.ConnectionString);

        Assert.AreEqual("localhost", builder.DataSource);
        Assert.AreEqual("RetailDemo", builder.InitialCatalog);
        Assert.AreEqual("sa", builder.UserID);
    }

    private sealed class FakeConnectionFactory : SqlServerConnectionFactory
    {
        public override ValueTask<SqlConnection> OpenConnectionAsync(
            string connectionString,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(CreateConnection(connectionString));
    }
}