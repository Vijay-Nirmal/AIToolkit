using AIToolkit.Sql.MySql;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using OpenAI;
using System.ClientModel;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddUserSecrets<Program>(optional: true)
    .Build();

var connectionName = GetSetting(configuration, "MySql:ConnectionName");
var adminConnectionString = GetSetting(configuration, "MySql:AdminConnectionString");
var databaseName = GetSetting(configuration, "MySql:DatabaseName");
var apiKey = configuration["OpenAI:ApiKey"];

if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("OpenAI:ApiKey is not configured.");
    Console.Error.WriteLine("Set it with:");
    Console.Error.WriteLine("dotnet user-secrets set \"OpenAI:ApiKey\" \"<your-api-key>\" --project samples/AIToolkit.Sql.MySql.Sample");
    return;
}

await EnsureSampleDatabaseAsync(adminConnectionString, databaseName);

var connectionString = BuildConnectionString(adminConnectionString, databaseName, "AIToolkit.Sql.MySql.Sample");
var tools = MySqlTools.CreateFunctions(
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
		You are a MySQL data assistant for the {databaseName} database.
		All SQL tools are stateless.
		Always pass connectionName set to {connectionName} when calling a SQL tool.
		Use metadata tools before writing SQL when the schema is unclear.
		Mention the table, view, or routine names you used when presenting results.
		""")
];

Console.WriteLine("AIToolkit MySQL sample agent");
Console.WriteLine($"Sample database ready: {databaseName}");
Console.WriteLine($"Configured connection name: {connectionName}");
Console.WriteLine("Try prompts like:");
Console.WriteLine("- Summarize the available schema.");
Console.WriteLine("- Show the top 5 customers by revenue.");
Console.WriteLine("- Explain why a query over sales_orders might be slow.");
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

static async Task EnsureSampleDatabaseAsync(string adminConnectionString, string databaseName)
{
    var setupConnectionString = BuildConnectionString(adminConnectionString, "mysql", "AIToolkit.Sql.MySql.Sample.Setup");
    await ExecuteNonQueryAsync(setupConnectionString, $"CREATE DATABASE IF NOT EXISTS {EscapeMySqlIdentifier(databaseName)};");

    var databaseConnectionString = BuildConnectionString(adminConnectionString, databaseName, "AIToolkit.Sql.MySql.Sample.Setup");

    await ExecuteNonQueryAsync(databaseConnectionString, GetSchemaScript());
    await ExecuteNonQueryAsync(databaseConnectionString, GetSeedScript());
    await ExecuteNonQueryAsync(databaseConnectionString, GetViewScript());
    await ExecuteNonQueryAsync(databaseConnectionString, GetFunctionScript());
    await ExecuteNonQueryAsync(databaseConnectionString, GetProcedureScript());
}

static async Task ExecuteNonQueryAsync(string connectionString, string commandText)
{
    await using var connection = new MySqlConnection(connectionString);
    await connection.OpenAsync();

    await using var command = connection.CreateCommand();
    command.CommandText = commandText;
    command.CommandTimeout = 60;
    await command.ExecuteNonQueryAsync();
}

static string BuildConnectionString(string connectionString, string database, string applicationName)
{
    var builder = new MySqlConnectionStringBuilder(connectionString)
    {
        Database = database,
        ApplicationName = applicationName,
    };

    return builder.ConnectionString;
}

static string EscapeMySqlIdentifier(string value) => $"`{value.Replace("`", "``", StringComparison.Ordinal)}`";

static string GetSchemaScript() => """
CREATE TABLE IF NOT EXISTS customers
(
	customer_id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
	customer_code VARCHAR(20) NOT NULL UNIQUE,
	customer_name VARCHAR(120) NOT NULL,
	region VARCHAR(50) NOT NULL,
	segment VARCHAR(50) NOT NULL
);

CREATE TABLE IF NOT EXISTS products
(
	product_id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
	sku VARCHAR(30) NOT NULL UNIQUE,
	product_name VARCHAR(120) NOT NULL,
	category VARCHAR(50) NOT NULL,
	unit_price DECIMAL(18,2) NOT NULL
);

CREATE TABLE IF NOT EXISTS sales_orders
(
	sales_order_id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
	order_number VARCHAR(20) NOT NULL UNIQUE,
	customer_id INT NOT NULL,
	order_date DATE NOT NULL,
	status VARCHAR(20) NOT NULL,
	sales_rep VARCHAR(80) NOT NULL,
	CONSTRAINT fk_sales_orders_customers FOREIGN KEY (customer_id) REFERENCES customers(customer_id)
);

CREATE TABLE IF NOT EXISTS sales_order_lines
(
	sales_order_line_id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
	sales_order_id INT NOT NULL,
	product_id INT NOT NULL,
	quantity INT NOT NULL,
	unit_price DECIMAL(18,2) NOT NULL,
	CONSTRAINT fk_sales_order_lines_orders FOREIGN KEY (sales_order_id) REFERENCES sales_orders(sales_order_id),
	CONSTRAINT fk_sales_order_lines_products FOREIGN KEY (product_id) REFERENCES products(product_id),
	CONSTRAINT uq_sales_order_lines UNIQUE (sales_order_id, product_id)
);
""";

static string GetSeedScript() => """
INSERT INTO customers (customer_code, customer_name, region, segment)
VALUES
	('CUST-1001', 'Alpine Outfitters', 'Seattle', 'Outdoor Retail'),
	('CUST-1002', 'Blue Yonder Market', 'Denver', 'Specialty Retail'),
	('CUST-1003', 'Contoso Home', 'Austin', 'E-Commerce'),
	('CUST-1004', 'Northwind Studio', 'Chicago', 'Corporate Procurement')
ON DUPLICATE KEY UPDATE
	customer_name = VALUES(customer_name),
	region = VALUES(region),
	segment = VALUES(segment);

INSERT INTO products (sku, product_name, category, unit_price)
VALUES
	('TRL-100', 'Trail Backpack 28L', 'Gear', 129.00),
	('TRL-200', 'Carbon Trekking Poles', 'Gear', 89.00),
	('APP-110', 'All-Weather Shell Jacket', 'Apparel', 179.00),
	('APP-210', 'Merino Base Layer', 'Apparel', 69.00),
	('ELC-310', 'GPS Adventure Watch', 'Electronics', 249.00),
	('ELC-410', 'Portable Solar Charger', 'Electronics', 119.00)
ON DUPLICATE KEY UPDATE
	product_name = VALUES(product_name),
	category = VALUES(category),
	unit_price = VALUES(unit_price);

INSERT INTO sales_orders (order_number, customer_id, order_date, status, sales_rep)
SELECT
	orders.order_number,
	customers.customer_id,
	orders.order_date,
	orders.status,
	orders.sales_rep
FROM (
	SELECT 'SO-10001' AS order_number, 'CUST-1001' AS customer_code, CURDATE() - INTERVAL 21 DAY AS order_date, 'Closed' AS status, 'Maya Patel' AS sales_rep
	UNION ALL SELECT 'SO-10002', 'CUST-1002', CURDATE() - INTERVAL 16 DAY, 'Closed', 'Jordan Lee'
	UNION ALL SELECT 'SO-10003', 'CUST-1003', CURDATE() - INTERVAL 9 DAY, 'Shipped', 'Maya Patel'
	UNION ALL SELECT 'SO-10004', 'CUST-1001', CURDATE() - INTERVAL 5 DAY, 'Closed', 'Jordan Lee'
	UNION ALL SELECT 'SO-10005', 'CUST-1004', CURDATE() - INTERVAL 2 DAY, 'Processing', 'Elena Garcia'
) AS orders
INNER JOIN customers ON customers.customer_code = orders.customer_code
ON DUPLICATE KEY UPDATE
	customer_id = VALUES(customer_id),
	order_date = VALUES(order_date),
	status = VALUES(status),
	sales_rep = VALUES(sales_rep);

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
ON DUPLICATE KEY UPDATE
	quantity = VALUES(quantity),
	unit_price = VALUES(unit_price);
""";

static string GetViewScript() => """
CREATE OR REPLACE VIEW v_order_revenue AS
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

static string GetFunctionScript() => """
DROP FUNCTION IF EXISTS fn_customer_lifetime_value;
CREATE FUNCTION fn_customer_lifetime_value(customer_code_param VARCHAR(20))
RETURNS DECIMAL(18,2)
DETERMINISTIC
READS SQL DATA
BEGIN
	DECLARE total_value DECIMAL(18,2);

	SELECT COALESCE(SUM(order_line.quantity * order_line.unit_price), 0)
	INTO total_value
	FROM sales_orders AS order_header
	INNER JOIN customers AS customer ON customer.customer_id = order_header.customer_id
	INNER JOIN sales_order_lines AS order_line ON order_line.sales_order_id = order_header.sales_order_id
	WHERE customer.customer_code = customer_code_param;

	RETURN total_value;
END;
""";

static string GetProcedureScript() => """
DROP PROCEDURE IF EXISTS usp_top_products_last_90_days;
CREATE PROCEDURE usp_top_products_last_90_days()
BEGIN
	SELECT
		products.sku,
		products.product_name,
		SUM(order_line.quantity) AS units_sold,
		SUM(order_line.quantity * order_line.unit_price) AS revenue
	FROM sales_order_lines AS order_line
	INNER JOIN sales_orders AS order_header ON order_header.sales_order_id = order_line.sales_order_id
	INNER JOIN products ON products.product_id = order_line.product_id
	WHERE order_header.order_date >= CURDATE() - INTERVAL 90 DAY
	GROUP BY products.sku, products.product_name
	ORDER BY revenue DESC
	LIMIT 5;
END;
""";
