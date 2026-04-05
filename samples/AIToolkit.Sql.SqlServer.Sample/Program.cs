using AIToolkit.Sql.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenAI;
using System.ClientModel;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddUserSecrets<Program>(optional: true)
    .Build();

var connectionName = GetSetting(configuration, "SqlServer:ConnectionName");
var adminConnectionString = GetSetting(configuration, "SqlServer:AdminConnectionString");
var databaseName = GetSetting(configuration, "SqlServer:DatabaseName");
var apiKey = configuration["OpenAI:ApiKey"];

if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("OpenAI:ApiKey is not configured.");
    Console.Error.WriteLine("Set it with:");
    Console.Error.WriteLine("dotnet user-secrets set \"OpenAI:ApiKey\" \"<your-api-key>\" --project samples/AIToolkit.Sql.SqlServer.Sample");
    return;
}

await EnsureSampleDatabaseAsync(adminConnectionString, databaseName);

var tools = SqlServerTools.CreateFunctions(connectionString: adminConnectionString, executionPolicy: new() { AllowMutations = true, RequireApprovalForMutations = false }, connectionName: connectionName);

var servers = new ServiceCollection()
    .AddLogging(builder => builder.AddConsole());

IChatClient agent = new ChatClientBuilder(CreateChatClient(configuration))
    .UseFunctionInvocation()
    .Build(servers.BuildServiceProvider());

var chatOptions = new ChatOptions
{
    Tools = [.. tools],
};

List<ChatMessage> chatHistory =
[
    new(ChatRole.System, $"""
		You are a SQL Server data assistant for the {databaseName} database.
		All SQL tools are stateless.
		Always pass connectionName set to {connectionName} when calling a SQL tool.
		Use metadata tools before writing SQL when the schema is unclear.
		Mention the table or view names you used when presenting results.
		""")
];

Console.WriteLine("AIToolkit SQL Server sample agent");
Console.WriteLine($"Sample database ready: {databaseName}");
Console.WriteLine($"Configured connection name: {connectionName}");
Console.WriteLine("Try prompts like:");
Console.WriteLine("- Summarize the available schema.");
Console.WriteLine("- Show the top 5 customers by revenue.");
Console.WriteLine("- Which products sold the most units in the last 90 days?");
Console.WriteLine("Type 'exit' to quit.");
Console.WriteLine();

while (true)
{
    Console.Write("> ");
    var prompt = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(prompt))
    {
        continue;
    }

    if (string.Equals(prompt, "exit", StringComparison.OrdinalIgnoreCase))
    {
        break;
    }

    chatHistory.Add(new ChatMessage(ChatRole.User, prompt));

    Console.WriteLine();
    Console.WriteLine("Assistant:");

    var responseText = string.Empty;
    await foreach (var update in agent.GetStreamingResponseAsync(chatHistory, chatOptions))
    {
        if (!string.IsNullOrWhiteSpace(update.Text))
        {
            Console.Write(update.Text);
            responseText += update.Text;
        }
    }

    chatHistory.Add(new ChatMessage(ChatRole.Assistant, responseText));
    Console.WriteLine();
    Console.WriteLine();
}

static IChatClient CreateChatClient(IConfiguration configuration)
{
    var model = GetSetting(configuration, "OpenAI:Model");
    var endpoint = GetSetting(configuration, "OpenAI:Endpoint");
    var apiKey = GetSetting(configuration, "OpenAI:ApiKey");

    return new OpenAI.Chat.ChatClient(
            model,
            new ApiKeyCredential(apiKey),
            new OpenAIClientOptions
            {
                Endpoint = new Uri(endpoint, UriKind.Absolute),
            })
        .AsIChatClient();
}

static string GetSetting(IConfiguration configuration, string key)
{
    var value = configuration[key];
    if (!string.IsNullOrWhiteSpace(value))
    {
        return value;
    }

    throw new InvalidOperationException(
        $"Configuration value '{key}' is required. Set it in appsettings.json or dotnet user-secrets.");
}

