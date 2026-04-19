namespace AIToolkit.Tools.Sql.SqlServer;

/// <summary>
/// Represents the payload returned by <c>mssql_list_servers</c>.
/// </summary>
/// <param name="Success">Indicates whether the tool completed successfully.</param>
/// <param name="Servers">The registered SQL Server connection profiles visible to the caller.</param>
/// <param name="Message">An optional diagnostic message.</param>
internal sealed record SqlServerListServersToolResult(
    bool Success,
    IReadOnlyList<SqlConnectionProfileSummary> Servers,
    string? Message = null)
    : SqlToolResult(Success, Message);

/// <summary>
/// Represents the payload returned by <c>mssql_list_databases</c>.
/// </summary>
/// <param name="Success">Indicates whether the tool completed successfully.</param>
/// <param name="Databases">The visible SQL Server databases.</param>
/// <param name="Message">An optional diagnostic message.</param>
internal sealed record SqlServerListDatabasesToolResult(
    bool Success,
    IReadOnlyList<string> Databases,
    string? Message = null)
    : SqlToolResult(Success, Message);

/// <summary>
/// Represents the payload returned by <c>mssql_list_schemas</c>.
/// </summary>
/// <param name="Success">Indicates whether the tool completed successfully.</param>
/// <param name="Schemas">The visible SQL Server schemas.</param>
/// <param name="Message">An optional diagnostic message.</param>
internal sealed record SqlServerListSchemasToolResult(
    bool Success,
    IReadOnlyList<string> Schemas,
    string? Message = null)
    : SqlToolResult(Success, Message);

/// <summary>
/// Represents the payload returned by <c>mssql_list_tables</c>.
/// </summary>
/// <param name="Success">Indicates whether the tool completed successfully.</param>
/// <param name="Tables">The fully qualified table names in the selected database.</param>
/// <param name="Message">An optional diagnostic message.</param>
internal sealed record SqlServerListTablesToolResult(
    bool Success,
    IReadOnlyList<string> Tables,
    string? Message = null)
    : SqlToolResult(Success, Message);

/// <summary>
/// Represents the payload returned by <c>mssql_list_views</c>.
/// </summary>
/// <param name="Success">Indicates whether the tool completed successfully.</param>
/// <param name="Views">The fully qualified view names in the selected database.</param>
/// <param name="Message">An optional diagnostic message.</param>
internal sealed record SqlServerListViewsToolResult(
    bool Success,
    IReadOnlyList<string> Views,
    string? Message = null)
    : SqlToolResult(Success, Message);

/// <summary>
/// Represents the payload returned by <c>mssql_list_functions</c>.
/// </summary>
/// <param name="Success">Indicates whether the tool completed successfully.</param>
/// <param name="Functions">The fully qualified SQL Server function names.</param>
/// <param name="Message">An optional diagnostic message.</param>
internal sealed record SqlServerListFunctionsToolResult(
    bool Success,
    IReadOnlyList<string> Functions,
    string? Message = null)
    : SqlToolResult(Success, Message);

/// <summary>
/// Represents the payload returned by <c>mssql_list_procedures</c>.
/// </summary>
/// <param name="Success">Indicates whether the tool completed successfully.</param>
/// <param name="Procedures">The fully qualified SQL Server procedure names.</param>
/// <param name="Message">An optional diagnostic message.</param>
internal sealed record SqlServerListProceduresToolResult(
    bool Success,
    IReadOnlyList<string> Procedures,
    string? Message = null)
    : SqlToolResult(Success, Message);

/// <summary>
/// Represents the payload returned by <c>mssql_get_object_definition</c>.
/// </summary>
/// <param name="Success">Indicates whether the tool completed successfully.</param>
/// <param name="Definition">The resolved table, view, function, or procedure definition.</param>
/// <param name="Message">An optional diagnostic message.</param>
internal sealed record SqlServerGetObjectDefinitionToolResult(
    bool Success,
    SqlObjectDefinition? Definition,
    string? Message = null)
    : SqlToolResult(Success, Message);

/// <summary>
/// Represents the payload returned by <c>mssql_run_query</c>.
/// </summary>
/// <param name="Success">Indicates whether the tool completed successfully.</param>
/// <param name="Result">The classified query execution result.</param>
/// <param name="Message">An optional diagnostic message.</param>
internal sealed record SqlServerRunQueryToolResult(
    bool Success,
    SqlExecuteQueryResult? Result,
    string? Message = null)
    : SqlToolResult(Success, Message);

/// <summary>
/// Represents the payload returned by <c>mssql_explain_query</c>.
/// </summary>
/// <param name="Success">Indicates whether the tool completed successfully.</param>
/// <param name="Result">The explain-plan summary produced for the query.</param>
/// <param name="Message">An optional diagnostic message.</param>
internal sealed record SqlServerExplainQueryToolResult(
    bool Success,
    SqlExplainQueryResult? Result,
    string? Message = null)
    : SqlToolResult(Success, Message);
