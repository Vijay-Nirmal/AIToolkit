using AIToolkit.Tools.Sql.Sqlite;
using Microsoft.Data.Sqlite;
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

var connectionName = GetSetting(configuration, "Sqlite:ConnectionName");
var connectionString = GetSetting(configuration, "Sqlite:ConnectionString");
var catalogName = GetSetting(configuration, "Sqlite:CatalogName");
var apiKey = configuration["OpenAI:ApiKey"];

if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("OpenAI:ApiKey is not configured.");
    Console.Error.WriteLine("Set it with:");
    Console.Error.WriteLine("dotnet user-secrets set \"OpenAI:ApiKey\" \"<your-api-key>\" --project samples/AIToolkit.Tools.Sql.Sqlite.Sample");
    return;
}

await EnsureSampleDatabaseAsync(connectionString);

var tools = SqliteTools.CreateFunctions(
    connectionString: connectionString,
    executionPolicy: new() { AllowMutations = true, RequireApprovalForMutations = false },
    connectionName: connectionName);

var services = new ServiceCollection()
    .AddLogging(builder => builder.AddConsole());

IChatClient agent = new ChatClientBuilder(CreateChatClient(configuration))
    .UseFunctionInvocation()
    .Build(services.BuildServiceProvider());

var chatOptions = new ChatOptions
{
    Tools = [.. tools],
};

List<ChatMessage> chatHistory =
[
    new(ChatRole.System, $"""
		You are a SQLite data assistant for the configured sample database.
		All SQL tools are stateless.
		Always pass connectionName set to {connectionName} when calling a SQL tool.
		Use metadata tools before writing SQL when the schema is unclear.
		SQLite does not expose stored procedures, so focus on tables, views, and ad-hoc queries.
		Use catalog name {catalogName} when a tool call asks for database.
		""")
];

Console.WriteLine("AIToolkit SQLite sample agent");
Console.WriteLine($"Sample catalog ready: {catalogName}");
Console.WriteLine($"Configured connection name: {connectionName}");
Console.WriteLine("Try prompts like:");
Console.WriteLine("- Summarize the available schema.");
Console.WriteLine("- Show the top 5 customers by revenue.");
Console.WriteLine("- Explain whether a query is using an index.");
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

    throw new InvalidOperationException($"Configuration value '{key}' is required. Set it in appsettings.json or dotnet user-secrets.");
}

static async Task EnsureSampleDatabaseAsync(string connectionString)
{
    await ExecuteNonQueryAsync(connectionString, GetSchemaScript());
    await ExecuteNonQueryAsync(connectionString, GetSeedScript());
    await ExecuteNonQueryAsync(connectionString, GetViewScript());
}

static async Task ExecuteNonQueryAsync(string connectionString, string commandText)
{
    await using var connection = new SqliteConnection(connectionString);
    await connection.OpenAsync();

    await using var command = connection.CreateCommand();
    command.CommandText = commandText;
    command.CommandTimeout = 60;
    await command.ExecuteNonQueryAsync();
}

static string GetSchemaScript() => """
CREATE TABLE IF NOT EXISTS customers
(
	customer_id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
	customer_code TEXT NOT NULL UNIQUE,
	customer_name TEXT NOT NULL,
	region TEXT NOT NULL,
	segment TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS products
(
	product_id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
	sku TEXT NOT NULL UNIQUE,
	product_name TEXT NOT NULL,
	category TEXT NOT NULL,
	unit_price NUMERIC NOT NULL
);

CREATE TABLE IF NOT EXISTS sales_orders
(
	sales_order_id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
	order_number TEXT NOT NULL UNIQUE,
	customer_id INTEGER NOT NULL,
	order_date TEXT NOT NULL,
	status TEXT NOT NULL,
	sales_rep TEXT NOT NULL,
	FOREIGN KEY (customer_id) REFERENCES customers(customer_id)
);

CREATE TABLE IF NOT EXISTS sales_order_lines
(
	sales_order_line_id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
	sales_order_id INTEGER NOT NULL,
	product_id INTEGER NOT NULL,
	quantity INTEGER NOT NULL,
	unit_price NUMERIC NOT NULL,
	UNIQUE (sales_order_id, product_id),
	FOREIGN KEY (sales_order_id) REFERENCES sales_orders(sales_order_id),
	FOREIGN KEY (product_id) REFERENCES products(product_id)
);

CREATE INDEX IF NOT EXISTS ix_sales_orders_order_date ON sales_orders(order_date);
CREATE INDEX IF NOT EXISTS ix_sales_order_lines_sales_order_id ON sales_order_lines(sales_order_id);
""";

