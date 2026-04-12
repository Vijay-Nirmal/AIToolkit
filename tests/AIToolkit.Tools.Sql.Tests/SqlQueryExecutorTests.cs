using System.Collections;
using System.Data;
using System.Data.Common;
using System.Globalization;

namespace AIToolkit.Tools.Sql.Tests;

[TestClass]
public class SqlQueryExecutorTests
{
    [TestMethod]
    public async Task ExecuteAsyncMaterializesReadOnlyResultsThroughDbAbstractions()
    {
        var timestamp = new DateTimeOffset(2026, 4, 5, 12, 0, 0, TimeSpan.Zero);
        var opener = new FakeConnectionOpener(
            () => new FakeDbConnection(
                () => CreateReader(
                    [("Id", typeof(int)), ("CreatedAt", typeof(DateTimeOffset)), ("Payload", typeof(byte[]))],
                    [1, timestamp, new byte[] { 1, 2, 3 }])));

        var executor = new SqlQueryExecutor(
            opener,
            new StubQueryClassifier(new SqlQueryClassification(["SELECT"], SqlStatementSafety.ReadOnly)));

        var result = await executor.ExecuteAsync(new SqlConnectionTarget("analytics"), "SELECT * FROM events;");

        Assert.IsTrue(result.Success);
        Assert.AreEqual(SqlStatementSafety.ReadOnly, result.Classification.Safety);
        Assert.AreEqual(1, opener.OpenCallCount);
        Assert.HasCount(1, result.ResultSets);
        Assert.HasCount(1, result.ResultSets[0].Rows);
        Assert.AreEqual(1, result.ResultSets[0].Rows[0]["Id"]);
        Assert.AreEqual(timestamp.ToString("O", CultureInfo.InvariantCulture), result.ResultSets[0].Rows[0]["CreatedAt"]);
        Assert.AreEqual("AQID", result.ResultSets[0].Rows[0]["Payload"]);
    }

    [TestMethod]
    public async Task ExecuteAsyncRejectsMutationWhenPolicyDisablesMutations()
    {
        var opener = new FakeConnectionOpener(() => new FakeDbConnection(() => CreateReader([("Value", typeof(int))], [1])));
        var executor = new SqlQueryExecutor(
            opener,
            new StubQueryClassifier(new SqlQueryClassification(["DELETE"], SqlStatementSafety.ApprovalRequired)),
            SqlExecutionPolicy.ReadOnly);

        var result = await executor.ExecuteAsync(new SqlConnectionTarget("operations"), "DELETE FROM dbo.Events;");

        Assert.IsFalse(result.Success);
        Assert.AreEqual("Mutation queries are disabled by the current execution policy.", result.Message);
        Assert.AreEqual(0, opener.OpenCallCount);
    }

    [TestMethod]
    public async Task ExecuteAsyncRejectsMutationWhenApprovalIsRequiredButMissing()
    {
        var opener = new FakeConnectionOpener(() => new FakeDbConnection(() => CreateReader([("Value", typeof(int))], [1])));
        var executor = new SqlQueryExecutor(
            opener,
            new StubQueryClassifier(new SqlQueryClassification(["UPDATE"], SqlStatementSafety.ApprovalRequired)),
            new SqlExecutionPolicy
            {
                AllowMutations = true,
                RequireApprovalForMutations = true,
            });

        var result = await executor.ExecuteAsync(new SqlConnectionTarget("operations"), "UPDATE dbo.Events SET IsProcessed = 1;");

        Assert.IsFalse(result.Success);
        Assert.AreEqual("Mutation approval is required, but no ISqlMutationApprover is registered.", result.Message);
        Assert.AreEqual(0, opener.OpenCallCount);
    }

    [TestMethod]
    public async Task ExecuteAsyncRunsMutationAfterApproval()
    {
        var opener = new FakeConnectionOpener(() => new FakeDbConnection(() => CreateReader([("Applied", typeof(bool))], [true])));
        var approverInvoked = false;
        var approver = new DelegateSqlMutationApprover(
            (request, cancellationToken) =>
            {
                approverInvoked = true;
                Assert.AreEqual("operations", request.Target.ConnectionName);
                Assert.AreEqual("Inventory", request.Target.Database);
                Assert.AreEqual(SqlStatementSafety.ApprovalRequired, request.Classification.Safety);
                StringAssert.Contains(request.Query, "UPDATE");
                return ValueTask.FromResult(SqlMutationApprovalDecision.Allow("Approved for test."));
            });

        var executor = new SqlQueryExecutor(
            opener,
            new StubQueryClassifier(new SqlQueryClassification(["UPDATE"], SqlStatementSafety.ApprovalRequired)),
            new SqlExecutionPolicy
            {
                AllowMutations = true,
                RequireApprovalForMutations = true,
            },
            approver);

        var result = await executor.ExecuteAsync(
            new SqlConnectionTarget("operations", "Inventory"),
            "UPDATE dbo.Products SET Price = Price + 1;");

        Assert.IsTrue(result.Success);
        Assert.IsTrue(approverInvoked);
        Assert.AreEqual(1, opener.OpenCallCount);
        Assert.AreEqual(SqlStatementSafety.ApprovalRequired, result.Classification.Safety);
    }

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

    private sealed class StubQueryClassifier(SqlQueryClassification classification) : ISqlQueryClassifier
    {
        private readonly SqlQueryClassification _classification = classification;

        public SqlQueryClassification Classify(string query) => _classification;
    }

    private sealed class FakeConnectionOpener(Func<DbConnection> connectionFactory) : ISqlConnectionOpener
    {
        private readonly Func<DbConnection> _connectionFactory = connectionFactory;

        public int OpenCallCount { get; private set; }

        public ValueTask<DbConnection> OpenConnectionAsync(
            SqlConnectionTarget target,
            CancellationToken cancellationToken = default)
        {
            OpenCallCount++;
            return ValueTask.FromResult(_connectionFactory());
        }
    }

#pragma warning disable CS8764
#pragma warning disable CS8765

    private sealed class FakeDbConnection(Func<DataTableReader> readerFactory) : DbConnection
    {
        private readonly Func<DataTableReader> _readerFactory = readerFactory;
        private ConnectionState _state = ConnectionState.Closed;

        public override string? ConnectionString { get; set; } = string.Empty;

        public override string Database => "TestDatabase";

        public override string DataSource => "TestDataSource";

        public override string ServerVersion => "1.0";

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

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
            throw new NotSupportedException();

        protected override DbCommand CreateDbCommand() => new FakeDbCommand(this, _readerFactory);
    }

    private sealed class FakeDbCommand(FakeDbConnection connection, Func<DataTableReader> readerFactory) : DbCommand
    {
        private readonly Func<DataTableReader> _readerFactory = readerFactory;
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

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => _readerFactory();
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

        public override void Insert(int index, object? value) =>
            _parameters.Insert(index, (DbParameter)(value ?? throw new ArgumentNullException(nameof(value))));

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

        protected override DbParameter GetParameter(string parameterName) =>
            _parameters[IndexOf(parameterName)];

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