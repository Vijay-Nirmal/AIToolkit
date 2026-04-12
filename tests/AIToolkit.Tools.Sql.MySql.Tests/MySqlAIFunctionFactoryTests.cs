using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Data.Common;
using System.Text.Json;

namespace AIToolkit.Tools.Sql.MySql.Tests;

[TestClass]
public class MySqlAIFunctionFactoryTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly string[] ExpectedToolNames =
    [
        "mysql_explain_query",
        "mysql_get_object_definition",
        "mysql_list_databases",
        "mysql_list_functions",
        "mysql_list_procedures",
        "mysql_list_schemas",
        "mysql_list_servers",
        "mysql_list_tables",
        "mysql_list_views",
        "mysql_run_query",
    ];

    [TestMethod]
    public void CreateAllUsesExpectedToolNames()
    {
        var functions = CreateFunctions();
        CollectionAssert.AreEquivalent(ExpectedToolNames, functions.Select(static function => function.Name).ToArray());
    }

    [TestMethod]
    public async Task ListServersFunctionInvokesToolService()
    {
        var listServers = CreateFunctions().Single(static function => function.Name == "mysql_list_servers");
        var invocationResult = await listServers.InvokeAsync();
        var result = invocationResult switch
        {
            JsonElement json => json.Deserialize<MySqlListServersToolResult>(JsonOptions),
            MySqlListServersToolResult typed => typed,
            _ => null,
        };

        Assert.IsNotNull(result);
        Assert.IsTrue(result.Success);
        Assert.HasCount(1, result.Servers);
        Assert.AreEqual("local", result.Servers[0].ConnectionName);
    }

    [TestMethod]
    public async Task ListServersFunctionLogsWhenLoggerFactoryIsAvailable()
    {
        var listServers = CreateFunctions().Single(static function => function.Name == "mysql_list_servers");
        var loggerFactory = new TestLoggerFactory();
        var arguments = new AIFunctionArguments { Services = new TestServiceProvider(loggerFactory) };

        _ = await listServers.InvokeAsync(arguments);

        Assert.HasCount(1, loggerFactory.Entries);
        StringAssert.Contains(loggerFactory.Entries[0], "mysql_list_servers");
        StringAssert.Contains(loggerFactory.Entries[0], "{}");
    }

    private static IReadOnlyList<AIFunction> CreateFunctions()
    {
        var service = new MySqlToolService(new FakeProfileCatalog(), new FakeMetadataProvider(), new FakeQueryExecutor(), new FakeConnectionOpener(), new MySqlQueryClassifier(), SqlExecutionPolicy.ReadOnly);
        return new MySqlAIFunctionFactory(service).CreateAll();
    }

    private sealed class FakeProfileCatalog : ISqlConnectionProfileCatalog
    {
        public ValueTask<IReadOnlyList<SqlConnectionProfileSummary>> ListProfilesAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<SqlConnectionProfileSummary>>([new SqlConnectionProfileSummary("local", "localhost:3306", "mysql")]);
    }

    private sealed class FakeMetadataProvider : ISqlMetadataProvider
    {
        public ValueTask<IReadOnlyList<SqlDatabaseInfo>> ListDatabasesAsync(SqlConnectionTarget target, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<SqlDatabaseInfo>>([new SqlDatabaseInfo("mysql")]);

        public ValueTask<IReadOnlyList<SqlSchemaInfo>> ListSchemasAsync(SqlConnectionTarget target, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<SqlSchemaInfo>>([new SqlSchemaInfo("mysql")]);

        public ValueTask<IReadOnlyList<SqlTableInfo>> ListTablesAsync(SqlConnectionTarget target, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<SqlTableInfo>>([new SqlTableInfo(new SqlObjectIdentifier("mysql", "customers"))]);

        public ValueTask<IReadOnlyList<SqlViewInfo>> ListViewsAsync(SqlConnectionTarget target, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<SqlViewInfo>>([new SqlViewInfo(new SqlObjectIdentifier("mysql", "active_customers"))]);

        public ValueTask<IReadOnlyList<SqlRoutineInfo>> ListFunctionsAsync(SqlConnectionTarget target, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<SqlRoutineInfo>>([new SqlRoutineInfo(new SqlObjectIdentifier("mysql", "fn_count"), SqlRoutineKind.Function)]);

        public ValueTask<IReadOnlyList<SqlRoutineInfo>> ListProceduresAsync(SqlConnectionTarget target, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<SqlRoutineInfo>>([new SqlRoutineInfo(new SqlObjectIdentifier("mysql", "usp_seed"), SqlRoutineKind.Procedure)]);

        public ValueTask<SqlObjectDefinition> GetObjectDefinitionAsync(SqlConnectionTarget target, string? schemaName, string objectName, SqlObjectKind objectKind, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new SqlObjectDefinition(new SqlObjectIdentifier(schemaName ?? "mysql", objectName), objectKind, "SELECT 1"));

        public ValueTask<SqlSchemaOverview> GetSchemaOverviewAsync(SqlConnectionTarget target, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new SqlSchemaOverview(Array.Empty<SqlTableInfo>(), Array.Empty<SqlViewInfo>(), Array.Empty<SqlRoutineInfo>(), Array.Empty<SqlRoutineInfo>()));
    }

    private sealed class FakeQueryExecutor : ISqlQueryExecutor
    {
        public ValueTask<SqlExecuteQueryResult> ExecuteAsync(SqlConnectionTarget target, string query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new SqlExecuteQueryResult(true, new SqlQueryClassification(["SELECT"], SqlStatementSafety.ReadOnly), [new SqlResultSet([new SqlResultColumn("value", "int", 0, false)], [new Dictionary<string, object?> { ["value"] = 1 }])]));
    }

    private sealed class FakeConnectionOpener : ISqlConnectionOpener
    {
        public ValueTask<DbConnection> OpenConnectionAsync(SqlConnectionTarget target, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class TestServiceProvider(ILoggerFactory loggerFactory) : IServiceProvider
    {
        private readonly ILoggerFactory _loggerFactory = loggerFactory;
        public object? GetService(Type serviceType) => serviceType == typeof(ILoggerFactory) ? _loggerFactory : null;
    }

    private sealed class TestLoggerFactory : ILoggerFactory
    {
        public List<string> Entries { get; } = [];
        public void AddProvider(ILoggerProvider provider) { }
        public ILogger CreateLogger(string categoryName) => new TestLogger(Entries);
        public void Dispose() { }
    }

    private sealed class TestLogger(List<string> entries) : ILogger
    {
        private readonly List<string> _entries = entries;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => _entries.Add(formatter(state, exception));
    }
}