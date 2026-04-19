using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.Json;

namespace AIToolkit.Tools.Sql.Sqlite;

/// <summary>
/// Adapts the shared SQL abstractions into AI-callable SQLite tool methods.
/// </summary>
/// <remarks>
/// This class is the SQLite-specific orchestration layer between the shared abstractions and the public <c>sqlite_*</c> tool names. It logs
/// tool invocations, translates optional catalog selection into <see cref="SqlConnectionTarget"/> values, converts provider exceptions into
/// stable tool result records, and handles SQLite-specific explain-plan parsing that does not fit the provider-neutral abstractions.
/// </remarks>
/// <param name="profileCatalog">Supplies the named SQLite profiles visible to the model.</param>
/// <param name="metadataProvider">Reads catalog metadata such as attached databases, tables, and object definitions.</param>
/// <param name="queryExecutor">Executes classified queries through the shared execution pipeline.</param>
/// <param name="connectionOpener">Opens raw SQLite connections for operations that need provider-specific commands.</param>
/// <param name="queryClassifier">Classifies SQLite SQL text before execution or analysis.</param>
/// <param name="executionPolicy">Controls command timeout, mutation behavior, and result limits.</param>
internal sealed class SqliteToolService(
    ISqlConnectionProfileCatalog profileCatalog,
    ISqlMetadataProvider metadataProvider,
    ISqlQueryExecutor queryExecutor,
    ISqlConnectionOpener connectionOpener,
    ISqlQueryClassifier queryClassifier,
    SqlExecutionPolicy? executionPolicy = null)
{
    private readonly ISqlConnectionProfileCatalog _profileCatalog = profileCatalog ?? throw new ArgumentNullException(nameof(profileCatalog));
    private readonly ISqlMetadataProvider _metadataProvider = metadataProvider ?? throw new ArgumentNullException(nameof(metadataProvider));
    private readonly ISqlQueryExecutor _queryExecutor = queryExecutor ?? throw new ArgumentNullException(nameof(queryExecutor));
    private readonly ISqlConnectionOpener _connectionOpener = connectionOpener ?? throw new ArgumentNullException(nameof(connectionOpener));
    private readonly ISqlQueryClassifier _queryClassifier = queryClassifier ?? throw new ArgumentNullException(nameof(queryClassifier));
    private readonly SqlExecutionPolicy _executionPolicy = executionPolicy ?? SqlExecutionPolicy.ReadOnly;
    private static readonly JsonSerializerOptions LogJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Action<ILogger, string, string, Exception?> ToolInvocationLog =
        LoggerMessage.Define<string, string>(LogLevel.Information, new EventId(1, "SqliteToolInvocation"), "AI tool call {ToolName} with parameters {Parameters}");

    /// <summary>
    /// Lists the SQLite connection profiles registered with the host.
    /// </summary>
    /// <param name="serviceProvider">Optional services used for logging.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    /// <returns>A tool result containing connection names and file-oriented summaries.</returns>
    public async Task<SqliteListServersToolResult> ListServersAsync(IServiceProvider? serviceProvider = null, CancellationToken cancellationToken = default)
    {
        LogToolInvocation(serviceProvider, "sqlite_list_servers", new Dictionary<string, object?>());

        try
        {
            var profiles = await _profileCatalog.ListProfilesAsync(cancellationToken).ConfigureAwait(false);
            return new SqliteListServersToolResult(true, profiles);
        }
        catch (Exception exception)
        {
            return new SqliteListServersToolResult(false, Array.Empty<SqlConnectionProfileSummary>(), exception.Message);
        }
    }

    /// <summary>
    /// Lists the attached SQLite databases visible to the selected connection.
    /// </summary>
    /// <param name="connectionName">The logical SQLite connection profile to inspect.</param>
    /// <param name="serviceProvider">Optional services used for logging.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    /// <returns>A tool result containing attached catalog names such as <c>main</c> and <c>temp</c>.</returns>
    public async Task<SqliteListDatabasesToolResult> ListDatabasesAsync(string connectionName, IServiceProvider? serviceProvider = null, CancellationToken cancellationToken = default)
    {
        LogToolInvocation(serviceProvider, "sqlite_list_databases", new Dictionary<string, object?> { ["connectionName"] = connectionName });

        try
        {
            var databases = await _metadataProvider.ListDatabasesAsync(CreateTarget(connectionName), cancellationToken).ConfigureAwait(false);
            return new SqliteListDatabasesToolResult(true, databases.Select(static item => item.Name).ToArray());
        }
        catch (Exception exception)
        {
            return new SqliteListDatabasesToolResult(false, Array.Empty<string>(), exception.Message);
        }
    }

    /// <summary>
    /// Lists the schema-like namespaces available to the selected SQLite connection.
    /// </summary>
    /// <param name="connectionName">The logical SQLite connection profile to inspect.</param>
    /// <param name="database">An optional attached catalog name. When omitted, SQLite uses <c>main</c>.</param>
    /// <param name="serviceProvider">Optional services used for logging.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    /// <returns>A tool result containing SQLite catalog names used as schema equivalents.</returns>
    public async Task<SqliteListSchemasToolResult> ListSchemasAsync(string connectionName, string? database = null, IServiceProvider? serviceProvider = null, CancellationToken cancellationToken = default)
    {
        LogToolInvocation(serviceProvider, "sqlite_list_schemas", new Dictionary<string, object?> { ["connectionName"] = connectionName, ["database"] = database });

        try
        {
            var schemas = await _metadataProvider.ListSchemasAsync(CreateTarget(connectionName, database), cancellationToken).ConfigureAwait(false);
            return new SqliteListSchemasToolResult(true, schemas.Select(static item => item.Name).ToArray());
        }
        catch (Exception exception)
        {
            return new SqliteListSchemasToolResult(false, Array.Empty<string>(), exception.Message);
        }
    }

    /// <summary>
    /// Lists the tables available in the selected SQLite catalog.
    /// </summary>
    /// <param name="connectionName">The logical SQLite connection profile to inspect.</param>
    /// <param name="database">An optional attached catalog name. When omitted, SQLite uses <c>main</c>.</param>
    /// <param name="serviceProvider">Optional services used for logging.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    /// <returns>A tool result containing fully qualified table names.</returns>
    public async Task<SqliteListTablesToolResult> ListTablesAsync(string connectionName, string? database = null, IServiceProvider? serviceProvider = null, CancellationToken cancellationToken = default)
    {
        LogToolInvocation(serviceProvider, "sqlite_list_tables", new Dictionary<string, object?> { ["connectionName"] = connectionName, ["database"] = database });

        try
        {
            var tables = await _metadataProvider.ListTablesAsync(CreateTarget(connectionName, database), cancellationToken).ConfigureAwait(false);
            return new SqliteListTablesToolResult(true, tables.Select(static item => item.Table.FullyQualifiedName).ToArray());
        }
        catch (Exception exception)
        {
            return new SqliteListTablesToolResult(false, Array.Empty<string>(), exception.Message);
        }
    }

    /// <summary>
    /// Lists the views available in the selected SQLite catalog.
    /// </summary>
    /// <param name="connectionName">The logical SQLite connection profile to inspect.</param>
    /// <param name="database">An optional attached catalog name. When omitted, SQLite uses <c>main</c>.</param>
    /// <param name="serviceProvider">Optional services used for logging.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    /// <returns>A tool result containing fully qualified view names.</returns>
    public async Task<SqliteListViewsToolResult> ListViewsAsync(string connectionName, string? database = null, IServiceProvider? serviceProvider = null, CancellationToken cancellationToken = default)
    {
        LogToolInvocation(serviceProvider, "sqlite_list_views", new Dictionary<string, object?> { ["connectionName"] = connectionName, ["database"] = database });

        try
        {
            var views = await _metadataProvider.ListViewsAsync(CreateTarget(connectionName, database), cancellationToken).ConfigureAwait(false);
            return new SqliteListViewsToolResult(true, views.Select(static item => item.View.FullyQualifiedName).ToArray());
        }
        catch (Exception exception)
        {
            return new SqliteListViewsToolResult(false, Array.Empty<string>(), exception.Message);
        }
    }

    /// <summary>
    /// Lists functions known to SQLite metadata.
    /// </summary>
    /// <param name="connectionName">The logical SQLite connection profile to inspect.</param>
    /// <param name="database">An optional attached catalog name.</param>
    /// <param name="serviceProvider">Optional services used for logging.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    /// <returns>
    /// A tool result containing function names. SQLite does not expose a built-in function catalog, so successful calls normally return an
    /// empty collection.
    /// </returns>
    public async Task<SqliteListFunctionsToolResult> ListFunctionsAsync(string connectionName, string? database = null, IServiceProvider? serviceProvider = null, CancellationToken cancellationToken = default)
    {
        LogToolInvocation(serviceProvider, "sqlite_list_functions", new Dictionary<string, object?> { ["connectionName"] = connectionName, ["database"] = database });

        try
        {
            var functions = await _metadataProvider.ListFunctionsAsync(CreateTarget(connectionName, database), cancellationToken).ConfigureAwait(false);
            return new SqliteListFunctionsToolResult(true, functions.Select(static item => item.Routine.FullyQualifiedName).ToArray());
        }
        catch (Exception exception)
        {
            return new SqliteListFunctionsToolResult(false, Array.Empty<string>(), exception.Message);
        }
    }

    /// <summary>
    /// Lists procedures known to SQLite metadata.
    /// </summary>
    /// <param name="connectionName">The logical SQLite connection profile to inspect.</param>
    /// <param name="database">An optional attached catalog name.</param>
    /// <param name="serviceProvider">Optional services used for logging.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    /// <returns>
    /// A tool result containing procedure names. SQLite does not support stored procedures, so successful calls normally return an empty
    /// collection.
    /// </returns>
    public async Task<SqliteListProceduresToolResult> ListProceduresAsync(string connectionName, string? database = null, IServiceProvider? serviceProvider = null, CancellationToken cancellationToken = default)
    {
        LogToolInvocation(serviceProvider, "sqlite_list_procedures", new Dictionary<string, object?> { ["connectionName"] = connectionName, ["database"] = database });

        try
        {
            var procedures = await _metadataProvider.ListProceduresAsync(CreateTarget(connectionName, database), cancellationToken).ConfigureAwait(false);
            return new SqliteListProceduresToolResult(true, procedures.Select(static item => item.Routine.FullyQualifiedName).ToArray());
        }
        catch (Exception exception)
        {
            return new SqliteListProceduresToolResult(false, Array.Empty<string>(), exception.Message);
        }
    }

    /// <summary>
    /// Gets the SQLite definition for a table or view.
    /// </summary>
    /// <param name="connectionName">The logical SQLite connection profile to inspect.</param>
    /// <param name="objectName">The table or view name to resolve.</param>
    /// <param name="schemaName">An optional attached catalog name. When omitted, SQLite uses the requested database or <c>main</c>.</param>
    /// <param name="objectKind">An optional object-kind hint such as <c>Table</c> or <c>View</c>.</param>
    /// <param name="database">An optional attached catalog override for the connection target.</param>
    /// <param name="serviceProvider">Optional services used for logging.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    /// <returns>A tool result containing the resolved <c>CREATE TABLE</c> or <c>CREATE VIEW</c> text.</returns>
    public async Task<SqliteGetObjectDefinitionToolResult> GetObjectDefinitionAsync(string connectionName, string objectName, string? schemaName = null, string? objectKind = null, string? database = null, IServiceProvider? serviceProvider = null, CancellationToken cancellationToken = default)
    {
        LogToolInvocation(serviceProvider, "sqlite_get_object_definition", new Dictionary<string, object?>
        {
            ["connectionName"] = connectionName,
            ["database"] = database,
            ["objectName"] = objectName,
            ["schemaName"] = schemaName,
            ["objectKind"] = objectKind,
        });

        try
        {
            var kind = ParseObjectKind(objectKind);
            var definition = await _metadataProvider.GetObjectDefinitionAsync(CreateTarget(connectionName, database), schemaName, objectName, kind, cancellationToken).ConfigureAwait(false);
            return new SqliteGetObjectDefinitionToolResult(true, definition);
        }
        catch (Exception exception)
        {
            return new SqliteGetObjectDefinitionToolResult(false, null, exception.Message);
        }
    }

    /// <summary>
    /// Executes SQLite SQL text through the shared execution pipeline.
    /// </summary>
    /// <param name="connectionName">The logical SQLite connection profile to use.</param>
    /// <param name="query">The SQL text to classify, approve if required, and execute.</param>
    /// <param name="database">An optional attached catalog override for the connection target.</param>
    /// <param name="serviceProvider">Optional services used for logging.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    /// <returns>A tool result containing the execution classification, any result sets, and provider messages.</returns>
    /// <seealso cref="ISqlQueryExecutor.ExecuteAsync(SqlConnectionTarget, string, CancellationToken)"/>
    public async Task<SqliteRunQueryToolResult> RunQueryAsync(string connectionName, string query, string? database = null, IServiceProvider? serviceProvider = null, CancellationToken cancellationToken = default)
    {
        LogToolInvocation(serviceProvider, "sqlite_run_query", new Dictionary<string, object?> { ["connectionName"] = connectionName, ["database"] = database, ["query"] = query });

        try
        {
            var result = await _queryExecutor.ExecuteAsync(CreateTarget(connectionName, database), query, cancellationToken).ConfigureAwait(false);
            return new SqliteRunQueryToolResult(result.Success, result, result.Message);
        }
        catch (Exception exception)
        {
            return new SqliteRunQueryToolResult(false, null, exception.Message);
        }
    }

    /// <summary>
    /// Analyzes a read-only SQLite query with <c>EXPLAIN QUERY PLAN</c>.
    /// </summary>
    /// <param name="connectionName">The logical SQLite connection profile to use.</param>
    /// <param name="query">The read-only SQL statement to analyze.</param>
    /// <param name="database">An optional attached catalog override for the connection target.</param>
    /// <param name="serviceProvider">Optional services used for logging.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    /// <returns>A tool result containing the synthesized explain payload and root-node summary.</returns>
    /// <remarks>
    /// SQLite returns <c>EXPLAIN QUERY PLAN</c> output as simple rows rather than rich XML or JSON. This method interprets the provider
    /// output, infers a root-node summary, and serializes the tabular plan into JSON for downstream consumers.
    /// </remarks>
    public async Task<SqliteExplainQueryToolResult> ExplainQueryAsync(string connectionName, string query, string? database = null, IServiceProvider? serviceProvider = null, CancellationToken cancellationToken = default)
    {
        LogToolInvocation(serviceProvider, "sqlite_explain_query", new Dictionary<string, object?> { ["connectionName"] = connectionName, ["database"] = database, ["query"] = query });

        try
        {
            var classification = _queryClassifier.Classify(query);
            if (classification.Safety != SqlStatementSafety.ReadOnly)
            {
                return new SqliteExplainQueryToolResult(false, null, classification.Reason ?? "Only read-only SQLite queries can be analyzed with sqlite_explain_query.");
            }

            if (classification.StatementTypes.Contains("EXPLAIN", StringComparer.Ordinal))
            {
                return new SqliteExplainQueryToolResult(false, null, "Provide the underlying SQLite query instead of an EXPLAIN statement.");
            }

            var explainQuery = BuildExplainQuery(query);
            var (planPayload, rootNode) = await ExecuteExplainQueryAsync(CreateTarget(connectionName, database), explainQuery, cancellationToken).ConfigureAwait(false);
            var result = new SqlExplainQueryResult(query, explainQuery, "tabular-json", classification, rootNode, planPayload);
            return new SqliteExplainQueryToolResult(true, result);
        }
        catch (Exception exception)
        {
            return new SqliteExplainQueryToolResult(false, null, exception.Message);
        }
    }

    private static SqlObjectKind ParseObjectKind(string? objectKind) =>
        string.IsNullOrWhiteSpace(objectKind)
            ? SqlObjectKind.Unknown
            : Enum.TryParse<SqlObjectKind>(objectKind, ignoreCase: true, out var kind) ? kind : SqlObjectKind.Unknown;

    private async Task<(string PlanPayload, SqlExplainNodeSummary? RootNode)> ExecuteExplainQueryAsync(SqlConnectionTarget target, string explainQuery, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionOpener.OpenConnectionAsync(target, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = explainQuery;
        command.CommandTimeout = Math.Max(1, _executionPolicy.CommandTimeoutSeconds);

        var rows = new List<Dictionary<string, object?>>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var id = reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture);
            var parentId = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture);
            var detail = reader.IsDBNull(3) ? string.Empty : Convert.ToString(reader.GetValue(3), CultureInfo.InvariantCulture) ?? string.Empty;

            rows.Add(
                new Dictionary<string, object?>
                {
                    ["id"] = id,
                    ["parentId"] = parentId,
                    ["detail"] = detail,
                });
        }

        if (rows.Count == 0)
        {
            throw new InvalidOperationException("SQLite did not return an EXPLAIN QUERY PLAN result.");
        }

        // SQLite exposes planner output as free-form detail strings, so the first row is used to derive a compact root-node summary.
        var firstDetail = rows[0]["detail"] as string;
        var rootNode = new SqlExplainNodeSummary(
            NodeType: ExtractSqliteNodeType(firstDetail),
            RelationName: ExtractSqliteRelationName(firstDetail),
            Schema: string.IsNullOrWhiteSpace(target.Database) ? "main" : target.Database,
            Alias: null,
            IndexName: ExtractSqliteIndexName(firstDetail),
            JoinType: null,
            Strategy: null,
            StartupCost: null,
            TotalCost: null,
            PlanRows: null,
            PlanWidth: null,
            WorkersPlanned: null,
            WorkersLaunched: null,
            Filter: firstDetail,
            IndexCondition: null,
            HashCondition: null,
            MergeCondition: null,
            Timing: new SqlExplainTimingStatistics(null, null, null, null, null, null, null, null),
            Buffers: new SqlExplainBufferStatistics(null, null, null, null, null, null, null, null, null, null),
            Wal: new SqlExplainWalStatistics(null, null, null));

        return (JsonSerializer.Serialize(rows, LogJsonOptions), rootNode);
    }

    private static string BuildExplainQuery(string query) =>
        $"EXPLAIN QUERY PLAN {query.Trim().TrimEnd(';')};";

    private static string? ExtractSqliteNodeType(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return null;
        }

        var knownPrefixes = new[]
        {
            "USE TEMP B-TREE",
            "MULTI-INDEX OR",
            "COMPOUND QUERY",
            "LEFT-MOST SUBQUERY",
            "CORRELATED SCALAR SUBQUERY",
            "SCALAR SUBQUERY",
            "CO-ROUTINE",
            "MATERIALIZE",
            "SEARCH",
            "SCAN",
            "MERGE",
        };

        return knownPrefixes.FirstOrDefault(prefix => detail.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            ?? detail.Split(' ', 2, StringSplitOptions.TrimEntries)[0];
    }

    private static string? ExtractSqliteRelationName(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return null;
        }

        if (detail.StartsWith("SCAN ", StringComparison.OrdinalIgnoreCase))
        {
            return detail[5..].Split(' ', 2, StringSplitOptions.TrimEntries)[0];
        }

        if (detail.StartsWith("SEARCH ", StringComparison.OrdinalIgnoreCase))
        {
            return detail[7..].Split(' ', 2, StringSplitOptions.TrimEntries)[0];
        }

        return null;
    }

    private static string? ExtractSqliteIndexName(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return null;
        }

        var usingIndex = "USING INDEX ";
        var usingCoveringIndex = "USING COVERING INDEX ";

        if (detail.Contains(usingCoveringIndex, StringComparison.OrdinalIgnoreCase))
        {
            var remainder = detail[(detail.IndexOf(usingCoveringIndex, StringComparison.OrdinalIgnoreCase) + usingCoveringIndex.Length)..];
            return remainder.Split(' ', 2, StringSplitOptions.TrimEntries)[0];
        }

        if (detail.Contains(usingIndex, StringComparison.OrdinalIgnoreCase))
        {
            var remainder = detail[(detail.IndexOf(usingIndex, StringComparison.OrdinalIgnoreCase) + usingIndex.Length)..];
            return remainder.Split(' ', 2, StringSplitOptions.TrimEntries)[0];
        }

        return null;
    }

    private static SqlConnectionTarget CreateTarget(string connectionName, string? database = null)
    {
        if (string.IsNullOrWhiteSpace(connectionName))
        {
            throw new ArgumentException("connectionName is required.", nameof(connectionName));
        }

        return new SqlConnectionTarget(connectionName, database);
    }

    private static void LogToolInvocation(IServiceProvider? serviceProvider, string toolName, IReadOnlyDictionary<string, object?> parameters)
    {
        var loggerFactory = serviceProvider?.GetService(typeof(ILoggerFactory)) as ILoggerFactory;
        var logger = loggerFactory?.CreateLogger("AIToolkit.Tools.Sql.Sqlite.Tools");
        if (logger is null || !logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        ToolInvocationLog(logger, toolName, JsonSerializer.Serialize(parameters, LogJsonOptions), null);
    }
}
