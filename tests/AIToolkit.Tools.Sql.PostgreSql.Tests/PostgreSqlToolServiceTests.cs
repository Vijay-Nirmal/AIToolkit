using System.Collections;
using System.Data;
using System.Data.Common;

namespace AIToolkit.Tools.Sql.PostgreSql.Tests;

[TestClass]
public class PostgreSqlToolServiceTests
{
    [TestMethod]
    public async Task ListExtensionsAsyncReturnsInstalledExtensions()
    {
        var opener = new FakeConnectionOpener(
            static commandText =>
            {
                Assert.AreEqual(PostgreSqlCatalogQueries.ListExtensions, commandText);
                return CreateReader(
                    [("extname", typeof(string)), ("extversion", typeof(string)), ("nspname", typeof(string)), ("description", typeof(string))],
                    ["pgcrypto", "1.3", "public", "cryptographic functions"]);
            });

        var service = CreateToolService(opener, new PostgreSqlQueryClassifier());

        var result = await service.ListExtensionsAsync("local", "postgres");

        Assert.IsTrue(result.Success);
        Assert.AreEqual(1, opener.OpenCallCount);
        Assert.HasCount(1, result.Extensions);
        Assert.AreEqual("pgcrypto", result.Extensions[0].Name);
        Assert.AreEqual("1.3", result.Extensions[0].Version);
        Assert.AreEqual("public", result.Extensions[0].Schema);
    }

    [TestMethod]
    public async Task ExplainQueryAsyncReturnsPlanAndStatisticsForReadOnlyQuery()
    {
        const string planJson = """
[
  {
    "Plan": {
      "Node Type": "Seq Scan",
      "Relation Name": "orders",
      "Schema": "public",
      "Alias": "orders",
      "Startup Cost": 0.00,
      "Total Cost": 18.10,
      "Plan Rows": 810,
      "Plan Width": 24,
      "Actual Startup Time": 0.012,
      "Actual Total Time": 0.124,
      "Actual Rows": 42,
      "Actual Loops": 1,
      "Shared Hit Blocks": 3,
      "Shared Read Blocks": 0,
      "Shared Dirtied Blocks": 0,
      "Shared Written Blocks": 0,
      "Local Hit Blocks": 0,
      "Local Read Blocks": 0,
      "Local Dirtied Blocks": 0,
      "Local Written Blocks": 0,
      "Temp Read Blocks": 0,
      "Temp Written Blocks": 0,
      "WAL Records": 0,
      "WAL FPI": 0,
      "WAL Bytes": 0,
      "Filter": "(is_active = true)"
    },
    "Planning Time": 0.321,
    "Execution Time": 0.456
  }
]
""";

        var opener = new FakeConnectionOpener(
            static commandText =>
            {
                StringAssert.StartsWith(commandText, "EXPLAIN (ANALYZE TRUE, VERBOSE TRUE, BUFFERS TRUE, SETTINGS TRUE, WAL TRUE, FORMAT JSON)");
                return CreateReader([("QUERY PLAN", typeof(string))], [planJson]);
            });

        var service = CreateToolService(opener, new PostgreSqlQueryClassifier());

        var result = await service.ExplainQueryAsync("local", "select * from public.orders where is_active = true", "postgres");

        Assert.IsTrue(result.Success);
        Assert.IsNotNull(result.Result);
        Assert.AreEqual(1, opener.OpenCallCount);
        Assert.AreEqual("json", result.Result.Format);
        Assert.AreEqual(SqlStatementSafety.ReadOnly, result.Result.Classification.Safety);
        Assert.IsNotNull(result.Result.RootNode);
        Assert.AreEqual("Seq Scan", result.Result.RootNode.NodeType);
        Assert.AreEqual("orders", result.Result.RootNode.RelationName);
        Assert.AreEqual(18.10m, result.Result.RootNode.TotalCost);
        Assert.AreEqual(0.321d, result.Result.RootNode.Timing.PlanningTimeMs);
        Assert.AreEqual(0.456d, result.Result.RootNode.Timing.ExecutionTimeMs);
        Assert.AreEqual(3L, result.Result.RootNode.Buffers.SharedHitBlocks);
    }

