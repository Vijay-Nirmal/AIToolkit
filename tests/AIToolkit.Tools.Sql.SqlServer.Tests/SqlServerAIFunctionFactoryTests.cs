using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Data.Common;
using System.Text.Json;

namespace AIToolkit.Tools.Sql.SqlServer.Tests;

[TestClass]
public class SqlServerAIFunctionFactoryTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly string[] ExpectedToolNames =
    [
        "mssql_explain_query",
        "mssql_get_object_definition",
        "mssql_list_databases",
        "mssql_list_functions",
        "mssql_list_procedures",
        "mssql_list_schemas",
        "mssql_list_servers",
        "mssql_list_tables",
        "mssql_list_views",
        "mssql_run_query",
    ];

    [TestMethod]
    public void CreateAllUsesReferenceAlignedToolNames()
    {
        var functions = CreateFunctions();

        CollectionAssert.AreEquivalent(
            ExpectedToolNames,
            functions.Select(static function => function.Name).ToArray());
    }

    [TestMethod]
    public async Task ListServersFunctionInvokesToolService()
    {
        var listServers = CreateFunctions().Single(static function => function.Name == "mssql_list_servers");

        var invocationResult = await listServers.InvokeAsync();
        var result = invocationResult switch
        {
            JsonElement json => json.Deserialize<SqlServerListServersToolResult>(JsonOptions),
            SqlServerListServersToolResult typed => typed,
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
        var listServers = CreateFunctions().Single(static function => function.Name == "mssql_list_servers");
        var loggerFactory = new TestLoggerFactory();
        var arguments = new AIFunctionArguments
        {
            Services = new TestServiceProvider(loggerFactory),
        };

        _ = await listServers.InvokeAsync(arguments);

        Assert.HasCount(1, loggerFactory.Entries);
        StringAssert.Contains(loggerFactory.Entries[0], "mssql_list_servers");
        StringAssert.Contains(loggerFactory.Entries[0], "{}");
    }

    private static IReadOnlyList<AIFunction> CreateFunctions()
    {
        var service = new SqlServerToolService(
            new FakeProfileCatalog(),
            new FakeMetadataProvider(),
            new FakeQueryExecutor(),
            new FakeConnectionOpener(),
            new SqlServerQueryClassifier(),
            SqlExecutionPolicy.ReadOnly);

        return new SqlServerAIFunctionFactory(service).CreateAll();
    }

    private sealed class FakeProfileCatalog : ISqlConnectionProfileCatalog
    {
        public ValueTask<IReadOnlyList<SqlConnectionProfileSummary>> ListProfilesAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<SqlConnectionProfileSummary>>(
                [new SqlConnectionProfileSummary("local", "localhost", "master")]);
    }

    private sealed class FakeMetadataProvider : ISqlMetadataProvider
    {
        public ValueTask<IReadOnlyList<SqlDatabaseInfo>> ListDatabasesAsync(SqlConnectionTarget target, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<SqlDatabaseInfo>>([new SqlDatabaseInfo("master")]);

        public ValueTask<IReadOnlyList<SqlSchemaInfo>> ListSchemasAsync(SqlConnectionTarget target, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<SqlSchemaInfo>>([new SqlSchemaInfo("dbo")]);

        public ValueTask<IReadOnlyList<SqlTableInfo>> ListTablesAsync(SqlConnectionTarget target, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<SqlTableInfo>>([new SqlTableInfo(new SqlObjectIdentifier("dbo", "Customers"))]);

        public ValueTask<IReadOnlyList<SqlViewInfo>> ListViewsAsync(SqlConnectionTarget target, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<SqlViewInfo>>([new SqlViewInfo(new SqlObjectIdentifier("dbo", "ActiveCustomers"))]);

        public ValueTask<IReadOnlyList<SqlRoutineInfo>> ListFunctionsAsync(SqlConnectionTarget target, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<SqlRoutineInfo>>([new SqlRoutineInfo(new SqlObjectIdentifier("dbo", "fn_count"), SqlRoutineKind.Function)]);

        public ValueTask<IReadOnlyList<SqlRoutineInfo>> ListProceduresAsync(SqlConnectionTarget target, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<SqlRoutineInfo>>([new SqlRoutineInfo(new SqlObjectIdentifier("dbo", "usp_seed"), SqlRoutineKind.Procedure)]);

        public ValueTask<SqlObjectDefinition> GetObjectDefinitionAsync(
            SqlConnectionTarget target,
            string? schemaName,
            string objectName,
            SqlObjectKind objectKind,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new SqlObjectDefinition(new SqlObjectIdentifier(schemaName ?? "dbo", objectName), objectKind, "SELECT 1"));

        public ValueTask<SqlSchemaOverview> GetSchemaOverviewAsync(SqlConnectionTarget target, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new SqlSchemaOverview(Array.Empty<SqlTableInfo>(), Array.Empty<SqlViewInfo>(), Array.Empty<SqlRoutineInfo>(), Array.Empty<SqlRoutineInfo>()));
    }

    private sealed class FakeQueryExecutor : ISqlQueryExecutor
    {
        public ValueTask<SqlExecuteQueryResult> ExecuteAsync(
            SqlConnectionTarget target,
            string query,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(
                new SqlExecuteQueryResult(
                    true,
                    new SqlQueryClassification(["SELECT"], SqlStatementSafety.ReadOnly),
                    [
                        new SqlResultSet(
                            [new SqlResultColumn("Value", "int", 0, false)],
                            [new Dictionary<string, object?> { ["Value"] = 1 }])
                    ]));
    }

    private sealed class FakeConnectionOpener : ISqlConnectionOpener
    {
        public ValueTask<DbConnection> OpenConnectionAsync(SqlConnectionTarget target, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class TestServiceProvider(ILoggerFactory loggerFactory) : IServiceProvider
    {
        private readonly ILoggerFactory _loggerFactory = loggerFactory;

        public object? GetService(Type serviceType) =>
            serviceType == typeof(ILoggerFactory) ? _loggerFactory : null;
    }

    private sealed class TestLoggerFactory : ILoggerFactory
    {
        public List<string> Entries { get; } = [];

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName) => new TestLogger(Entries);

        public void Dispose()
        {
        }
    }

    private sealed class TestLogger(List<string> entries) : ILogger
    {
        private readonly List<string> _entries = entries;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _entries.Add(formatter(state, exception));
        }
    }
}