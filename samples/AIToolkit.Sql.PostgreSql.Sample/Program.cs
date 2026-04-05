using AIToolkit.Sql.PostgreSql;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenAI;
using System.ClientModel;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddUserSecrets<Program>(optional: true)
    .Build();

var connectionName = GetSetting(configuration, "PostgreSql:ConnectionName");
var adminConnectionString = GetSetting(configuration, "PostgreSql:AdminConnectionString");
var databaseName = GetSetting(configuration, "PostgreSql:DatabaseName");
var apiKey = configuration["OpenAI:ApiKey"];

if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("OpenAI:ApiKey is not configured.");
    Console.Error.WriteLine("Set it with:");
    Console.Error.WriteLine("dotnet user-secrets set \"OpenAI:ApiKey\" \"<your-api-key>\" --project samples/AIToolkit.Sql.PostgreSql.Sample");
    return;
}

await EnsureSampleDatabaseAsync(adminConnectionString, databaseName);

var connectionString = BuildConnectionString(adminConnectionString, databaseName, "AIToolkit.Sql.PostgreSql.Sample");
var tools = PostgreSqlTools.CreateFunctions(
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
		You are a PostgreSQL data assistant for the {databaseName} database.
		All SQL tools are stateless.
		Always pass connectionName set to {connectionName} when calling a SQL tool.
		Use metadata tools before writing SQL when the schema is unclear.
		Mention the table, view, or function names you used when presenting results.
		""")
];

Console.WriteLine("AIToolkit PostgreSQL sample agent");
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
    await EnsureDatabaseExistsAsync(adminConnectionString, databaseName);

    var databaseConnectionString = BuildConnectionString(adminConnectionString, databaseName, "AIToolkit.Sql.PostgreSql.Sample.Setup");

    await ExecuteNonQueryAsync(databaseConnectionString, GetSchemaScript());
    await ExecuteNonQueryAsync(databaseConnectionString, GetSeedScript());
    await ExecuteNonQueryAsync(databaseConnectionString, GetViewScript());
    await ExecuteNonQueryAsync(databaseConnectionString, GetFunctionScript());
    await ExecuteNonQueryAsync(databaseConnectionString, GetProcedureScript());
}

static async Task EnsureDatabaseExistsAsync(string adminConnectionString, string databaseName)
{
    await using var connection = new NpgsqlConnection(BuildConnectionString(adminConnectionString, "postgres", "AIToolkit.Sql.PostgreSql.Sample.Setup"));
    await connection.OpenAsync();

    await using var existsCommand = connection.CreateCommand();
    existsCommand.CommandText = "SELECT 1 FROM pg_database WHERE datname = @databaseName;";
    existsCommand.Parameters.AddWithValue("databaseName", databaseName);
    var exists = await existsCommand.ExecuteScalarAsync() is not null;

    if (exists)
    {
        return;
    }

    await using var createCommand = connection.CreateCommand();
    createCommand.CommandText = $"CREATE DATABASE {EscapePgIdentifier(databaseName)};";
    await createCommand.ExecuteNonQueryAsync();
}

static async Task ExecuteNonQueryAsync(string connectionString, string commandText)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();

    await using var command = connection.CreateCommand();
    command.CommandText = commandText;
    command.CommandTimeout = 60;
    await command.ExecuteNonQueryAsync();
}

static string BuildConnectionString(string connectionString, string database, string applicationName)
{
    var builder = new NpgsqlConnectionStringBuilder(connectionString)
    {
        Database = database,
        ApplicationName = applicationName,
    };

    return builder.ConnectionString;
}

static string EscapePgIdentifier(string value) => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

static string GetSchemaScript() => """
CREATE TABLE IF NOT EXISTS public.customers
(
	customer_id INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
	customer_code VARCHAR(20) NOT NULL UNIQUE,
	customer_name VARCHAR(120) NOT NULL,
	region VARCHAR(50) NOT NULL,
	segment VARCHAR(50) NOT NULL
);

CREATE TABLE IF NOT EXISTS public.products
(
	product_id INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
	sku VARCHAR(30) NOT NULL UNIQUE,
	product_name VARCHAR(120) NOT NULL,
	category VARCHAR(50) NOT NULL,
	unit_price NUMERIC(18,2) NOT NULL
);

CREATE TABLE IF NOT EXISTS public.sales_orders
(
	sales_order_id INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
	order_number VARCHAR(20) NOT NULL UNIQUE,
	customer_id INTEGER NOT NULL REFERENCES public.customers(customer_id),
	order_date DATE NOT NULL,
	status VARCHAR(20) NOT NULL,
	sales_rep VARCHAR(80) NOT NULL
);

CREATE TABLE IF NOT EXISTS public.sales_order_lines
(
	sales_order_line_id INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
	sales_order_id INTEGER NOT NULL REFERENCES public.sales_orders(sales_order_id),
	product_id INTEGER NOT NULL REFERENCES public.products(product_id),
	quantity INTEGER NOT NULL,
	unit_price NUMERIC(18,2) NOT NULL,
	CONSTRAINT uq_sales_order_lines UNIQUE (sales_order_id, product_id)
);
""";

static string GetSeedScript() => """
INSERT INTO public.customers (customer_code, customer_name, region, segment)
VALUES
	('CUST-1001', 'Alpine Outfitters', 'Seattle', 'Outdoor Retail'),
	('CUST-1002', 'Blue Yonder Market', 'Denver', 'Specialty Retail'),
	('CUST-1003', 'Contoso Home', 'Austin', 'E-Commerce'),
	('CUST-1004', 'Northwind Studio', 'Chicago', 'Corporate Procurement')
ON CONFLICT (customer_code) DO UPDATE
SET customer_name = EXCLUDED.customer_name,
	region = EXCLUDED.region,
	segment = EXCLUDED.segment;

INSERT INTO public.products (sku, product_name, category, unit_price)
VALUES
	('TRL-100', 'Trail Backpack 28L', 'Gear', 129.00),
	('TRL-200', 'Carbon Trekking Poles', 'Gear', 89.00),
	('APP-110', 'All-Weather Shell Jacket', 'Apparel', 179.00),
	('APP-210', 'Merino Base Layer', 'Apparel', 69.00),
	('ELC-310', 'GPS Adventure Watch', 'Electronics', 249.00),
	('ELC-410', 'Portable Solar Charger', 'Electronics', 119.00)
ON CONFLICT (sku) DO UPDATE
SET product_name = EXCLUDED.product_name,
	category = EXCLUDED.category,
	unit_price = EXCLUDED.unit_price;

INSERT INTO public.sales_orders (order_number, customer_id, order_date, status, sales_rep)
SELECT
	orders.order_number,
	customers.customer_id,
	orders.order_date,
	orders.status,
	orders.sales_rep
FROM (VALUES
	('SO-10001', 'CUST-1001', CURRENT_DATE - INTERVAL '21 days', 'Closed', 'Maya Patel'),
	('SO-10002', 'CUST-1002', CURRENT_DATE - INTERVAL '16 days', 'Closed', 'Jordan Lee'),
	('SO-10003', 'CUST-1003', CURRENT_DATE - INTERVAL '9 days', 'Shipped', 'Maya Patel'),
	('SO-10004', 'CUST-1001', CURRENT_DATE - INTERVAL '5 days', 'Closed', 'Jordan Lee'),
	('SO-10005', 'CUST-1004', CURRENT_DATE - INTERVAL '2 days', 'Processing', 'Elena Garcia')
) AS orders(order_number, customer_code, order_date, status, sales_rep)
INNER JOIN public.customers AS customers ON customers.customer_code = orders.customer_code
ON CONFLICT (order_number) DO UPDATE
SET customer_id = EXCLUDED.customer_id,
	order_date = EXCLUDED.order_date,
	status = EXCLUDED.status,
	sales_rep = EXCLUDED.sales_rep;

INSERT INTO public.sales_order_lines (sales_order_id, product_id, quantity, unit_price)
SELECT
	sales_orders.sales_order_id,
	products.product_id,
	lines.quantity,
	lines.unit_price
FROM (VALUES
	('SO-10001', 'TRL-100', 8, 129.00),
	('SO-10001', 'ELC-410', 4, 119.00),
	('SO-10002', 'APP-110', 6, 179.00),
	('SO-10002', 'APP-210', 12, 69.00),
	('SO-10003', 'ELC-310', 5, 249.00),
	('SO-10003', 'TRL-200', 10, 89.00),
	('SO-10004', 'TRL-100', 3, 129.00),
	('SO-10004', 'APP-110', 2, 179.00),
	('SO-10005', 'ELC-410', 7, 119.00),
	('SO-10005', 'APP-210', 9, 69.00)
) AS lines(order_number, sku, quantity, unit_price)
INNER JOIN public.sales_orders AS sales_orders ON sales_orders.order_number = lines.order_number
INNER JOIN public.products AS products ON products.sku = lines.sku
ON CONFLICT (sales_order_id, product_id) DO UPDATE
SET quantity = EXCLUDED.quantity,
	unit_price = EXCLUDED.unit_price;
""";

static string GetViewScript() => """
CREATE OR REPLACE VIEW public.v_order_revenue AS
SELECT
	order_header.order_number,
	customer.customer_code,
	customer.customer_name,
	order_header.order_date,
	order_header.status,
	order_header.sales_rep,
	SUM(order_line.quantity * order_line.unit_price) AS revenue
FROM public.sales_orders AS order_header
INNER JOIN public.customers AS customer ON customer.customer_id = order_header.customer_id
INNER JOIN public.sales_order_lines AS order_line ON order_line.sales_order_id = order_header.sales_order_id
GROUP BY
	order_header.order_number,
	customer.customer_code,
	customer.customer_name,
	order_header.order_date,
	order_header.status,
	order_header.sales_rep;
""";

static string GetFunctionScript() => """
CREATE OR REPLACE FUNCTION public.fn_customer_lifetime_value(customer_code_param VARCHAR(20))
RETURNS NUMERIC(18,2)
LANGUAGE SQL
AS $$
	SELECT COALESCE(SUM(order_line.quantity * order_line.unit_price), 0)
	FROM public.sales_orders AS order_header
	INNER JOIN public.customers AS customer ON customer.customer_id = order_header.customer_id
	INNER JOIN public.sales_order_lines AS order_line ON order_line.sales_order_id = order_header.sales_order_id
	WHERE customer.customer_code = customer_code_param;
$$;
""";

static string GetProcedureScript() => """
CREATE OR REPLACE PROCEDURE public.usp_refresh_sales_summary(days_back INTEGER DEFAULT 90)
LANGUAGE plpgsql
AS $$
BEGIN
	PERFORM COUNT(*)
	FROM public.sales_orders
	WHERE order_date >= CURRENT_DATE - make_interval(days => days_back);
END;
$$;
""";