static string GetSeedScript() => """
INSERT INTO customers (customer_code, customer_name, region, segment)
VALUES
	('CUST-1001', 'Alpine Outfitters', 'Seattle', 'Outdoor Retail'),
	('CUST-1002', 'Blue Yonder Market', 'Denver', 'Specialty Retail'),
	('CUST-1003', 'Contoso Home', 'Austin', 'E-Commerce'),
	('CUST-1004', 'Northwind Studio', 'Chicago', 'Corporate Procurement')
ON CONFLICT(customer_code) DO UPDATE SET
	customer_name = excluded.customer_name,
	region = excluded.region,
	segment = excluded.segment;

INSERT INTO products (sku, product_name, category, unit_price)
VALUES
	('TRL-100', 'Trail Backpack 28L', 'Gear', 129.00),
	('TRL-200', 'Carbon Trekking Poles', 'Gear', 89.00),
	('APP-110', 'All-Weather Shell Jacket', 'Apparel', 179.00),
	('APP-210', 'Merino Base Layer', 'Apparel', 69.00),
	('ELC-310', 'GPS Adventure Watch', 'Electronics', 249.00),
	('ELC-410', 'Portable Solar Charger', 'Electronics', 119.00)
ON CONFLICT(sku) DO UPDATE SET
	product_name = excluded.product_name,
	category = excluded.category,
	unit_price = excluded.unit_price;

INSERT INTO sales_orders (order_number, customer_id, order_date, status, sales_rep)
SELECT
	orders.order_number,
	customers.customer_id,
	orders.order_date,
	orders.status,
	orders.sales_rep
FROM (
	SELECT 'SO-10001' AS order_number, 'CUST-1001' AS customer_code, date('now', '-21 day') AS order_date, 'Closed' AS status, 'Maya Patel' AS sales_rep
	UNION ALL SELECT 'SO-10002', 'CUST-1002', date('now', '-16 day'), 'Closed', 'Jordan Lee'
	UNION ALL SELECT 'SO-10003', 'CUST-1003', date('now', '-9 day'), 'Shipped', 'Maya Patel'
	UNION ALL SELECT 'SO-10004', 'CUST-1001', date('now', '-5 day'), 'Closed', 'Jordan Lee'
	UNION ALL SELECT 'SO-10005', 'CUST-1004', date('now', '-2 day'), 'Processing', 'Elena Garcia'
) AS orders
INNER JOIN customers ON customers.customer_code = orders.customer_code
ON CONFLICT(order_number) DO UPDATE SET
	customer_id = excluded.customer_id,
	order_date = excluded.order_date,
	status = excluded.status,
	sales_rep = excluded.sales_rep;

INSERT INTO sales_order_lines (sales_order_id, product_id, quantity, unit_price)
SELECT
	sales_orders.sales_order_id,
	products.product_id,
	lines.quantity,
	lines.unit_price
FROM (
	SELECT 'SO-10001' AS order_number, 'TRL-100' AS sku, 8 AS quantity, 129.00 AS unit_price
	UNION ALL SELECT 'SO-10001', 'ELC-410', 4, 119.00
	UNION ALL SELECT 'SO-10002', 'APP-110', 6, 179.00
	UNION ALL SELECT 'SO-10002', 'APP-210', 12, 69.00
	UNION ALL SELECT 'SO-10003', 'ELC-310', 5, 249.00
	UNION ALL SELECT 'SO-10003', 'TRL-200', 10, 89.00
	UNION ALL SELECT 'SO-10004', 'TRL-100', 3, 129.00
	UNION ALL SELECT 'SO-10004', 'APP-110', 2, 179.00
	UNION ALL SELECT 'SO-10005', 'ELC-410', 7, 119.00
	UNION ALL SELECT 'SO-10005', 'APP-210', 9, 69.00
) AS lines
INNER JOIN sales_orders ON sales_orders.order_number = lines.order_number
INNER JOIN products ON products.sku = lines.sku
ON CONFLICT(sales_order_id, product_id) DO UPDATE SET
	quantity = excluded.quantity,
	unit_price = excluded.unit_price;
""";

static string GetViewScript() => """
DROP VIEW IF EXISTS v_order_revenue;

CREATE VIEW v_order_revenue AS
SELECT
	order_header.order_number,
	customer.customer_code,
	customer.customer_name,
	order_header.order_date,
	order_header.status,
	order_header.sales_rep,
	SUM(order_line.quantity * order_line.unit_price) AS revenue
FROM sales_orders AS order_header
INNER JOIN customers AS customer ON customer.customer_id = order_header.customer_id
INNER JOIN sales_order_lines AS order_line ON order_line.sales_order_id = order_header.sales_order_id
GROUP BY
	order_header.order_number,
	customer.customer_code,
	customer.customer_name,
	order_header.order_date,
	order_header.status,
	order_header.sales_rep;
""";
