using System.Collections;
using System.Data;
using System.Data.Common;

namespace AIToolkit.Tools.Sql.SqlServer.Tests;

[TestClass]
public class SqlServerToolServiceTests
{
    [TestMethod]
    public async Task ExplainQueryAsyncReturnsActualExecutionPlanSummary()
    {
        const string planXml = """
<ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan">
  <BatchSequence>
    <Batch>
      <Statements>
        <StmtSimple StatementText="SELECT * FROM dbo.Orders" StatementSubTreeCost="0.0032831" StatementEstRows="1">
          <QueryPlan DegreeOfParallelism="1" CompileTime="3" CompileCPU="1">
            <RelOp NodeId="0" PhysicalOp="Clustered Index Seek" LogicalOp="Clustered Index Seek" EstimateRows="1" AvgRowSize="11" EstimatedTotalSubtreeCost="0.0032831">
              <IndexScan>
                <Object Schema="[dbo]" Table="[Orders]" Index="[PK_Orders]" />
              </IndexScan>
              <RunTimeInformation>
                <RunTimeCountersPerThread Thread="0" ActualRows="1" ActualExecutions="1" ActualElapsedms="0.5" />
              </RunTimeInformation>
            </RelOp>
          </QueryPlan>
          <QueryTimeStats CpuTime="2" ElapsedTime="4" />
        </StmtSimple>
      </Statements>
    </Batch>
  </BatchSequence>
</ShowPlanXML>
""";

        var opener = new FakeConnectionOpener(
            static commandText =>
            {
                StringAssert.Contains(commandText, "SET STATISTICS XML ON");
                StringAssert.Contains(commandText, "SELECT * FROM dbo.Orders");
                return CreateMultiResultReader(
                    CreateTable([("OrderId", typeof(int))], [1]),
                    CreateTable([("Microsoft SQL Server 2005 XML Showplan", typeof(string))], [planXml]));
            });

        var service = CreateToolService(opener, new SqlServerQueryClassifier());

        var result = await service.ExplainQueryAsync("local", "SELECT * FROM dbo.Orders", "master");

        Assert.IsTrue(result.Success);
        Assert.IsNotNull(result.Result);
        Assert.AreEqual(1, opener.OpenCallCount);
        Assert.AreEqual("xml", result.Result.Format);
        Assert.AreEqual("Clustered Index Seek", result.Result.RootNode?.NodeType);
        Assert.AreEqual("Orders", result.Result.RootNode?.RelationName);
        Assert.AreEqual("PK_Orders", result.Result.RootNode?.IndexName);
        Assert.AreEqual(4d, result.Result.RootNode?.Timing.ExecutionTimeMs);
        Assert.AreEqual(2d, result.Result.RootNode?.Timing.ExecutionCpuTimeMs);
    }