#region Sample Database Setup
static async Task EnsureSampleDatabaseAsync(string adminConnectionString, string databaseName)
{
    var masterConnectionString = BuildConnectionString(
        adminConnectionString,
        "master",
        "AIToolkit.Sql.SqlServer.Sample.Setup");

    await ExecuteNonQueryAsync(
        masterConnectionString,
        $"""
		IF DB_ID(N'{EscapeSqlLiteral(databaseName)}') IS NULL
		BEGIN
			CREATE DATABASE {EscapeSqlIdentifier(databaseName)};
		END;
		""");

    var databaseConnectionString = BuildConnectionString(
        adminConnectionString,
        databaseName,
        "AIToolkit.Sql.SqlServer.Sample.Setup");

    await ExecuteNonQueryAsync(databaseConnectionString, GetSchemaScript());
    await ExecuteNonQueryAsync(databaseConnectionString, GetSeedScript());
    await ExecuteNonQueryAsync(databaseConnectionString, GetViewScript());
    await ExecuteNonQueryAsync(databaseConnectionString, GetFunctionScript());
    await ExecuteNonQueryAsync(databaseConnectionString, GetProcedureScript());
}

static async Task ExecuteNonQueryAsync(string connectionString, string commandText)
{
    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();

    await using var command = connection.CreateCommand();
    command.CommandText = commandText;
    command.CommandTimeout = 60;
    await command.ExecuteNonQueryAsync();
}

static string BuildConnectionString(string connectionString, string database, string applicationName)
{
    var builder = new SqlConnectionStringBuilder(connectionString)
    {
        InitialCatalog = database,
        ApplicationName = applicationName,
    };

    return builder.ConnectionString;
}

static string EscapeSqlLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);

static string EscapeSqlIdentifier(string value) => $"[{value.Replace("]", "]]", StringComparison.Ordinal)}]";

static string GetSchemaScript() => """
IF OBJECT_ID(N'dbo.Customers', N'U') IS NULL
BEGIN
	CREATE TABLE dbo.Customers
	(
		CustomerId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
		CustomerCode NVARCHAR(20) NOT NULL UNIQUE,
		CustomerName NVARCHAR(120) NOT NULL,
		Region NVARCHAR(50) NOT NULL,
		Segment NVARCHAR(50) NOT NULL
	);
END;

IF OBJECT_ID(N'dbo.Products', N'U') IS NULL
BEGIN
	CREATE TABLE dbo.Products
	(
		ProductId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
		Sku NVARCHAR(30) NOT NULL UNIQUE,
		ProductName NVARCHAR(120) NOT NULL,
		Category NVARCHAR(50) NOT NULL,
		UnitPrice DECIMAL(18,2) NOT NULL
	);
END;

IF OBJECT_ID(N'dbo.SalesOrders', N'U') IS NULL
BEGIN
	CREATE TABLE dbo.SalesOrders
	(
		SalesOrderId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
		OrderNumber NVARCHAR(20) NOT NULL UNIQUE,
		CustomerId INT NOT NULL,
		OrderDate DATE NOT NULL,
		Status NVARCHAR(20) NOT NULL,
		SalesRep NVARCHAR(80) NOT NULL,
		CONSTRAINT FK_SalesOrders_Customers FOREIGN KEY (CustomerId) REFERENCES dbo.Customers(CustomerId)
	);
END;

IF OBJECT_ID(N'dbo.SalesOrderLines', N'U') IS NULL
BEGIN
	CREATE TABLE dbo.SalesOrderLines
	(
		SalesOrderLineId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
		SalesOrderId INT NOT NULL,
		ProductId INT NOT NULL,
		Quantity INT NOT NULL,
		UnitPrice DECIMAL(18,2) NOT NULL,
		CONSTRAINT FK_SalesOrderLines_SalesOrders FOREIGN KEY (SalesOrderId) REFERENCES dbo.SalesOrders(SalesOrderId),
		CONSTRAINT FK_SalesOrderLines_Products FOREIGN KEY (ProductId) REFERENCES dbo.Products(ProductId),
		CONSTRAINT UQ_SalesOrderLines UNIQUE (SalesOrderId, ProductId)
	);
END;
""";

