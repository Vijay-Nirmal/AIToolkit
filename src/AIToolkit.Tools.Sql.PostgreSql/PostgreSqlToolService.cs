using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AIToolkit.Tools.Sql.PostgreSql;

/// <summary>
/// Adapts the shared SQL abstractions into AI-callable PostgreSQL tool methods.
/// </summary>
/// <remarks>
/// This class is the PostgreSQL-specific orchestration layer between the shared abstractions and the public <c>pgsql_*</c> tool names. It
/// logs tool calls, translates optional database selection into <see cref="SqlConnectionTarget"/> values, converts provider exceptions into
/// stable tool result records, exposes PostgreSQL-specific extension discovery, and maps PostgreSQL JSON explain plans into shared result
/// models that other hosts can consume uniformly.
/// </remarks>
/// <param name="profileCatalog">Supplies the named PostgreSQL profiles visible to the model.</param>
/// <param name="metadataProvider">Reads database, schema, object, and routine metadata.</param>
/// <param name="queryExecutor">Executes classified queries through the shared execution pipeline.</param>
/// <param name="connectionOpener">Opens raw PostgreSQL connections for provider-specific commands.</param>
/// <param name="queryClassifier">Classifies PostgreSQL SQL text before execution or analysis.</param>
/// <param name="executionPolicy">Controls command timeout, mutation behavior, and result limits.</param>
internal sealed class PostgreSqlToolService(
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
        LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(1, "PostgreSqlToolInvocation"),
            "AI tool call {ToolName} with parameters {Parameters}");

    /// <summary>
    /// Lists the PostgreSQL connection profiles registered with the host.
    /// </summary>
    /// <param name="serviceProvider">Optional services used for logging.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    /// <returns>A tool result containing connection names and server summaries.</returns>
    public async Task<PostgreSqlListServersToolResult> ListServersAsync(
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(serviceProvider, "pgsql_list_servers", new Dictionary<string, object?>());

        try
        {
            var profiles = await _profileCatalog.ListProfilesAsync(cancellationToken).ConfigureAwait(false);
            return new PostgreSqlListServersToolResult(true, profiles);
        }
        catch (Exception exception)
        {
            return new PostgreSqlListServersToolResult(false, Array.Empty<SqlConnectionProfileSummary>(), exception.Message);
        }
    }

    /// <summary>
    /// Lists the databases visible to the selected PostgreSQL connection.
    /// </summary>
    /// <param name="connectionName">The logical PostgreSQL connection profile to inspect.</param>
    /// <param name="serviceProvider">Optional services used for logging.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    /// <returns>A tool result containing visible PostgreSQL database names.</returns>
    public async Task<PostgreSqlListDatabasesToolResult> ListDatabasesAsync(
        string connectionName,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(serviceProvider, "pgsql_list_databases", new Dictionary<string, object?> { ["connectionName"] = connectionName });

        try
        {
            var databases = await _metadataProvider.ListDatabasesAsync(CreateTarget(connectionName), cancellationToken).ConfigureAwait(false);
            return new PostgreSqlListDatabasesToolResult(true, databases.Select(static item => item.Name).ToArray());
        }
        catch (Exception exception)
        {
            return new PostgreSqlListDatabasesToolResult(false, Array.Empty<string>(), exception.Message);
        }
    }

    /// <summary>
    /// Lists the installed extensions in the selected PostgreSQL database.
    /// </summary>
    /// <param name="connectionName">The logical PostgreSQL connection profile to inspect.</param>
    /// <param name="database">An optional database override for the connection target.</param>
    /// <param name="serviceProvider">Optional services used for logging.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    /// <returns>A tool result containing installed extension metadata.</returns>
    public async Task<PostgreSqlListExtensionsToolResult> ListExtensionsAsync(
        string connectionName,
        string? database = null,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(serviceProvider, "pgsql_list_extensions", new Dictionary<string, object?> { ["connectionName"] = connectionName, ["database"] = database });

        try
        {
            var extensions = await ReadExtensionsAsync(CreateTarget(connectionName, database), cancellationToken).ConfigureAwait(false);
            return new PostgreSqlListExtensionsToolResult(true, extensions);
        }
        catch (Exception exception)
        {
            return new PostgreSqlListExtensionsToolResult(false, Array.Empty<PostgreSqlExtensionInfo>(), exception.Message);
        }
    }

    /// <summary>
    /// Lists the visible non-system schemas in the selected PostgreSQL database.
    /// </summary>
    /// <param name="connectionName">The logical PostgreSQL connection profile to inspect.</param>
    /// <param name="database">An optional database override for the connection target.</param>
    /// <param name="serviceProvider">Optional services used for logging.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    /// <returns>A tool result containing schema names.</returns>
    public async Task<PostgreSqlListSchemasToolResult> ListSchemasAsync(
        string connectionName,
        string? database = null,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(serviceProvider, "pgsql_list_schemas", new Dictionary<string, object?> { ["connectionName"] = connectionName, ["database"] = database });

        try
        {
            var schemas = await _metadataProvider.ListSchemasAsync(CreateTarget(connectionName, database), cancellationToken).ConfigureAwait(false);
            return new PostgreSqlListSchemasToolResult(true, schemas.Select(static item => item.Name).ToArray());
        }
        catch (Exception exception)
        {
            return new PostgreSqlListSchemasToolResult(false, Array.Empty<string>(), exception.Message);
        }
    }

    /// <summary>
    /// Lists the tables available in the selected PostgreSQL database.
    /// </summary>
    /// <param name="connectionName">The logical PostgreSQL connection profile to inspect.</param>
    /// <param name="database">An optional database override for the connection target.</param>
    /// <param name="serviceProvider">Optional services used for logging.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    /// <returns>A tool result containing fully qualified table names.</returns>
    public async Task<PostgreSqlListTablesToolResult> ListTablesAsync(
        string connectionName,
        string? database = null,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(serviceProvider, "pgsql_list_tables", new Dictionary<string, object?> { ["connectionName"] = connectionName, ["database"] = database });

        try
        {
            var tables = await _metadataProvider.ListTablesAsync(CreateTarget(connectionName, database), cancellationToken).ConfigureAwait(false);
            return new PostgreSqlListTablesToolResult(true, tables.Select(static item => item.Table.FullyQualifiedName).ToArray());
        }
        catch (Exception exception)
        {
            return new PostgreSqlListTablesToolResult(false, Array.Empty<string>(), exception.Message);
        }
    }

    /// <summary>
    /// Lists the views available in the selected PostgreSQL database.
    /// </summary>
    /// <param name="connectionName">The logical PostgreSQL connection profile to inspect.</param>
    /// <param name="database">An optional database override for the connection target.</param>
    /// <param name="serviceProvider">Optional services used for logging.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    /// <returns>A tool result containing fully qualified view names.</returns>
    public async Task<PostgreSqlListViewsToolResult> ListViewsAsync(
        string connectionName,
        string? database = null,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(serviceProvider, "pgsql_list_views", new Dictionary<string, object?> { ["connectionName"] = connectionName, ["database"] = database });

        try
        {
            var views = await _metadataProvider.ListViewsAsync(CreateTarget(connectionName, database), cancellationToken).ConfigureAwait(false);
            return new PostgreSqlListViewsToolResult(true, views.Select(static item => item.View.FullyQualifiedName).ToArray());
        }
        catch (Exception exception)
        {
            return new PostgreSqlListViewsToolResult(false, Array.Empty<string>(), exception.Message);
        }
    }

    /// <summary>
    /// Lists the functions available in the selected PostgreSQL database.
    /// </summary>
    /// <param name="connectionName">The logical PostgreSQL connection profile to inspect.</param>
    /// <param name="database">An optional database override for the connection target.</param>
    /// <param name="serviceProvider">Optional services used for logging.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    /// <returns>A tool result containing fully qualified PostgreSQL function names.</returns>
    public async Task<PostgreSqlListFunctionsToolResult> ListFunctionsAsync(
        string connectionName,
        string? database = null,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(serviceProvider, "pgsql_list_functions", new Dictionary<string, object?> { ["connectionName"] = connectionName, ["database"] = database });

        try
        {
            var functions = await _metadataProvider.ListFunctionsAsync(CreateTarget(connectionName, database), cancellationToken).ConfigureAwait(false);
            return new PostgreSqlListFunctionsToolResult(true, functions.Select(static item => item.Routine.FullyQualifiedName).ToArray());
        }
        catch (Exception exception)
        {
            return new PostgreSqlListFunctionsToolResult(false, Array.Empty<string>(), exception.Message);
        }
    }

    /// <summary>
    /// Lists the procedures available in the selected PostgreSQL database.
    /// </summary>
    /// <param name="connectionName">The logical PostgreSQL connection profile to inspect.</param>
    /// <param name="database">An optional database override for the connection target.</param>
    /// <param name="serviceProvider">Optional services used for logging.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    /// <returns>A tool result containing fully qualified PostgreSQL procedure names.</returns>
    public async Task<PostgreSqlListProceduresToolResult> ListProceduresAsync(
        string connectionName,
        string? database = null,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(serviceProvider, "pgsql_list_procedures", new Dictionary<string, object?> { ["connectionName"] = connectionName, ["database"] = database });

        try
        {
            var procedures = await _metadataProvider.ListProceduresAsync(CreateTarget(connectionName, database), cancellationToken).ConfigureAwait(false);
            return new PostgreSqlListProceduresToolResult(true, procedures.Select(static item => item.Routine.FullyQualifiedName).ToArray());
        }
        catch (Exception exception)
        {
            return new PostgreSqlListProceduresToolResult(false, Array.Empty<string>(), exception.Message);
        }
    }

    /// <summary>
    /// Gets the PostgreSQL definition for a table, view, function, or procedure.
    /// </summary>
    /// <param name="connectionName">The logical PostgreSQL connection profile to inspect.</param>
    /// <param name="objectName">The object name to resolve.</param>
    /// <param name="schemaName">An optional schema name. When omitted, PostgreSQL defaults to <c>public</c>.</param>
    /// <param name="objectKind">An optional object-kind hint such as <c>Table</c> or <c>Procedure</c>.</param>
    /// <param name="database">An optional database override for the connection target.</param>
    /// <param name="serviceProvider">Optional services used for logging.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    /// <returns>A tool result containing the resolved object definition.</returns>
    public async Task<PostgreSqlGetObjectDefinitionToolResult> GetObjectDefinitionAsync(
        string connectionName,
        string objectName,
        string? schemaName = null,
        string? objectKind = null,
        string? database = null,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(
            serviceProvider,
            "pgsql_get_object_definition",
            new Dictionary<string, object?>
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
            var definition = await _metadataProvider
                .GetObjectDefinitionAsync(CreateTarget(connectionName, database), schemaName, objectName, kind, cancellationToken)
                .ConfigureAwait(false);

            return new PostgreSqlGetObjectDefinitionToolResult(true, definition);
        }
        catch (Exception exception)
        {
            return new PostgreSqlGetObjectDefinitionToolResult(false, null, exception.Message);
        }
    }

    /// <summary>
    /// Executes PostgreSQL SQL text through the shared execution pipeline.
    /// </summary>
    /// <param name="connectionName">The logical PostgreSQL connection profile to use.</param>
    /// <param name="query">The SQL text to classify, approve if required, and execute.</param>
    /// <param name="database">An optional database override for the connection target.</param>
    /// <param name="serviceProvider">Optional services used for logging.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    /// <returns>A tool result containing the execution classification, any result sets, and provider messages.</returns>
    public async Task<PostgreSqlRunQueryToolResult> RunQueryAsync(
        string connectionName,
        string query,
        string? database = null,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(
            serviceProvider,
            "pgsql_run_query",
            new Dictionary<string, object?>
            {
                ["connectionName"] = connectionName,
                ["database"] = database,
                ["query"] = query,
            });

        try
        {
            var result = await _queryExecutor.ExecuteAsync(CreateTarget(connectionName, database), query, cancellationToken).ConfigureAwait(false);
            return new PostgreSqlRunQueryToolResult(result.Success, result, result.Message);
        }
        catch (Exception exception)
        {
            return new PostgreSqlRunQueryToolResult(false, null, exception.Message);
        }
    }

    /// <summary>
    /// Analyzes a read-only PostgreSQL query with JSON-formatted <c>EXPLAIN ANALYZE</c>.
    /// </summary>
    /// <param name="connectionName">The logical PostgreSQL connection profile to use.</param>
    /// <param name="query">The read-only SQL statement to analyze.</param>
    /// <param name="database">An optional database override for the connection target.</param>
    /// <param name="serviceProvider">Optional services used for logging.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    /// <returns>A tool result containing the parsed PostgreSQL plan summary and raw JSON plan payload.</returns>
    /// <remarks>
    /// PostgreSQL exposes detailed timing, buffer, and WAL statistics through JSON-formatted explain plans. This method keeps the raw payload
    /// and also projects a compact root-node summary into the provider-neutral explain models.
    /// </remarks>
    public async Task<PostgreSqlExplainQueryToolResult> ExplainQueryAsync(
        string connectionName,
        string query,
        string? database = null,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(
            serviceProvider,
            "pgsql_explain_query",
            new Dictionary<string, object?>
            {
                ["connectionName"] = connectionName,
                ["database"] = database,
                ["query"] = query,
            });

        try
        {
            var classification = _queryClassifier.Classify(query);
            if (classification.Safety != SqlStatementSafety.ReadOnly)
            {
                return new PostgreSqlExplainQueryToolResult(
                    false,
                    null,
                    classification.Reason ?? "Only read-only PostgreSQL queries can be analyzed with pgsql_explain_query.");
            }

            if (classification.StatementTypes.Contains("EXPLAIN", StringComparer.Ordinal))
            {
                return new PostgreSqlExplainQueryToolResult(
                    false,
                    null,
                    "Provide the underlying PostgreSQL query instead of an EXPLAIN statement.");
            }

            var explainQuery = BuildExplainQuery(query);
            var planPayload = await ExecuteExplainQueryAsync(CreateTarget(connectionName, database), explainQuery, cancellationToken).ConfigureAwait(false);
            var result = CreateExplainResult(query, explainQuery, classification, planPayload);
            return new PostgreSqlExplainQueryToolResult(true, result);
        }
        catch (Exception exception)
        {
            return new PostgreSqlExplainQueryToolResult(false, null, exception.Message);
        }
    }

    private static SqlObjectKind ParseObjectKind(string? objectKind) =>
        string.IsNullOrWhiteSpace(objectKind)
            ? SqlObjectKind.Unknown
            : Enum.TryParse<SqlObjectKind>(objectKind, ignoreCase: true, out var kind)
                ? kind
                : SqlObjectKind.Unknown;

    private async Task<IReadOnlyList<PostgreSqlExtensionInfo>> ReadExtensionsAsync(
        SqlConnectionTarget target,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionOpener.OpenConnectionAsync(target, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = PostgreSqlCatalogQueries.ListExtensions;
        command.CommandTimeout = Math.Max(1, _executionPolicy.CommandTimeoutSeconds);

        var items = new List<PostgreSqlExtensionInfo>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var name = reader.IsDBNull(0) ? string.Empty : Convert.ToString(reader.GetValue(0), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
            var version = reader.IsDBNull(1) ? string.Empty : Convert.ToString(reader.GetValue(1), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
            var schema = reader.IsDBNull(2) ? string.Empty : Convert.ToString(reader.GetValue(2), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
            var description = reader.IsDBNull(3) ? null : Convert.ToString(reader.GetValue(3), System.Globalization.CultureInfo.InvariantCulture);
            items.Add(new PostgreSqlExtensionInfo(name, version, schema, description));
        }

        return items;
    }

    private async Task<string> ExecuteExplainQueryAsync(
        SqlConnectionTarget target,
        string explainQuery,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionOpener.OpenConnectionAsync(target, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = explainQuery;
        command.CommandTimeout = Math.Max(1, _executionPolicy.CommandTimeoutSeconds);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false) || reader.IsDBNull(0))
        {
            throw new InvalidOperationException("PostgreSQL did not return an execution plan.");
        }

        return Convert.ToString(reader.GetValue(0), System.Globalization.CultureInfo.InvariantCulture)
            ?? throw new InvalidOperationException("PostgreSQL returned an empty execution plan.");
    }

    private static string BuildExplainQuery(string query) =>
        $"EXPLAIN (ANALYZE TRUE, VERBOSE TRUE, BUFFERS TRUE, SETTINGS TRUE, WAL TRUE, FORMAT JSON) {query.Trim().TrimEnd(';')};";

    private static SqlExplainQueryResult CreateExplainResult(
        string query,
        string explainQuery,
        SqlQueryClassification classification,
        string planPayload)
    {
        using var document = JsonDocument.Parse(planPayload);
        if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("PostgreSQL returned an execution plan in an unexpected format.");
        }

        var explainRoot = document.RootElement[0];
        // PostgreSQL returns a nested JSON payload, so the parser lifts key root-node metrics into provider-neutral summary records.
        var planElement = explainRoot.TryGetProperty("Plan", out var node) ? node : default;
        var timing = new SqlExplainTimingStatistics(
            GetDouble(explainRoot, "Planning Time"),
            GetDouble(explainRoot, "Execution Time"),
            null,
            null,
            GetDouble(planElement, "Actual Startup Time"),
            GetDouble(planElement, "Actual Total Time"),
            GetDouble(planElement, "Actual Rows"),
            GetInt(planElement, "Actual Loops"));

        var buffers = new SqlExplainBufferStatistics(
            GetLong(planElement, "Shared Hit Blocks"),
            GetLong(planElement, "Shared Read Blocks"),
            GetLong(planElement, "Shared Dirtied Blocks"),
            GetLong(planElement, "Shared Written Blocks"),
            GetLong(planElement, "Local Hit Blocks"),
            GetLong(planElement, "Local Read Blocks"),
            GetLong(planElement, "Local Dirtied Blocks"),
            GetLong(planElement, "Local Written Blocks"),
            GetLong(planElement, "Temp Read Blocks"),
            GetLong(planElement, "Temp Written Blocks"));

        var wal = new SqlExplainWalStatistics(
            GetLong(planElement, "WAL Records"),
            GetLong(planElement, "WAL FPI"),
            GetLong(planElement, "WAL Bytes"));

        var rootNode = planElement.ValueKind == JsonValueKind.Object
            ? new SqlExplainNodeSummary(
                GetString(planElement, "Node Type"),
                GetString(planElement, "Relation Name"),
                GetString(planElement, "Schema"),
                GetString(planElement, "Alias"),
                GetString(planElement, "Index Name"),
                GetString(planElement, "Join Type"),
                GetString(planElement, "Strategy"),
                GetDecimal(planElement, "Startup Cost"),
                GetDecimal(planElement, "Total Cost"),
                GetLong(planElement, "Plan Rows"),
                GetInt(planElement, "Plan Width"),
                GetInt(planElement, "Workers Planned"),
                GetInt(planElement, "Workers Launched"),
                GetString(planElement, "Filter"),
                GetString(planElement, "Index Cond"),
                GetString(planElement, "Hash Cond"),
                GetString(planElement, "Merge Cond"),
                timing,
                buffers,
                wal)
            : null;

        return new SqlExplainQueryResult(query, explainQuery, "json", classification, rootNode, planPayload);
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                JsonValueKind.True => bool.TrueString,
                JsonValueKind.False => bool.FalseString,
                _ => value.GetRawText(),
            }
            : null;

    private static int? GetInt(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var number))
        {
            return number;
        }

        return null;
    }

    private static long? GetLong(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var value) && value.TryGetInt64(out var number))
        {
            return number;
        }

        return null;
    }

    private static double? GetDouble(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var value) && value.TryGetDouble(out var number))
        {
            return number;
        }

        return null;
    }

    private static decimal? GetDecimal(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var value) && value.TryGetDecimal(out var number))
        {
            return number;
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

    private static void LogToolInvocation(
        IServiceProvider? serviceProvider,
        string toolName,
        IReadOnlyDictionary<string, object?> parameters)
    {
        var loggerFactory = serviceProvider?.GetService(typeof(ILoggerFactory)) as ILoggerFactory;
        var logger = loggerFactory?.CreateLogger("AIToolkit.Tools.Sql.PostgreSql.Tools");
        if (logger is null || !logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        ToolInvocationLog(logger, toolName, JsonSerializer.Serialize(parameters, LogJsonOptions), null);
    }
}
