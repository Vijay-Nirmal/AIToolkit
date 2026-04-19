using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AIToolkit.Tools.Sql.MySql;

/// <summary>
/// Adapts the shared SQL abstractions into AI-callable MySQL tool methods.
/// </summary>
/// <remarks>
/// This class is the MySQL-specific orchestration layer between the shared abstractions and the public <c>mysql_*</c> tool names. It logs
/// tool calls, translates optional database selection into <see cref="SqlConnectionTarget"/> values, converts provider exceptions into stable
/// tool result records, and interprets MySQL's text-based <c>EXPLAIN ANALYZE FORMAT=TREE</c> output into the shared explain-plan models.
/// </remarks>
/// <param name="profileCatalog">Supplies the named MySQL profiles visible to the model.</param>
/// <param name="metadataProvider">Reads database, schema, object, and routine metadata.</param>
/// <param name="queryExecutor">Executes classified queries through the shared execution pipeline.</param>
/// <param name="connectionOpener">Opens raw MySQL connections for provider-specific commands.</param>
/// <param name="queryClassifier">Classifies MySQL SQL text before execution or analysis.</param>
/// <param name="executionPolicy">Controls command timeout, mutation behavior, and result limits.</param>
internal sealed class MySqlToolService(
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
        LoggerMessage.Define<string, string>(LogLevel.Information, new EventId(1, "MySqlToolInvocation"), "AI tool call {ToolName} with parameters {Parameters}");
    private static readonly Regex MySqlEstimatedNodeRegex = new(@"^->\s*(?<node>.+?)\s+\(cost=(?<cost>[-+0-9.eE]+)\s+rows=(?<rows>[-+0-9.eE]+)\)$", RegexOptions.Compiled);
    private static readonly Regex MySqlActualNodeRegex = new(@"actual time=(?<start>[-+0-9.eE]+)\.\.(?<end>[-+0-9.eE]+)\s+rows=(?<rows>[-+0-9.eE]+)\s+loops=(?<loops>[-+0-9.eE]+)", RegexOptions.Compiled);

    /// <summary>
    /// Lists the MySQL connection profiles registered with the host.
    /// </summary>
    /// <param name="serviceProvider">Optional services used for logging.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    /// <returns>A tool result containing connection names and server summaries.</returns>
    public async Task<MySqlListServersToolResult> ListServersAsync(IServiceProvider? serviceProvider = null, CancellationToken cancellationToken = default)
    {
        LogToolInvocation(serviceProvider, "mysql_list_servers", new Dictionary<string, object?>());

        try
        {
            var profiles = await _profileCatalog.ListProfilesAsync(cancellationToken).ConfigureAwait(false);
            return new MySqlListServersToolResult(true, profiles);
        }
        catch (Exception exception)
        {
            return new MySqlListServersToolResult(false, Array.Empty<SqlConnectionProfileSummary>(), exception.Message);
        }
    }

    /// <summary>
    /// Lists the databases visible to the selected MySQL connection.
    /// </summary>
    /// <param name="connectionName">The logical MySQL connection profile to inspect.</param>
    /// <param name="serviceProvider">Optional services used for logging.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    /// <returns>A tool result containing visible MySQL database names.</returns>
    public async Task<MySqlListDatabasesToolResult> ListDatabasesAsync(string connectionName, IServiceProvider? serviceProvider = null, CancellationToken cancellationToken = default)
    {
        LogToolInvocation(serviceProvider, "mysql_list_databases", new Dictionary<string, object?> { ["connectionName"] = connectionName });

        try
        {
            var databases = await _metadataProvider.ListDatabasesAsync(CreateTarget(connectionName), cancellationToken).ConfigureAwait(false);
            return new MySqlListDatabasesToolResult(true, databases.Select(static item => item.Name).ToArray());
        }
        catch (Exception exception)
        {
            return new MySqlListDatabasesToolResult(false, Array.Empty<string>(), exception.Message);
        }
    }

    /// <summary>
    /// Lists the schema context exposed by the selected MySQL connection.
    /// </summary>
    /// <param name="connectionName">The logical MySQL connection profile to inspect.</param>
    /// <param name="database">An optional database override for the connection target.</param>
    /// <param name="serviceProvider">Optional services used for logging.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    /// <returns>A tool result containing the active schema name for the selected database context.</returns>
    public async Task<MySqlListSchemasToolResult> ListSchemasAsync(string connectionName, string? database = null, IServiceProvider? serviceProvider = null, CancellationToken cancellationToken = default)
    {
        LogToolInvocation(serviceProvider, "mysql_list_schemas", new Dictionary<string, object?> { ["connectionName"] = connectionName, ["database"] = database });

        try
        {
            var schemas = await _metadataProvider.ListSchemasAsync(CreateTarget(connectionName, database), cancellationToken).ConfigureAwait(false);
            return new MySqlListSchemasToolResult(true, schemas.Select(static item => item.Name).ToArray());
        }
        catch (Exception exception)
        {
            return new MySqlListSchemasToolResult(false, Array.Empty<string>(), exception.Message);
        }
    }

    /// <summary>
    /// Lists the tables available in the selected MySQL database.
    /// </summary>
    /// <param name="connectionName">The logical MySQL connection profile to inspect.</param>
    /// <param name="database">An optional database override for the connection target.</param>
    /// <param name="serviceProvider">Optional services used for logging.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    /// <returns>A tool result containing fully qualified table names.</returns>
    public async Task<MySqlListTablesToolResult> ListTablesAsync(string connectionName, string? database = null, IServiceProvider? serviceProvider = null, CancellationToken cancellationToken = default)
    {
        LogToolInvocation(serviceProvider, "mysql_list_tables", new Dictionary<string, object?> { ["connectionName"] = connectionName, ["database"] = database });

        try
        {
            var tables = await _metadataProvider.ListTablesAsync(CreateTarget(connectionName, database), cancellationToken).ConfigureAwait(false);
            return new MySqlListTablesToolResult(true, tables.Select(static item => item.Table.FullyQualifiedName).ToArray());
        }
        catch (Exception exception)
        {
            return new MySqlListTablesToolResult(false, Array.Empty<string>(), exception.Message);
        }
    }

    /// <summary>
    /// Lists the views available in the selected MySQL database.
    /// </summary>
    /// <param name="connectionName">The logical MySQL connection profile to inspect.</param>
    /// <param name="database">An optional database override for the connection target.</param>
    /// <param name="serviceProvider">Optional services used for logging.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    /// <returns>A tool result containing fully qualified view names.</returns>
    public async Task<MySqlListViewsToolResult> ListViewsAsync(string connectionName, string? database = null, IServiceProvider? serviceProvider = null, CancellationToken cancellationToken = default)
    {
        LogToolInvocation(serviceProvider, "mysql_list_views", new Dictionary<string, object?> { ["connectionName"] = connectionName, ["database"] = database });

        try
        {
            var views = await _metadataProvider.ListViewsAsync(CreateTarget(connectionName, database), cancellationToken).ConfigureAwait(false);
            return new MySqlListViewsToolResult(true, views.Select(static item => item.View.FullyQualifiedName).ToArray());
        }
        catch (Exception exception)
        {
            return new MySqlListViewsToolResult(false, Array.Empty<string>(), exception.Message);
        }
    }

    /// <summary>
    /// Lists the functions available in the selected MySQL database.
    /// </summary>
    /// <param name="connectionName">The logical MySQL connection profile to inspect.</param>
    /// <param name="database">An optional database override for the connection target.</param>
    /// <param name="serviceProvider">Optional services used for logging.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    /// <returns>A tool result containing fully qualified MySQL function names.</returns>
    public async Task<MySqlListFunctionsToolResult> ListFunctionsAsync(string connectionName, string? database = null, IServiceProvider? serviceProvider = null, CancellationToken cancellationToken = default)
    {
        LogToolInvocation(serviceProvider, "mysql_list_functions", new Dictionary<string, object?> { ["connectionName"] = connectionName, ["database"] = database });

        try
        {
            var functions = await _metadataProvider.ListFunctionsAsync(CreateTarget(connectionName, database), cancellationToken).ConfigureAwait(false);
            return new MySqlListFunctionsToolResult(true, functions.Select(static item => item.Routine.FullyQualifiedName).ToArray());
        }
        catch (Exception exception)
        {
            return new MySqlListFunctionsToolResult(false, Array.Empty<string>(), exception.Message);
        }
    }

    /// <summary>
    /// Lists the procedures available in the selected MySQL database.
    /// </summary>
    /// <param name="connectionName">The logical MySQL connection profile to inspect.</param>
    /// <param name="database">An optional database override for the connection target.</param>
    /// <param name="serviceProvider">Optional services used for logging.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    /// <returns>A tool result containing fully qualified MySQL procedure names.</returns>
    public async Task<MySqlListProceduresToolResult> ListProceduresAsync(string connectionName, string? database = null, IServiceProvider? serviceProvider = null, CancellationToken cancellationToken = default)
    {
        LogToolInvocation(serviceProvider, "mysql_list_procedures", new Dictionary<string, object?> { ["connectionName"] = connectionName, ["database"] = database });

        try
        {
            var procedures = await _metadataProvider.ListProceduresAsync(CreateTarget(connectionName, database), cancellationToken).ConfigureAwait(false);
            return new MySqlListProceduresToolResult(true, procedures.Select(static item => item.Routine.FullyQualifiedName).ToArray());
        }
        catch (Exception exception)
        {
            return new MySqlListProceduresToolResult(false, Array.Empty<string>(), exception.Message);
        }
    }

    /// <summary>
    /// Gets the MySQL definition for a table, view, function, or procedure.
    /// </summary>
    /// <param name="connectionName">The logical MySQL connection profile to inspect.</param>
    /// <param name="objectName">The object name to resolve.</param>
    /// <param name="schemaName">An optional schema or database name.</param>
    /// <param name="objectKind">An optional object-kind hint such as <c>Table</c> or <c>Procedure</c>.</param>
    /// <param name="database">An optional database override for the connection target.</param>
    /// <param name="serviceProvider">Optional services used for logging.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    /// <returns>A tool result containing the resolved object definition.</returns>
    public async Task<MySqlGetObjectDefinitionToolResult> GetObjectDefinitionAsync(string connectionName, string objectName, string? schemaName = null, string? objectKind = null, string? database = null, IServiceProvider? serviceProvider = null, CancellationToken cancellationToken = default)
    {
        LogToolInvocation(serviceProvider, "mysql_get_object_definition", new Dictionary<string, object?>
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
            return new MySqlGetObjectDefinitionToolResult(true, definition);
        }
        catch (Exception exception)
        {
            return new MySqlGetObjectDefinitionToolResult(false, null, exception.Message);
        }
    }

    /// <summary>
    /// Executes MySQL SQL text through the shared execution pipeline.
    /// </summary>
    /// <param name="connectionName">The logical MySQL connection profile to use.</param>
    /// <param name="query">The SQL text to classify, approve if required, and execute.</param>
    /// <param name="database">An optional database override for the connection target.</param>
    /// <param name="serviceProvider">Optional services used for logging.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    /// <returns>A tool result containing the execution classification, any result sets, and provider messages.</returns>
    public async Task<MySqlRunQueryToolResult> RunQueryAsync(string connectionName, string query, string? database = null, IServiceProvider? serviceProvider = null, CancellationToken cancellationToken = default)
    {
        LogToolInvocation(serviceProvider, "mysql_run_query", new Dictionary<string, object?> { ["connectionName"] = connectionName, ["database"] = database, ["query"] = query });

        try
        {
            var result = await _queryExecutor.ExecuteAsync(CreateTarget(connectionName, database), query, cancellationToken).ConfigureAwait(false);
            return new MySqlRunQueryToolResult(result.Success, result, result.Message);
        }
        catch (Exception exception)
        {
            return new MySqlRunQueryToolResult(false, null, exception.Message);
        }
    }

    /// <summary>
    /// Analyzes a read-only MySQL query with <c>EXPLAIN ANALYZE FORMAT=TREE</c>.
    /// </summary>
    /// <param name="connectionName">The logical MySQL connection profile to use.</param>
    /// <param name="query">The read-only SQL statement to analyze.</param>
    /// <param name="database">An optional database override for the connection target.</param>
    /// <param name="serviceProvider">Optional services used for logging.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    /// <returns>A tool result containing the parsed MySQL plan summary and raw plan payload.</returns>
    /// <remarks>
    /// MySQL returns tree-shaped explain output as text. This method parses the first estimated and actual plan lines it can recognize and maps
    /// them into <see cref="SqlExplainNodeSummary"/> and <see cref="SqlExplainTimingStatistics"/> for cross-provider consumption.
    /// </remarks>
    public async Task<MySqlExplainQueryToolResult> ExplainQueryAsync(string connectionName, string query, string? database = null, IServiceProvider? serviceProvider = null, CancellationToken cancellationToken = default)
    {
        LogToolInvocation(serviceProvider, "mysql_explain_query", new Dictionary<string, object?> { ["connectionName"] = connectionName, ["database"] = database, ["query"] = query });

        try
        {
            var classification = _queryClassifier.Classify(query);
            if (classification.Safety != SqlStatementSafety.ReadOnly)
            {
                return new MySqlExplainQueryToolResult(false, null, classification.Reason ?? "Only read-only MySQL queries can be analyzed with mysql_explain_query.");
            }

            if (classification.StatementTypes.Contains("EXPLAIN", StringComparer.Ordinal))
            {
                return new MySqlExplainQueryToolResult(false, null, "Provide the underlying MySQL query instead of an EXPLAIN statement.");
            }

            var explainQuery = BuildExplainQuery(query);
            var planPayload = await ExecuteExplainQueryAsync(CreateTarget(connectionName, database), explainQuery, cancellationToken).ConfigureAwait(false);
            var result = CreateExplainResult(query, explainQuery, classification, planPayload);
            return new MySqlExplainQueryToolResult(true, result);
        }
        catch (Exception exception)
        {
            return new MySqlExplainQueryToolResult(false, null, exception.Message);
        }
    }

    private static SqlObjectKind ParseObjectKind(string? objectKind) =>
        string.IsNullOrWhiteSpace(objectKind)
            ? SqlObjectKind.Unknown
            : Enum.TryParse<SqlObjectKind>(objectKind, ignoreCase: true, out var kind) ? kind : SqlObjectKind.Unknown;

    private async Task<string> ExecuteExplainQueryAsync(SqlConnectionTarget target, string explainQuery, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionOpener.OpenConnectionAsync(target, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = explainQuery;
        command.CommandTimeout = Math.Max(1, _executionPolicy.CommandTimeoutSeconds);

        var lines = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (reader.FieldCount == 0 || reader.IsDBNull(0))
            {
                continue;
            }

            var line = Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(line))
            {
                lines.Add(line);
            }
        }

        return lines.Count == 0
            ? throw new InvalidOperationException("MySQL did not return an EXPLAIN ANALYZE plan. This may require MySQL 8.0.18+ and EXPLAIN ANALYZE support.")
            : string.Join(Environment.NewLine, lines);
    }

    private static string BuildExplainQuery(string query) =>
        $"EXPLAIN ANALYZE FORMAT=TREE {query.Trim().TrimEnd(';')};";

    private static SqlExplainQueryResult CreateExplainResult(string query, string explainQuery, SqlQueryClassification classification, string planPayload)
    {
        var lines = planPayload
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static line => line.Trim())
            .ToArray();

        // MySQL tree plans are plain text, so the parser looks for the first estimated and actual iterator lines it can reliably recognize.
        var estimatedMatch = lines.Select(line => MySqlEstimatedNodeRegex.Match(line)).FirstOrDefault(static match => match.Success);
        var actualMatch = lines.Select(line => MySqlActualNodeRegex.Match(line)).FirstOrDefault(static match => match.Success);

        var nodeDescription = estimatedMatch is { Success: true }
            ? estimatedMatch.Groups["node"].Value
            : lines.FirstOrDefault(static line => line.StartsWith("->", StringComparison.Ordinal))?.TrimStart('-', '>').Trim();

        var timing = new SqlExplainTimingStatistics(
            PlanningTimeMs: null,
            ExecutionTimeMs: actualMatch is { Success: true } ? ParseDouble(actualMatch.Groups["end"].Value) : null,
            PlanningCpuTimeMs: null,
            ExecutionCpuTimeMs: null,
            ActualStartupTimeMs: actualMatch is { Success: true } ? ParseDouble(actualMatch.Groups["start"].Value) : null,
            ActualTotalTimeMs: actualMatch is { Success: true } ? ParseDouble(actualMatch.Groups["end"].Value) : null,
            ActualRows: actualMatch is { Success: true } ? ParseDouble(actualMatch.Groups["rows"].Value) : null,
            ActualLoops: actualMatch is { Success: true } ? ParseInt(actualMatch.Groups["loops"].Value) : null);

        var rootNode = new SqlExplainNodeSummary(
            NodeType: ExtractMySqlNodeType(nodeDescription),
            RelationName: ExtractMySqlRelationName(nodeDescription),
            Schema: null,
            Alias: null,
            IndexName: ExtractMySqlIndexName(nodeDescription),
            JoinType: nodeDescription?.Contains("join", StringComparison.OrdinalIgnoreCase) == true ? nodeDescription : null,
            Strategy: null,
            StartupCost: null,
            TotalCost: estimatedMatch is { Success: true } ? ParseDecimal(estimatedMatch.Groups["cost"].Value) : null,
            PlanRows: estimatedMatch is { Success: true } ? ParseLong(estimatedMatch.Groups["rows"].Value) : null,
            PlanWidth: null,
            WorkersPlanned: null,
            WorkersLaunched: null,
            Filter: nodeDescription?.StartsWith("Filter:", StringComparison.OrdinalIgnoreCase) == true ? nodeDescription : null,
            IndexCondition: null,
            HashCondition: null,
            MergeCondition: null,
            Timing: timing,
            Buffers: new SqlExplainBufferStatistics(null, null, null, null, null, null, null, null, null, null),
            Wal: new SqlExplainWalStatistics(null, null, null));

        return new SqlExplainQueryResult(query, explainQuery, "text", classification, rootNode, planPayload);
    }

    private static string? ExtractMySqlNodeType(string? nodeDescription)
    {
        if (string.IsNullOrWhiteSpace(nodeDescription))
        {
            return null;
        }

        if (nodeDescription.Contains(" on ", StringComparison.OrdinalIgnoreCase))
        {
            return nodeDescription[..nodeDescription.IndexOf(" on ", StringComparison.OrdinalIgnoreCase)];
        }

        if (nodeDescription.Contains(':', StringComparison.Ordinal))
        {
            return nodeDescription[..nodeDescription.IndexOf(':', StringComparison.Ordinal)];
        }

        return nodeDescription;
    }

    private static string? ExtractMySqlRelationName(string? nodeDescription)
    {
        if (string.IsNullOrWhiteSpace(nodeDescription) || !nodeDescription.Contains(" on ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var remainder = nodeDescription[(nodeDescription.IndexOf(" on ", StringComparison.OrdinalIgnoreCase) + 4)..];
        var endIndex = remainder.IndexOf(" using ", StringComparison.OrdinalIgnoreCase);
        return (endIndex >= 0 ? remainder[..endIndex] : remainder).Trim('`');
    }

    private static string? ExtractMySqlIndexName(string? nodeDescription)
    {
        if (string.IsNullOrWhiteSpace(nodeDescription) || !nodeDescription.Contains(" using ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var remainder = nodeDescription[(nodeDescription.IndexOf(" using ", StringComparison.OrdinalIgnoreCase) + 7)..];
        var endIndex = remainder.IndexOf(' ');
        return (endIndex >= 0 ? remainder[..endIndex] : remainder).Trim('`');
    }

    private static double? ParseDouble(string value) =>
        double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;

    private static decimal? ParseDecimal(string value) =>
        decimal.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;

    private static long? ParseLong(string value)
    {
        var number = ParseDouble(value);
        return number is null ? null : (long?)Math.Round(number.Value, MidpointRounding.AwayFromZero);
    }

    private static int? ParseInt(string value)
    {
        var number = ParseDouble(value);
        return number is null ? null : (int?)Math.Round(number.Value, MidpointRounding.AwayFromZero);
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
        var logger = loggerFactory?.CreateLogger("AIToolkit.Tools.Sql.MySql.Tools");
        if (logger is null || !logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        ToolInvocationLog(logger, toolName, JsonSerializer.Serialize(parameters, LogJsonOptions), null);
    }
}
