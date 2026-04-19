using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;

namespace AIToolkit.Tools.Sql.SqlServer;

/// <summary>
/// Adapts the shared SQL abstractions into AI-callable tool methods with SQL Server-specific tool names and result shapes.
/// </summary>
/// <remarks>
/// This type also centralizes tool-call logging and the conversion from exceptions into tool-friendly failure payloads so those concerns do not
/// leak into the metadata and query components.
/// </remarks>
/// <param name="profileCatalog">Supplies the named SQL Server profiles visible to the model.</param>
/// <param name="metadataProvider">Reads database, schema, object, and routine metadata.</param>
/// <param name="queryExecutor">Executes classified queries through the shared execution pipeline.</param>
/// <param name="connectionOpener">Opens raw SQL Server connections for provider-specific commands.</param>
/// <param name="queryClassifier">Classifies T-SQL text before execution or analysis.</param>
/// <param name="executionPolicy">Controls command timeout, mutation behavior, and result limits.</param>
internal sealed class SqlServerToolService(
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
            new EventId(1, "SqlToolInvocation"),
            "AI tool call {ToolName} with parameters {Parameters}");

    /// <summary>
    /// Lists the SQL Server connection profiles registered with the host.
    /// </summary>
    /// <param name="serviceProvider">Optional services used for logging.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    /// <returns>A tool result containing connection names and server summaries.</returns>
    public async Task<SqlServerListServersToolResult> ListServersAsync(
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(serviceProvider, "mssql_list_servers", new Dictionary<string, object?>());

        try
        {
            var profiles = await _profileCatalog.ListProfilesAsync(cancellationToken).ConfigureAwait(false);
            return new SqlServerListServersToolResult(true, profiles);
        }
        catch (Exception exception)
        {
            return new SqlServerListServersToolResult(false, Array.Empty<SqlConnectionProfileSummary>(), exception.Message);
        }
    }

    /// <summary>
    /// Lists the databases visible to the selected SQL Server connection.
    /// </summary>
    /// <param name="connectionName">The logical SQL Server connection profile to inspect.</param>
    /// <param name="serviceProvider">Optional services used for logging.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    /// <returns>A tool result containing visible SQL Server database names.</returns>
    public async Task<SqlServerListDatabasesToolResult> ListDatabasesAsync(
        string connectionName,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(
            serviceProvider,
            "mssql_list_databases",
            new Dictionary<string, object?>
            {
                ["connectionName"] = connectionName,
            });

        try
        {
            var databases = await _metadataProvider.ListDatabasesAsync(CreateTarget(connectionName), cancellationToken).ConfigureAwait(false);
            return new SqlServerListDatabasesToolResult(true, databases.Select(static item => item.Name).ToArray());
        }
        catch (Exception exception)
        {
            return new SqlServerListDatabasesToolResult(false, Array.Empty<string>(), exception.Message);
        }
    }

    /// <summary>
    /// Lists the schemas available in the selected SQL Server database.
    /// </summary>
    /// <param name="connectionName">The logical SQL Server connection profile to inspect.</param>
    /// <param name="database">An optional database override for the connection target.</param>
    /// <param name="serviceProvider">Optional services used for logging.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    /// <returns>A tool result containing schema names.</returns>
    public async Task<SqlServerListSchemasToolResult> ListSchemasAsync(
        string connectionName,
        string? database = null,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(
            serviceProvider,
            "mssql_list_schemas",
            new Dictionary<string, object?>
            {
                ["connectionName"] = connectionName,
                ["database"] = database,
            });

        try
        {
            var schemas = await _metadataProvider.ListSchemasAsync(CreateTarget(connectionName, database), cancellationToken).ConfigureAwait(false);
            return new SqlServerListSchemasToolResult(true, schemas.Select(static item => item.Name).ToArray());
        }
        catch (Exception exception)
        {
            return new SqlServerListSchemasToolResult(false, Array.Empty<string>(), exception.Message);
        }
    }

    /// <summary>
    /// Lists the tables available in the selected SQL Server database.
    /// </summary>
    /// <param name="connectionName">The logical SQL Server connection profile to inspect.</param>
    /// <param name="database">An optional database override for the connection target.</param>
    /// <param name="serviceProvider">Optional services used for logging.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    /// <returns>A tool result containing fully qualified table names.</returns>
    public async Task<SqlServerListTablesToolResult> ListTablesAsync(
        string connectionName,
        string? database = null,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(
            serviceProvider,
            "mssql_list_tables",
            new Dictionary<string, object?>
            {
                ["connectionName"] = connectionName,
                ["database"] = database,
            });

        try
        {
            var tables = await _metadataProvider.ListTablesAsync(CreateTarget(connectionName, database), cancellationToken).ConfigureAwait(false);
            return new SqlServerListTablesToolResult(true, tables.Select(static item => item.Table.FullyQualifiedName).ToArray());
        }
        catch (Exception exception)
        {
            return new SqlServerListTablesToolResult(false, Array.Empty<string>(), exception.Message);
        }
    }

    /// <summary>
    /// Lists the views available in the selected SQL Server database.
    /// </summary>
    /// <param name="connectionName">The logical SQL Server connection profile to inspect.</param>
    /// <param name="database">An optional database override for the connection target.</param>
    /// <param name="serviceProvider">Optional services used for logging.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    /// <returns>A tool result containing fully qualified view names.</returns>
    public async Task<SqlServerListViewsToolResult> ListViewsAsync(
        string connectionName,
        string? database = null,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(
            serviceProvider,
            "mssql_list_views",
            new Dictionary<string, object?>
            {
                ["connectionName"] = connectionName,
                ["database"] = database,
            });

        try
        {
            var views = await _metadataProvider.ListViewsAsync(CreateTarget(connectionName, database), cancellationToken).ConfigureAwait(false);
            return new SqlServerListViewsToolResult(true, views.Select(static item => item.View.FullyQualifiedName).ToArray());
        }
        catch (Exception exception)
        {
            return new SqlServerListViewsToolResult(false, Array.Empty<string>(), exception.Message);
        }
    }

    /// <summary>
    /// Lists the functions available in the selected SQL Server database.
    /// </summary>
    /// <param name="connectionName">The logical SQL Server connection profile to inspect.</param>
    /// <param name="database">An optional database override for the connection target.</param>
    /// <param name="serviceProvider">Optional services used for logging.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    /// <returns>A tool result containing fully qualified SQL Server function names.</returns>
    public async Task<SqlServerListFunctionsToolResult> ListFunctionsAsync(
        string connectionName,
        string? database = null,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(
            serviceProvider,
            "mssql_list_functions",
            new Dictionary<string, object?>
            {
                ["connectionName"] = connectionName,
                ["database"] = database,
            });

        try
        {
            var functions = await _metadataProvider.ListFunctionsAsync(CreateTarget(connectionName, database), cancellationToken).ConfigureAwait(false);
            return new SqlServerListFunctionsToolResult(true, functions.Select(static item => item.Routine.FullyQualifiedName).ToArray());
        }
        catch (Exception exception)
        {
            return new SqlServerListFunctionsToolResult(false, Array.Empty<string>(), exception.Message);
        }
    }

    /// <summary>
    /// Lists the procedures available in the selected SQL Server database.
    /// </summary>
    /// <param name="connectionName">The logical SQL Server connection profile to inspect.</param>
    /// <param name="database">An optional database override for the connection target.</param>
    /// <param name="serviceProvider">Optional services used for logging.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    /// <returns>A tool result containing fully qualified SQL Server procedure names.</returns>
    public async Task<SqlServerListProceduresToolResult> ListProceduresAsync(
        string connectionName,
        string? database = null,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(
            serviceProvider,
            "mssql_list_procedures",
            new Dictionary<string, object?>
            {
                ["connectionName"] = connectionName,
                ["database"] = database,
            });

        try
        {
            var procedures = await _metadataProvider.ListProceduresAsync(CreateTarget(connectionName, database), cancellationToken).ConfigureAwait(false);
            return new SqlServerListProceduresToolResult(true, procedures.Select(static item => item.Routine.FullyQualifiedName).ToArray());
        }
        catch (Exception exception)
        {
            return new SqlServerListProceduresToolResult(false, Array.Empty<string>(), exception.Message);
        }
    }

    /// <summary>
    /// Gets the SQL Server definition for a table, view, function, or procedure.
    /// </summary>
    /// <param name="connectionName">The logical SQL Server connection profile to inspect.</param>
    /// <param name="objectName">The object name to resolve.</param>
    /// <param name="schemaName">An optional schema name. When omitted, SQL Server defaults to <c>dbo</c>.</param>
    /// <param name="objectKind">An optional object-kind hint such as <c>Table</c> or <c>Procedure</c>.</param>
    /// <param name="database">An optional database override for the connection target.</param>
    /// <param name="serviceProvider">Optional services used for logging.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    /// <returns>A tool result containing the resolved object definition.</returns>
    public async Task<SqlServerGetObjectDefinitionToolResult> GetObjectDefinitionAsync(
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
            "mssql_get_object_definition",
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

            return new SqlServerGetObjectDefinitionToolResult(true, definition);
        }
        catch (Exception exception)
        {
            return new SqlServerGetObjectDefinitionToolResult(false, null, exception.Message);
        }
    }

    /// <summary>
    /// Executes T-SQL text through the shared execution pipeline.
    /// </summary>
    /// <param name="connectionName">The logical SQL Server connection profile to use.</param>
    /// <param name="query">The SQL text to classify, approve if required, and execute.</param>
    /// <param name="database">An optional database override for the connection target.</param>
    /// <param name="serviceProvider">Optional services used for logging.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    /// <returns>A tool result containing the execution classification, any result sets, and provider messages.</returns>
    public async Task<SqlServerRunQueryToolResult> RunQueryAsync(
        string connectionName,
        string query,
        string? database = null,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(
            serviceProvider,
            "mssql_run_query",
            new Dictionary<string, object?>
            {
                ["connectionName"] = connectionName,
                ["database"] = database,
                ["query"] = query,
            });

        try
        {
            var result = await _queryExecutor.ExecuteAsync(CreateTarget(connectionName, database), query, cancellationToken).ConfigureAwait(false);
            return new SqlServerRunQueryToolResult(result.Success, result, result.Message);
        }
        catch (Exception exception)
        {
            return new SqlServerRunQueryToolResult(false, null, exception.Message);
        }
    }

    /// <summary>
    /// Analyzes a read-only SQL Server query with <c>SET STATISTICS XML ON</c>.
    /// </summary>
    /// <param name="connectionName">The logical SQL Server connection profile to use.</param>
    /// <param name="query">The read-only SQL statement to analyze.</param>
    /// <param name="database">An optional database override for the connection target.</param>
    /// <param name="serviceProvider">Optional services used for logging.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    /// <returns>A tool result containing the parsed SQL Server plan summary and raw XML plan payload.</returns>
    /// <remarks>
    /// SQL Server emits execution plans as ShowPlan XML. This method captures the raw XML and also projects key statement, operator, and
    /// runtime counter data into the shared explain-plan models for easier cross-provider consumption.
    /// </remarks>
    public async Task<SqlServerExplainQueryToolResult> ExplainQueryAsync(
        string connectionName,
        string query,
        string? database = null,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(
            serviceProvider,
            "mssql_explain_query",
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
                return new SqlServerExplainQueryToolResult(false, null, classification.Reason ?? "Only read-only SQL Server queries can be analyzed with mssql_explain_query.");
            }

            var normalized = query.ToUpperInvariant();
            if (normalized.Contains("STATISTICS XML", StringComparison.Ordinal) || normalized.Contains("SHOWPLAN_XML", StringComparison.Ordinal))
            {
                return new SqlServerExplainQueryToolResult(false, null, "Provide the underlying SQL query instead of a SHOWPLAN or STATISTICS XML statement.");
            }

            var explainQuery = BuildExplainQuery(query);
            var planPayload = await ExecuteExplainQueryAsync(CreateTarget(connectionName, database), explainQuery, cancellationToken).ConfigureAwait(false);
            var result = CreateExplainResult(query, explainQuery, classification, planPayload);
            return new SqlServerExplainQueryToolResult(true, result);
        }
        catch (Exception exception)
        {
            return new SqlServerExplainQueryToolResult(false, null, exception.Message);
        }
    }

    private static SqlObjectKind ParseObjectKind(string? objectKind) =>
        string.IsNullOrWhiteSpace(objectKind)
            ? SqlObjectKind.Unknown
            : Enum.TryParse<SqlObjectKind>(objectKind, ignoreCase: true, out var kind)
                ? kind
                : SqlObjectKind.Unknown;

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
        string? payload = null;

        do
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (reader.FieldCount == 0 || reader.IsDBNull(0))
                {
                    continue;
                }

                var value = Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(value) && value.Contains("<ShowPlanXML", StringComparison.Ordinal))
                {
                    payload = value;
                }
            }
        }
        while (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false));

        return payload ?? throw new InvalidOperationException("SQL Server did not return a STATISTICS XML plan. Ensure the login has SHOWPLAN permission.");
    }

    private static string BuildExplainQuery(string query) =>
        $"SET STATISTICS XML ON; {query.Trim().TrimEnd(';')}; SET STATISTICS XML OFF;";

    private static SqlExplainQueryResult CreateExplainResult(
        string query,
        string explainQuery,
        SqlQueryClassification classification,
        string planPayload)
    {
        var document = XDocument.Parse(planPayload, LoadOptions.PreserveWhitespace);
        var root = document.Root ?? throw new InvalidOperationException("SQL Server returned an empty execution plan.");
        var ns = root.Name.Namespace;

        // SQL Server nests the useful plan details across statement, query-plan, and relop elements, so the parser walks multiple levels.
        var statement = root
            .Descendants()
            .FirstOrDefault(static element => element.Name.LocalName.StartsWith("Stmt", StringComparison.Ordinal));
        var queryPlan = statement?.Element(ns + "QueryPlan") ?? root.Descendants(ns + "QueryPlan").FirstOrDefault();
        var relOp = queryPlan?.Descendants(ns + "RelOp").FirstOrDefault();
        var queryTimeStats = statement?.Element(ns + "QueryTimeStats") ?? root.Descendants(ns + "QueryTimeStats").FirstOrDefault();
        var objectElement = relOp?.Descendants(ns + "Object").FirstOrDefault();
        var runtimeCounters = relOp?.Descendants(ns + "RunTimeCountersPerThread").ToArray() ?? Array.Empty<XElement>();

        var actualRows = SumDoubleAttributes(runtimeCounters, "ActualRows");
        var actualExecutions = SumIntAttributes(runtimeCounters, "ActualExecutions");
        var actualElapsed = MaxDoubleAttribute(runtimeCounters, "ActualElapsedms") ?? GetDoubleAttribute(queryTimeStats, "ElapsedTime");

        var timing = new SqlExplainTimingStatistics(
            PlanningTimeMs: GetDoubleAttribute(queryPlan, "CompileTime"),
            ExecutionTimeMs: GetDoubleAttribute(queryTimeStats, "ElapsedTime"),
            PlanningCpuTimeMs: GetDoubleAttribute(queryPlan, "CompileCPU"),
            ExecutionCpuTimeMs: GetDoubleAttribute(queryTimeStats, "CpuTime"),
            ActualStartupTimeMs: null,
            ActualTotalTimeMs: actualElapsed,
            ActualRows: actualRows,
            ActualLoops: actualExecutions);

        var rootNode = new SqlExplainNodeSummary(
            NodeType: GetAttribute(relOp, "PhysicalOp") ?? GetAttribute(relOp, "LogicalOp"),
            RelationName: CleanSqlServerObjectName(GetAttribute(objectElement, "Table")),
            Schema: CleanSqlServerObjectName(GetAttribute(objectElement, "Schema")),
            Alias: null,
            IndexName: CleanSqlServerObjectName(GetAttribute(objectElement, "Index")),
            JoinType: GetAttribute(relOp, "LogicalOp"),
            Strategy: null,
            StartupCost: null,
            TotalCost: GetDecimalAttribute(statement, "StatementSubTreeCost") ?? GetDecimalAttribute(relOp, "EstimatedTotalSubtreeCost"),
            PlanRows: GetLongRoundedAttribute(statement, "StatementEstRows") ?? GetLongRoundedAttribute(relOp, "EstimateRows"),
            PlanWidth: GetIntRoundedAttribute(relOp, "AvgRowSize"),
            WorkersPlanned: GetIntAttribute(queryPlan, "DegreeOfParallelism"),
            WorkersLaunched: null,
            Filter: relOp?.Descendants(ns + "Predicate").Attributes("ScalarString").Select(static item => item.Value).FirstOrDefault(),
            IndexCondition: relOp?.Descendants(ns + "SeekPredicateNew").Attributes("ScalarString").Select(static item => item.Value).FirstOrDefault(),
            HashCondition: null,
            MergeCondition: null,
            Timing: timing,
            Buffers: new SqlExplainBufferStatistics(null, null, null, null, null, null, null, null, null, null),
            Wal: new SqlExplainWalStatistics(null, null, null));

        return new SqlExplainQueryResult(query, explainQuery, "xml", classification, rootNode, planPayload);
    }

    private static string? GetAttribute(XElement? element, string attributeName) => element?.Attribute(attributeName)?.Value;

    private static double? GetDoubleAttribute(XElement? element, string attributeName)
    {
        var value = GetAttribute(element, attributeName);
        return double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
    }

    private static decimal? GetDecimalAttribute(XElement? element, string attributeName)
    {
        var value = GetAttribute(element, attributeName);
        return decimal.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
    }

    private static int? GetIntAttribute(XElement? element, string attributeName)
    {
        var value = GetAttribute(element, attributeName);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
    }

    private static long? GetLongRoundedAttribute(XElement? element, string attributeName)
    {
        var number = GetDoubleAttribute(element, attributeName);
        return number is null ? null : (long?)Math.Round(number.Value, MidpointRounding.AwayFromZero);
    }

    private static int? GetIntRoundedAttribute(XElement? element, string attributeName)
    {
        var number = GetDoubleAttribute(element, attributeName);
        return number is null ? null : (int?)Math.Round(number.Value, MidpointRounding.AwayFromZero);
    }

    private static double? SumDoubleAttributes(IEnumerable<XElement> elements, string attributeName)
    {
        var values = elements
            .Select(element => GetDoubleAttribute(element, attributeName))
            .Where(static value => value is not null)
            .Select(static value => value!.Value)
            .ToArray();

        return values.Length == 0 ? null : values.Sum();
    }

    private static int? SumIntAttributes(IEnumerable<XElement> elements, string attributeName)
    {
        var values = elements
            .Select(element => GetIntAttribute(element, attributeName))
            .Where(static value => value is not null)
            .Select(static value => value!.Value)
            .ToArray();

        return values.Length == 0 ? null : values.Sum();
    }

    private static double? MaxDoubleAttribute(IEnumerable<XElement> elements, string attributeName)
    {
        var values = elements
            .Select(element => GetDoubleAttribute(element, attributeName))
            .Where(static value => value is not null)
            .Select(static value => value!.Value)
            .ToArray();

        return values.Length == 0 ? null : values.Max();
    }

    private static string? CleanSqlServerObjectName(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? value
            : value.Replace("[", string.Empty, StringComparison.Ordinal).Replace("]", string.Empty, StringComparison.Ordinal);

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
        var logger = loggerFactory?.CreateLogger("AIToolkit.Tools.Sql.SqlServer.Tools");
        if (logger is null || !logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        ToolInvocationLog(
            logger,
            toolName,
            JsonSerializer.Serialize(parameters, LogJsonOptions),
            null);
    }
}