static string GetSeedScript() => """
MERGE dbo.Customers AS target
USING (VALUES
	(N'CUST-1001', N'Alpine Outfitters', N'Seattle', N'Outdoor Retail'),
	(N'CUST-1002', N'Blue Yonder Market', N'Denver', N'Specialty Retail'),
	(N'CUST-1003', N'Contoso Home', N'Austin', N'E-Commerce'),
	(N'CUST-1004', N'Northwind Studio', N'Chicago', N'Corporate Procurement')
) AS source (CustomerCode, CustomerName, Region, Segment)
ON target.CustomerCode = source.CustomerCode
WHEN MATCHED THEN
	UPDATE SET
		CustomerName = source.CustomerName,
		Region = source.Region,
		Segment = source.Segment
WHEN NOT MATCHED THEN
	INSERT (CustomerCode, CustomerName, Region, Segment)
	VALUES (source.CustomerCode, source.CustomerName, source.Region, source.Segment);

MERGE dbo.Products AS target
USING (VALUES
	(N'TRL-100', N'Trail Backpack 28L', N'Gear', CAST(129.00 AS DECIMAL(18,2))),
	(N'TRL-200', N'Carbon Trekking Poles', N'Gear', CAST(89.00 AS DECIMAL(18,2))),
	(N'APP-110', N'All-Weather Shell Jacket', N'Apparel', CAST(179.00 AS DECIMAL(18,2))),
	(N'APP-210', N'Merino Base Layer', N'Apparel', CAST(69.00 AS DECIMAL(18,2))),
	(N'ELC-310', N'GPS Adventure Watch', N'Electronics', CAST(249.00 AS DECIMAL(18,2))),
	(N'ELC-410', N'Portable Solar Charger', N'Electronics', CAST(119.00 AS DECIMAL(18,2)))
) AS source (Sku, ProductName, Category, UnitPrice)
ON target.Sku = source.Sku
WHEN MATCHED THEN
	UPDATE SET
		ProductName = source.ProductName,
		Category = source.Category,
		UnitPrice = source.UnitPrice
WHEN NOT MATCHED THEN
	INSERT (Sku, ProductName, Category, UnitPrice)
	VALUES (source.Sku, source.ProductName, source.Category, source.UnitPrice);

MERGE dbo.SalesOrders AS target
USING (
	SELECT
		orders.OrderNumber,
		customer.CustomerId,
		orders.OrderDate,
		orders.Status,
		orders.SalesRep
	FROM (VALUES
		(N'SO-10001', N'CUST-1001', CONVERT(date, DATEADD(DAY, -21, SYSDATETIME())), N'Closed', N'Maya Patel'),
		(N'SO-10002', N'CUST-1002', CONVERT(date, DATEADD(DAY, -16, SYSDATETIME())), N'Closed', N'Jordan Lee'),
		(N'SO-10003', N'CUST-1003', CONVERT(date, DATEADD(DAY, -9, SYSDATETIME())), N'Shipped', N'Maya Patel'),
		(N'SO-10004', N'CUST-1001', CONVERT(date, DATEADD(DAY, -5, SYSDATETIME())), N'Closed', N'Jordan Lee'),
		(N'SO-10005', N'CUST-1004', CONVERT(date, DATEADD(DAY, -2, SYSDATETIME())), N'Processing', N'Elena Garcia')
	) AS orders (OrderNumber, CustomerCode, OrderDate, Status, SalesRep)
	INNER JOIN dbo.Customers AS customer ON customer.CustomerCode = orders.CustomerCode
) AS source
ON target.OrderNumber = source.OrderNumber
WHEN MATCHED THEN
	UPDATE SET
		CustomerId = source.CustomerId,
		OrderDate = source.OrderDate,
		Status = source.Status,
		SalesRep = source.SalesRep
WHEN NOT MATCHED THEN
	INSERT (OrderNumber, CustomerId, OrderDate, Status, SalesRep)
	VALUES (source.OrderNumber, source.CustomerId, source.OrderDate, source.Status, source.SalesRep);

MERGE dbo.SalesOrderLines AS target
USING (
	SELECT
		salesOrder.SalesOrderId,
		product.ProductId,
		lines.Quantity,
		lines.UnitPrice
	FROM (VALUES
		(N'SO-10001', N'TRL-100', 8, CAST(129.00 AS DECIMAL(18,2))),
		(N'SO-10001', N'ELC-410', 4, CAST(119.00 AS DECIMAL(18,2))),
		(N'SO-10002', N'APP-110', 6, CAST(179.00 AS DECIMAL(18,2))),
		(N'SO-10002', N'APP-210', 12, CAST(69.00 AS DECIMAL(18,2))),
		(N'SO-10003', N'ELC-310', 5, CAST(249.00 AS DECIMAL(18,2))),
		(N'SO-10003', N'TRL-200', 10, CAST(89.00 AS DECIMAL(18,2))),
		(N'SO-10004', N'TRL-100', 3, CAST(129.00 AS DECIMAL(18,2))),
		(N'SO-10004', N'APP-110', 2, CAST(179.00 AS DECIMAL(18,2))),
		(N'SO-10005', N'ELC-410', 7, CAST(119.00 AS DECIMAL(18,2))),
		(N'SO-10005', N'APP-210', 9, CAST(69.00 AS DECIMAL(18,2)))
	) AS lines (OrderNumber, Sku, Quantity, UnitPrice)
	INNER JOIN dbo.SalesOrders AS salesOrder ON salesOrder.OrderNumber = lines.OrderNumber
	INNER JOIN dbo.Products AS product ON product.Sku = lines.Sku
) AS source
ON target.SalesOrderId = source.SalesOrderId AND target.ProductId = source.ProductId
WHEN MATCHED THEN
	UPDATE SET
		Quantity = source.Quantity,
		UnitPrice = source.UnitPrice
WHEN NOT MATCHED THEN
	INSERT (SalesOrderId, ProductId, Quantity, UnitPrice)
	VALUES (source.SalesOrderId, source.ProductId, source.Quantity, source.UnitPrice);
""";

