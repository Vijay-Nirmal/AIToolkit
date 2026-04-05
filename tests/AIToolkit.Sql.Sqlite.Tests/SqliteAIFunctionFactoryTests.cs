using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Data.Common;
using System.Text.Json;

namespace AIToolkit.Sql.Sqlite.Tests;

[TestClass]
public class SqliteAIFunctionFactoryTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly string[] ExpectedToolNames =
    [
        "sqlite_explain_query",
        "sqlite_get_object_definition",
        "sqlite_list_databases",
        "sqlite_list_functions",
        "sqlite_list_procedures",
        "sqlite_list_schemas",
        "sqlite_list_servers",
        "sqlite_list_tables",
        "sqlite_list_views",
        "sqlite_run_query",
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
        var listServers = CreateFunctions().Single(static function => function.Name == "sqlite_list_servers");
        var invocationResult = await listServers.InvokeAsync();
        var result = invocationResult switch
        {
            JsonElement json => json.Deserialize<SqliteListServersToolResult>(JsonOptions),
            SqliteListServersToolResult typed => typed,
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
        var listServers = CreateFunctions().Single(static function => function.Name == "sqlite_list_servers");
        var loggerFactory = new TestLoggerFactory();
        var arguments = new AIFunctionArguments { Services = new TestServiceProvider(loggerFactory) };

        _ = await listServers.InvokeAsync(arguments);

        Assert.HasCount(1, loggerFactory.Entries);
        StringAssert.Contains(loggerFactory.Entries[0], "sqlite_list_servers");
        StringAssert.Contains(loggerFactory.Entries[0], "{}");
    }

    private static IReadOnlyList<AIFunction> CreateFunctions()
    {
        var service = new SqliteToolService(new FakeProfileCatalog(), new FakeMetadataProvider(), new FakeQueryExecutor(), new FakeConnectionOpener(), new SqliteQueryClassifier(), SqlExecutionPolicy.ReadOnly);
        return new SqliteAIFunctionFactory(service).CreateAll();
    }

    private sealed class FakeProfileCatalog : ISqlConnectionProfileCatalog
    {
        public ValueTask<IReadOnlyList<SqlConnectionProfileSummary>> ListProfilesAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<SqlConnectionProfileSummary>>([new SqlConnectionProfileSummary("local", "app.db", "main")]);
    }

    private sealed class FakeMetadataProvider : ISqlMetadataProvider
    {
        public ValueTask<IReadOnlyList<SqlDatabaseInfo>> ListDatabasesAsync(SqlConnectionTarget target, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<SqlDatabaseInfo>>([new SqlDatabaseInfo("main")]);

        public ValueTask<IReadOnlyList<SqlSchemaInfo>> ListSchemasAsync(SqlConnectionTarget target, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<SqlSchemaInfo>>([new SqlSchemaInfo("main")]);

        public ValueTask<IReadOnlyList<SqlTableInfo>> ListTablesAsync(SqlConnectionTarget target, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<SqlTableInfo>>([new SqlTableInfo(new SqlObjectIdentifier("main", "customers"))]);

        public ValueTask<IReadOnlyList<SqlViewInfo>> ListViewsAsync(SqlConnectionTarget target, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<SqlViewInfo>>([new SqlViewInfo(new SqlObjectIdentifier("main", "active_customers"))]);

        public ValueTask<IReadOnlyList<SqlRoutineInfo>> ListFunctionsAsync(SqlConnectionTarget target, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<SqlRoutineInfo>>(Array.Empty<SqlRoutineInfo>());

        public ValueTask<IReadOnlyList<SqlRoutineInfo>> ListProceduresAsync(SqlConnectionTarget target, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<SqlRoutineInfo>>(Array.Empty<SqlRoutineInfo>());

        public ValueTask<SqlObjectDefinition> GetObjectDefinitionAsync(SqlConnectionTarget target, string? schemaName, string objectName, SqlObjectKind objectKind, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new SqlObjectDefinition(new SqlObjectIdentifier(schemaName ?? "main", objectName), objectKind, "CREATE TABLE customers (id INTEGER)"));

        public ValueTask<SqlSchemaOverview> GetSchemaOverviewAsync(SqlConnectionTarget target, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new SqlSchemaOverview(Array.Empty<SqlTableInfo>(), Array.Empty<SqlViewInfo>(), Array.Empty<SqlRoutineInfo>(), Array.Empty<SqlRoutineInfo>()));
    }

    private sealed class FakeQueryExecutor : ISqlQueryExecutor
    {
        public ValueTask<SqlExecuteQueryResult> ExecuteAsync(SqlConnectionTarget target, string query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new SqlExecuteQueryResult(true, new SqlQueryClassification(["SELECT"], SqlStatementSafety.ReadOnly), [new SqlResultSet([new SqlResultColumn("value", "INTEGER", 0, false)], [new Dictionary<string, object?> { ["value"] = 1 }])]));
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