    [TestMethod]
    public async Task ExplainQueryAsyncRejectsMutationQueries()
    {
        var opener = new FakeConnectionOpener(_ => throw new AssertFailedException("OpenConnectionAsync should not be called for rejected explain requests."));
        var service = CreateToolService(
            opener,
            new StubQueryClassifier(new SqlQueryClassification(["UPDATE"], SqlStatementSafety.ApprovalRequired, "The query can modify database state.")));

        var result = await service.ExplainQueryAsync("local", "update public.orders set is_active = false");

        Assert.IsFalse(result.Success);
        Assert.AreEqual("The query can modify database state.", result.Message);
        Assert.AreEqual(0, opener.OpenCallCount);
    }

    [TestMethod]
    public async Task ExplainQueryAsyncRejectsExplainStatements()
    {
        var opener = new FakeConnectionOpener(_ => throw new AssertFailedException("OpenConnectionAsync should not be called for nested EXPLAIN requests."));
        var service = CreateToolService(opener, new StubQueryClassifier(new SqlQueryClassification(["EXPLAIN"], SqlStatementSafety.ReadOnly)));

        var result = await service.ExplainQueryAsync("local", "EXPLAIN SELECT * FROM public.orders");

        Assert.IsFalse(result.Success);
        Assert.AreEqual("Provide the underlying PostgreSQL query instead of an EXPLAIN statement.", result.Message);
        Assert.AreEqual(0, opener.OpenCallCount);
    }

    [TestMethod]
    public async Task ExplainQueryAsyncReturnsFailureWhenPlanIsMissing()
    {
        var opener = new FakeConnectionOpener(static _ => CreateReader([("QUERY PLAN", typeof(string))]));
        var service = CreateToolService(opener, new PostgreSqlQueryClassifier());

        var result = await service.ExplainQueryAsync("local", "select * from public.orders", "postgres");

        Assert.IsFalse(result.Success);
        Assert.AreEqual("PostgreSQL did not return an execution plan.", result.Message);
        Assert.AreEqual(1, opener.OpenCallCount);
    }

    [TestMethod]
    public async Task ExplainQueryAsyncReturnsFailureForUnexpectedPlanPayload()
    {
        const string invalidPlanJson = "{}";

        var opener = new FakeConnectionOpener(static _ => CreateReader([("QUERY PLAN", typeof(string))], [invalidPlanJson]));
        var service = CreateToolService(opener, new PostgreSqlQueryClassifier());

        var result = await service.ExplainQueryAsync("local", "select * from public.orders", "postgres");

        Assert.IsFalse(result.Success);
        Assert.AreEqual("PostgreSQL returned an execution plan in an unexpected format.", result.Message);
        Assert.AreEqual(1, opener.OpenCallCount);
    }

    [TestMethod]
    public async Task ListExtensionsAsyncReturnsFailureWhenReaderThrows()
    {
        var opener = new FakeConnectionOpener(_ => throw new InvalidOperationException("catalog unavailable"));
        var service = CreateToolService(opener, new PostgreSqlQueryClassifier());

        var result = await service.ListExtensionsAsync("local", "postgres");

        Assert.IsFalse(result.Success);
        Assert.AreEqual("catalog unavailable", result.Message);
        Assert.AreEqual(1, opener.OpenCallCount);
    }

    private static PostgreSqlToolService CreateToolService(
        FakeConnectionOpener connectionOpener,
        ISqlQueryClassifier queryClassifier) =>
        new(
            new FakeProfileCatalog(),
            new FakeMetadataProvider(),
            new FakeQueryExecutor(),
            connectionOpener,
            queryClassifier,
            SqlExecutionPolicy.ReadOnly);

    private static DataTableReader CreateReader(
        (string Name, Type Type)[] columns,
        params object?[][] rows)
    {
        var table = new DataTable();
        foreach (var (name, type) in columns)
        {
            table.Columns.Add(name, type);
        }

        foreach (var row in rows)
        {
            table.Rows.Add(row);
        }

        return table.CreateDataReader();
    }

