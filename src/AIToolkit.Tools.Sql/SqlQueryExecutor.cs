using System.Data.Common;
using System.Globalization;

namespace AIToolkit.Tools.Sql;

/// <summary>
/// Executes SQL text through provider-neutral ADO.NET abstractions.
/// </summary>
/// <remarks>
/// Provider packages can reuse this executor by supplying an <see cref="ISqlConnectionOpener"/> and an
/// <see cref="ISqlQueryClassifier"/>. Override the virtual members when a provider needs custom materialization behavior.
/// </remarks>
public class SqlQueryExecutor(
    ISqlConnectionOpener connectionOpener,
    ISqlQueryClassifier queryClassifier,
    SqlExecutionPolicy? executionPolicy = null,
    ISqlMutationApprover? mutationApprover = null)
    : ISqlQueryExecutor
{
    private readonly ISqlConnectionOpener _connectionOpener = connectionOpener ?? throw new ArgumentNullException(nameof(connectionOpener));
    private readonly ISqlQueryClassifier _queryClassifier = queryClassifier ?? throw new ArgumentNullException(nameof(queryClassifier));
    private readonly SqlExecutionPolicy _executionPolicy = executionPolicy ?? SqlExecutionPolicy.ReadOnly;
    private readonly ISqlMutationApprover? _mutationApprover = mutationApprover;

    /// <inheritdoc />
    public async ValueTask<SqlExecuteQueryResult> ExecuteAsync(
        SqlConnectionTarget target,
        string query,
        CancellationToken cancellationToken = default)
    {
        var classification = _queryClassifier.Classify(query);

        if (classification.Safety == SqlStatementSafety.Blocked)
        {
            return new SqlExecuteQueryResult(false, classification, Array.Empty<SqlResultSet>(), Message: classification.Reason);
        }

        if (classification.Safety == SqlStatementSafety.ApprovalRequired)
        {
            var approvalResult = await ApproveMutationAsync(target, query, classification, cancellationToken).ConfigureAwait(false);
            if (approvalResult is not null)
            {
                return approvalResult;
            }
        }

        try
        {
            await using var connection = await _connectionOpener.OpenConnectionAsync(target, cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = query;
            command.CommandTimeout = Math.Max(1, _executionPolicy.CommandTimeoutSeconds);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var resultSets = new List<SqlResultSet>();
            var resultSetCount = 0;

            do
            {
                if (resultSetCount >= _executionPolicy.MaxResultSets)
                {
                    break;
                }

                var resultSet = await ReadResultSetAsync(reader, cancellationToken).ConfigureAwait(false);
                if (resultSet is not null)
                {
                    resultSets.Add(resultSet);
                    resultSetCount++;
                }
            }
            while (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false));

            return new SqlExecuteQueryResult(
                Success: true,
                Classification: classification,
                ResultSets: resultSets,
                RecordsAffected: reader.RecordsAffected);
        }
        catch (Exception exception)
        {
            return new SqlExecuteQueryResult(false, classification, Array.Empty<SqlResultSet>(), Message: exception.Message);
        }
    }

    /// <summary>
    /// Creates the result column model for one provider column.
    /// </summary>
    /// <param name="column">The provider column metadata.</param>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns>The materialized result column.</returns>
    protected virtual SqlResultColumn CreateResultColumn(DbColumn column, int ordinal) =>
        new(
            column.ColumnName ?? $"Column{ordinal}",
            GetDataTypeName(column, ordinal),
            ordinal,
            column.AllowDBNull ?? true);

    /// <summary>
    /// Resolves the displayed data type name for one provider column.
    /// </summary>
    /// <param name="column">The provider column metadata.</param>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns>The data type name that should appear in results.</returns>
    protected virtual string GetDataTypeName(DbColumn column, int ordinal) =>
        column.DataTypeName ?? column.DataType?.Name ?? "object";

    /// <summary>
    /// Normalizes one provider value into a JSON-friendly result value.
    /// </summary>
    /// <param name="value">The provider value to normalize.</param>
    /// <returns>The normalized value.</returns>
    protected virtual object? NormalizeValue(object value)
    {
        if (value is DBNull)
        {
            return null;
        }

        return value switch
        {
            string text when text.Length > _executionPolicy.MaxStringLength =>
                text[.._executionPolicy.MaxStringLength] + "...",
            byte[] bytes => Convert.ToBase64String(bytes),
            DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
            TimeSpan timeSpan => timeSpan.ToString("c", CultureInfo.InvariantCulture),
            sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal or bool or Guid => value,
            _ => Convert.ToString(value, CultureInfo.InvariantCulture),
        };
    }

    private async ValueTask<SqlExecuteQueryResult?> ApproveMutationAsync(
        SqlConnectionTarget target,
        string query,
        SqlQueryClassification classification,
        CancellationToken cancellationToken)
    {
        if (!_executionPolicy.AllowMutations)
        {
            return new SqlExecuteQueryResult(
                false,
                classification,
                Array.Empty<SqlResultSet>(),
                Message: "Mutation queries are disabled by the current execution policy.");
        }

        if (!_executionPolicy.RequireApprovalForMutations)
        {
            return null;
        }

        if (_mutationApprover is null)
        {
            return new SqlExecuteQueryResult(
                false,
                classification,
                Array.Empty<SqlResultSet>(),
                Message: "Mutation approval is required, but no ISqlMutationApprover is registered.");
        }

        var decision = await _mutationApprover
            .ApproveAsync(new SqlMutationApprovalRequest(target, query, classification), cancellationToken)
            .ConfigureAwait(false);

        if (decision.Approved)
        {
            return null;
        }

        return new SqlExecuteQueryResult(false, classification, Array.Empty<SqlResultSet>(), Message: decision.Reason);
    }

    private async ValueTask<SqlResultSet?> ReadResultSetAsync(
        DbDataReader reader,
        CancellationToken cancellationToken)
    {
        if (reader.FieldCount == 0)
        {
            return null;
        }

        var schema = reader.GetColumnSchema();
        var columns = new List<SqlResultColumn>(reader.FieldCount);
        for (var ordinal = 0; ordinal < reader.FieldCount; ordinal++)
        {
            columns.Add(CreateResultColumn(schema[ordinal], ordinal));
        }

        var rows = new List<Dictionary<string, object?>>();
        var totalRows = 0;
        var isTruncated = false;

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            totalRows++;
            if (rows.Count >= _executionPolicy.MaxRows)
            {
                isTruncated = true;
                continue;
            }

            var row = new Dictionary<string, object?>(reader.FieldCount, StringComparer.OrdinalIgnoreCase);
            for (var ordinal = 0; ordinal < reader.FieldCount; ordinal++)
            {
                row[columns[ordinal].Name] = NormalizeValue(reader.GetValue(ordinal));
            }

            rows.Add(row);
        }

        return new SqlResultSet(columns, rows, isTruncated, totalRows);
    }
}