    [TestMethod]
    public async Task ExplainQueryAsyncRejectsMutationQueries()
    {
        var opener = new FakeConnectionOpener(_ => throw new AssertFailedException("OpenConnectionAsync should not be called for rejected explain requests."));
        var service = CreateToolService(opener, new SqlServerQueryClassifier());

        var result = await service.ExplainQueryAsync("local", "UPDATE dbo.Orders SET IsActive = 0");

        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, opener.OpenCallCount);
    }

    [TestMethod]
    public async Task ExplainQueryAsyncRejectsShowplanStatements()
    {
        var opener = new FakeConnectionOpener(_ => throw new AssertFailedException("OpenConnectionAsync should not be called for nested SHOWPLAN requests."));
        var service = CreateToolService(opener, new StubQueryClassifier(new SqlQueryClassification(["SELECT"], SqlStatementSafety.ReadOnly)));

        var result = await service.ExplainQueryAsync("local", "SET STATISTICS XML ON; SELECT * FROM dbo.Orders;");

        Assert.IsFalse(result.Success);
        Assert.AreEqual("Provide the underlying SQL query instead of a SHOWPLAN or STATISTICS XML statement.", result.Message);
        Assert.AreEqual(0, opener.OpenCallCount);
    }

    [TestMethod]
    public async Task ExplainQueryAsyncReturnsFailureWhenPlanIsMissing()
    {
        var opener = new FakeConnectionOpener(static _ => CreateMultiResultReader(CreateTable([("OrderId", typeof(int))], [1])));
        var service = CreateToolService(opener, new SqlServerQueryClassifier());

        var result = await service.ExplainQueryAsync("local", "SELECT * FROM dbo.Orders", "master");

        Assert.IsFalse(result.Success);
        Assert.AreEqual("SQL Server did not return a STATISTICS XML plan. Ensure the login has SHOWPLAN permission.", result.Message);
        Assert.AreEqual(1, opener.OpenCallCount);
    }

    [TestMethod]
    public async Task ExplainQueryAsyncReturnsFailureForMalformedPlanXml()
    {
        const string invalidPlanXml = "<ShowPlanXML xmlns=\"http://schemas.microsoft.com/sqlserver/2004/07/showplan\">";

        var opener = new FakeConnectionOpener(static _ => CreateMultiResultReader(CreateTable([("Microsoft SQL Server 2005 XML Showplan", typeof(string))], [invalidPlanXml])));
        var service = CreateToolService(opener, new SqlServerQueryClassifier());

        var result = await service.ExplainQueryAsync("local", "SELECT * FROM dbo.Orders", "master");

        Assert.IsFalse(result.Success);
        Assert.IsNotNull(result.Message);
        Assert.AreEqual(1, opener.OpenCallCount);
    }

    private static SqlServerToolService CreateToolService(FakeConnectionOpener connectionOpener, ISqlQueryClassifier classifier) =>
        new(
            new FakeProfileCatalog(),
            new FakeMetadataProvider(),
            new FakeQueryExecutor(),
            connectionOpener,
            classifier,
            SqlExecutionPolicy.ReadOnly);

    private static DataTable CreateTable((string Name, Type Type)[] columns, params object?[][] rows)
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

        return table;
    }

    private static DataTableReader CreateMultiResultReader(params DataTable[] tables)
    {
        var dataSet = new DataSet();
        foreach (var table in tables)
        {
            dataSet.Tables.Add(table);
        }

        return dataSet.CreateDataReader();
    }

    private sealed class FakeProfileCatalog : ISqlConnectionProfileCatalog
    {
        public ValueTask<IReadOnlyList<SqlConnectionProfileSummary>> ListProfilesAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<SqlConnectionProfileSummary>>([new SqlConnectionProfileSummary("local", "localhost", "master")]);
    }

    private sealed class FakeMetadataProvider : ISqlMetadataProvider
    {
        public ValueTask<IReadOnlyList<SqlDatabaseInfo>> ListDatabasesAsync(SqlConnectionTarget target, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<SqlDatabaseInfo>>(Array.Empty<SqlDatabaseInfo>());
        public ValueTask<IReadOnlyList<SqlSchemaInfo>> ListSchemasAsync(SqlConnectionTarget target, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<SqlSchemaInfo>>(Array.Empty<SqlSchemaInfo>());
        public ValueTask<IReadOnlyList<SqlTableInfo>> ListTablesAsync(SqlConnectionTarget target, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<SqlTableInfo>>(Array.Empty<SqlTableInfo>());
        public ValueTask<IReadOnlyList<SqlViewInfo>> ListViewsAsync(SqlConnectionTarget target, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<SqlViewInfo>>(Array.Empty<SqlViewInfo>());
        public ValueTask<IReadOnlyList<SqlRoutineInfo>> ListFunctionsAsync(SqlConnectionTarget target, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<SqlRoutineInfo>>(Array.Empty<SqlRoutineInfo>());
        public ValueTask<IReadOnlyList<SqlRoutineInfo>> ListProceduresAsync(SqlConnectionTarget target, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<SqlRoutineInfo>>(Array.Empty<SqlRoutineInfo>());
        public ValueTask<SqlObjectDefinition> GetObjectDefinitionAsync(SqlConnectionTarget target, string? schemaName, string objectName, SqlObjectKind objectKind, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<SqlSchemaOverview> GetSchemaOverviewAsync(SqlConnectionTarget target, CancellationToken cancellationToken = default) => ValueTask.FromResult(new SqlSchemaOverview(Array.Empty<SqlTableInfo>(), Array.Empty<SqlViewInfo>(), Array.Empty<SqlRoutineInfo>(), Array.Empty<SqlRoutineInfo>()));
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

    private sealed class FakeConnectionOpener(Func<string, DbDataReader> readerFactory) : ISqlConnectionOpener
    {
        private readonly Func<string, DbDataReader> _readerFactory = readerFactory;

        public int OpenCallCount { get; private set; }

        public ValueTask<DbConnection> OpenConnectionAsync(SqlConnectionTarget target, CancellationToken cancellationToken = default)
        {
            OpenCallCount++;
            return ValueTask.FromResult<DbConnection>(new FakeDbConnection(_readerFactory));
        }
    }

#pragma warning disable CS8764
#pragma warning disable CS8765

    private sealed class FakeDbConnection(Func<string, DbDataReader> readerFactory) : DbConnection
    {
        private readonly Func<string, DbDataReader> _readerFactory = readerFactory;
        private ConnectionState _state = ConnectionState.Closed;

        public override string? ConnectionString { get; set; } = string.Empty;
        public override string Database => "master";
        public override string DataSource => "localhost";
        public override string ServerVersion => "16.0";
        public override ConnectionState State => _state;
        public override void ChangeDatabase(string databaseName) { }
        public override void Close() => _state = ConnectionState.Closed;
        public override void Open() => _state = ConnectionState.Open;
        public override Task OpenAsync(CancellationToken cancellationToken) { _state = ConnectionState.Open; return Task.CompletedTask; }
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() => new FakeDbCommand(this, _readerFactory);
    }

    private sealed class FakeDbCommand(FakeDbConnection connection, Func<string, DbDataReader> readerFactory) : DbCommand
    {
        private readonly Func<string, DbDataReader> _readerFactory = readerFactory;
        private readonly FakeDbParameterCollection _parameters = new();
        private DbConnection _connection = connection;

        public override string? CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; } = CommandType.Text;
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get => _connection; set => _connection = value ?? throw new ArgumentNullException(nameof(value)); }
        protected override DbParameterCollection DbParameterCollection => _parameters;
        protected override DbTransaction? DbTransaction { get; set; }
        public override void Cancel() { }
        public override int ExecuteNonQuery() => throw new NotSupportedException();
        public override object? ExecuteScalar() => throw new NotSupportedException();
        public override void Prepare() { }
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
        public override void ResetDbType() { }
    }

    private sealed class FakeDbParameterCollection : DbParameterCollection
    {
        private readonly List<DbParameter> _parameters = [];
        public override int Count => _parameters.Count;
        public override object SyncRoot => ((ICollection)_parameters).SyncRoot;
        public override int Add(object? value) { _parameters.Add((DbParameter)(value ?? throw new ArgumentNullException(nameof(value)))); return _parameters.Count - 1; }
        public override void AddRange(Array values) { foreach (var value in values) { Add(value!); } }
        public override void Clear() => _parameters.Clear();
        public override bool Contains(object? value) => value is DbParameter parameter && _parameters.Contains(parameter);
        public override bool Contains(string value) => _parameters.Any(parameter => parameter.ParameterName == value);
        public override void CopyTo(Array array, int index) => ((ICollection)_parameters).CopyTo(array, index);
        public override IEnumerator GetEnumerator() => _parameters.GetEnumerator();
        public override int IndexOf(object? value) => value is DbParameter parameter ? _parameters.IndexOf(parameter) : -1;
        public override int IndexOf(string parameterName) => _parameters.FindIndex(parameter => parameter.ParameterName == parameterName);
        public override void Insert(int index, object? value) => _parameters.Insert(index, (DbParameter)(value ?? throw new ArgumentNullException(nameof(value))));
        public override void Remove(object? value) { if (value is DbParameter parameter) { _parameters.Remove(parameter); } }
        public override void RemoveAt(int index) => _parameters.RemoveAt(index);
        public override void RemoveAt(string parameterName) { var index = IndexOf(parameterName); if (index >= 0) { _parameters.RemoveAt(index); } }
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