    private sealed class FakeProfileCatalog : ISqlConnectionProfileCatalog
    {
        public ValueTask<IReadOnlyList<SqlConnectionProfileSummary>> ListProfilesAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<SqlConnectionProfileSummary>>([new SqlConnectionProfileSummary("local", "localhost:5432", "postgres")]);
    }

    private sealed class FakeMetadataProvider : ISqlMetadataProvider
    {
        public ValueTask<IReadOnlyList<SqlDatabaseInfo>> ListDatabasesAsync(SqlConnectionTarget target, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<SqlDatabaseInfo>>(Array.Empty<SqlDatabaseInfo>());

        public ValueTask<IReadOnlyList<SqlSchemaInfo>> ListSchemasAsync(SqlConnectionTarget target, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<SqlSchemaInfo>>(Array.Empty<SqlSchemaInfo>());

        public ValueTask<IReadOnlyList<SqlTableInfo>> ListTablesAsync(SqlConnectionTarget target, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<SqlTableInfo>>(Array.Empty<SqlTableInfo>());

        public ValueTask<IReadOnlyList<SqlViewInfo>> ListViewsAsync(SqlConnectionTarget target, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<SqlViewInfo>>(Array.Empty<SqlViewInfo>());

        public ValueTask<IReadOnlyList<SqlRoutineInfo>> ListFunctionsAsync(SqlConnectionTarget target, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<SqlRoutineInfo>>(Array.Empty<SqlRoutineInfo>());

        public ValueTask<IReadOnlyList<SqlRoutineInfo>> ListProceduresAsync(SqlConnectionTarget target, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<SqlRoutineInfo>>(Array.Empty<SqlRoutineInfo>());

        public ValueTask<SqlObjectDefinition> GetObjectDefinitionAsync(SqlConnectionTarget target, string? schemaName, string objectName, SqlObjectKind objectKind, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<SqlSchemaOverview> GetSchemaOverviewAsync(SqlConnectionTarget target, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new SqlSchemaOverview(Array.Empty<SqlTableInfo>(), Array.Empty<SqlViewInfo>(), Array.Empty<SqlRoutineInfo>(), Array.Empty<SqlRoutineInfo>()));
    }

    private sealed class FakeQueryExecutor : ISqlQueryExecutor
    {
        public ValueTask<SqlExecuteQueryResult> ExecuteAsync(SqlConnectionTarget target, string query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new SqlExecuteQueryResult(true, new SqlQueryClassification(["SELECT"], SqlStatementSafety.ReadOnly), Array.Empty<SqlResultSet>()));
    }

    private sealed class StubQueryClassifier(SqlQueryClassification classification) : ISqlQueryClassifier
    {
        private readonly SqlQueryClassification _classification = classification;

        public SqlQueryClassification Classify(string query) => _classification;
    }

    private sealed class FakeConnectionOpener(Func<string, DataTableReader> readerFactory) : ISqlConnectionOpener
    {
        private readonly Func<string, DataTableReader> _readerFactory = readerFactory;

        public int OpenCallCount { get; private set; }

        public ValueTask<DbConnection> OpenConnectionAsync(SqlConnectionTarget target, CancellationToken cancellationToken = default)
        {
            OpenCallCount++;
            return ValueTask.FromResult<DbConnection>(new FakeDbConnection(_readerFactory));
        }
    }

#pragma warning disable CS8764
#pragma warning disable CS8765

    private sealed class FakeDbConnection(Func<string, DataTableReader> readerFactory) : DbConnection
    {
        private readonly Func<string, DataTableReader> _readerFactory = readerFactory;
        private ConnectionState _state = ConnectionState.Closed;

        public override string? ConnectionString { get; set; } = string.Empty;

        public override string Database => "postgres";

        public override string DataSource => "localhost:5432";

        public override string ServerVersion => "16.0";

        public override ConnectionState State => _state;

        public override void ChangeDatabase(string databaseName)
        {
        }

        public override void Close() => _state = ConnectionState.Closed;

        public override void Open() => _state = ConnectionState.Open;

        public override Task OpenAsync(CancellationToken cancellationToken)
        {
            _state = ConnectionState.Open;
            return Task.CompletedTask;
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();

        protected override DbCommand CreateDbCommand() => new FakeDbCommand(this, _readerFactory);
    }

    private sealed class FakeDbCommand(FakeDbConnection connection, Func<string, DataTableReader> readerFactory) : DbCommand
    {
        private readonly Func<string, DataTableReader> _readerFactory = readerFactory;
        private readonly FakeDbParameterCollection _parameters = new();
        private DbConnection _connection = connection;

        public override string? CommandText { get; set; } = string.Empty;

        public override int CommandTimeout { get; set; }

        public override CommandType CommandType { get; set; } = CommandType.Text;

        public override bool DesignTimeVisible { get; set; }

        public override UpdateRowSource UpdatedRowSource { get; set; }

        protected override DbConnection? DbConnection
        {
            get => _connection;
            set => _connection = value ?? throw new ArgumentNullException(nameof(value));
        }

        protected override DbParameterCollection DbParameterCollection => _parameters;

        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel()
        {
        }

        public override int ExecuteNonQuery() => throw new NotSupportedException();

        public override object? ExecuteScalar() => throw new NotSupportedException();

        public override void Prepare()
        {
        }

        protected override DbParameter CreateDbParameter() => new FakeDbParameter();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => _readerFactory(CommandText ?? string.Empty);
    }

    private sealed class FakeDbParameter : DbParameter
    {
        public override DbType DbType { get; set; }

        public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;

        public override bool IsNullable { get; set; }

        public override string ParameterName { get; set; } = string.Empty;

        public override string? SourceColumn { get; set; } = string.Empty;

        public override object? Value { get; set; }

        public override bool SourceColumnNullMapping { get; set; }

        public override int Size { get; set; }

        public override void ResetDbType()
        {
        }
    }

    private sealed class FakeDbParameterCollection : DbParameterCollection
    {
        private readonly List<DbParameter> _parameters = [];

        public override int Count => _parameters.Count;

        public override object SyncRoot => ((ICollection)_parameters).SyncRoot;

        public override int Add(object? value)
        {
            _parameters.Add((DbParameter)(value ?? throw new ArgumentNullException(nameof(value))));
            return _parameters.Count - 1;
        }

        public override void AddRange(Array values)
        {
            foreach (var value in values)
            {
                Add(value!);
            }
        }

        public override void Clear() => _parameters.Clear();

        public override bool Contains(object? value) => value is DbParameter parameter && _parameters.Contains(parameter);

        public override bool Contains(string value) => _parameters.Any(parameter => parameter.ParameterName == value);

        public override void CopyTo(Array array, int index) => ((ICollection)_parameters).CopyTo(array, index);

        public override IEnumerator GetEnumerator() => _parameters.GetEnumerator();

        public override int IndexOf(object? value) => value is DbParameter parameter ? _parameters.IndexOf(parameter) : -1;

        public override int IndexOf(string parameterName) => _parameters.FindIndex(parameter => parameter.ParameterName == parameterName);

        public override void Insert(int index, object? value) => _parameters.Insert(index, (DbParameter)(value ?? throw new ArgumentNullException(nameof(value))));

        public override void Remove(object? value)
        {
            if (value is DbParameter parameter)
            {
                _parameters.Remove(parameter);
            }
        }

        public override void RemoveAt(int index) => _parameters.RemoveAt(index);

        public override void RemoveAt(string parameterName)
        {
            var index = IndexOf(parameterName);
            if (index >= 0)
            {
                _parameters.RemoveAt(index);
            }
        }

        protected override DbParameter GetParameter(int index) => _parameters[index];

        protected override DbParameter GetParameter(string parameterName) => _parameters[IndexOf(parameterName)];

        protected override void SetParameter(int index, DbParameter value) => _parameters[index] = value;

        protected override void SetParameter(string parameterName, DbParameter value)
        {
            var index = IndexOf(parameterName);
            if (index >= 0)
            {
                _parameters[index] = value;
            }
            else
            {
                _parameters.Add(value);
            }
        }
    }

#pragma warning restore CS8765
#pragma warning restore CS8764
}