static string GetViewScript() => """
EXEC(N'
CREATE OR ALTER VIEW dbo.vOrderRevenue
AS
SELECT
	orderHeader.OrderNumber,
	customer.CustomerCode,
	customer.CustomerName,
	orderHeader.OrderDate,
	orderHeader.Status,
	orderHeader.SalesRep,
	SUM(orderLine.Quantity * orderLine.UnitPrice) AS Revenue
FROM dbo.SalesOrders AS orderHeader
INNER JOIN dbo.Customers AS customer ON customer.CustomerId = orderHeader.CustomerId
INNER JOIN dbo.SalesOrderLines AS orderLine ON orderLine.SalesOrderId = orderHeader.SalesOrderId
GROUP BY
	orderHeader.OrderNumber,
	customer.CustomerCode,
	customer.CustomerName,
	orderHeader.OrderDate,
	orderHeader.Status,
	orderHeader.SalesRep;
');
""";

static string GetFunctionScript() => """
EXEC(N'
CREATE OR ALTER FUNCTION dbo.fn_customer_lifetime_value(@CustomerCode NVARCHAR(20))
RETURNS DECIMAL(18,2)
AS
BEGIN
	DECLARE @Total DECIMAL(18,2);

	SELECT @Total = SUM(orderLine.Quantity * orderLine.UnitPrice)
	FROM dbo.SalesOrders AS orderHeader
	INNER JOIN dbo.Customers AS customer ON customer.CustomerId = orderHeader.CustomerId
	INNER JOIN dbo.SalesOrderLines AS orderLine ON orderLine.SalesOrderId = orderHeader.SalesOrderId
	WHERE customer.CustomerCode = @CustomerCode;

	RETURN ISNULL(@Total, 0);
END;
');
""";

static string GetProcedureScript() => """
EXEC(N'
CREATE OR ALTER PROCEDURE dbo.usp_top_products_last_90_days
AS
BEGIN
	SET NOCOUNT ON;

	SELECT TOP (5)
		product.Sku,
		product.ProductName,
		SUM(orderLine.Quantity) AS UnitsSold,
		SUM(orderLine.Quantity * orderLine.UnitPrice) AS Revenue
	FROM dbo.SalesOrderLines AS orderLine
	INNER JOIN dbo.SalesOrders AS orderHeader ON orderHeader.SalesOrderId = orderLine.SalesOrderId
	INNER JOIN dbo.Products AS product ON product.ProductId = orderLine.ProductId
	WHERE orderHeader.OrderDate >= CONVERT(date, DATEADD(DAY, -90, SYSDATETIME()))
	GROUP BY product.Sku, product.ProductName
	ORDER BY Revenue DESC;
END;
');
""";

#endregion
