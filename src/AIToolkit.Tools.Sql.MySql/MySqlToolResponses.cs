namespace AIToolkit.Tools.Sql.MySql;

/// <summary>
/// Represents the payload returned by <c>mysql_list_servers</c>.
/// </summary>
/// <param name="Success">Indicates whether the tool completed successfully.</param>
/// <param name="Servers">The registered MySQL connection profiles visible to the caller.</param>
/// <param name="Message">An optional diagnostic message.</param>
internal sealed record MySqlListServersToolResult(bool Success, IReadOnlyList<SqlConnectionProfileSummary> Servers, string? Message = null) : SqlToolResult(Success, Message);

/// <summary>
/// Represents the payload returned by <c>mysql_list_databases</c>.
/// </summary>
/// <param name="Success">Indicates whether the tool completed successfully.</param>
/// <param name="Databases">The visible MySQL databases.</param>
/// <param name="Message">An optional diagnostic message.</param>
internal sealed record MySqlListDatabasesToolResult(bool Success, IReadOnlyList<string> Databases, string? Message = null) : SqlToolResult(Success, Message);

/// <summary>
/// Represents the payload returned by <c>mysql_list_schemas</c>.
/// </summary>
/// <param name="Success">Indicates whether the tool completed successfully.</param>
/// <param name="Schemas">The current MySQL schema selection.</param>
/// <param name="Message">An optional diagnostic message.</param>
internal sealed record MySqlListSchemasToolResult(bool Success, IReadOnlyList<string> Schemas, string? Message = null) : SqlToolResult(Success, Message);

/// <summary>
/// Represents the payload returned by <c>mysql_list_tables</c>.
/// </summary>
/// <param name="Success">Indicates whether the tool completed successfully.</param>
/// <param name="Tables">The fully qualified table names in the selected database.</param>
/// <param name="Message">An optional diagnostic message.</param>
internal sealed record MySqlListTablesToolResult(bool Success, IReadOnlyList<string> Tables, string? Message = null) : SqlToolResult(Success, Message);

/// <summary>
/// Represents the payload returned by <c>mysql_list_views</c>.
/// </summary>
/// <param name="Success">Indicates whether the tool completed successfully.</param>
/// <param name="Views">The fully qualified view names in the selected database.</param>
/// <param name="Message">An optional diagnostic message.</param>
internal sealed record MySqlListViewsToolResult(bool Success, IReadOnlyList<string> Views, string? Message = null) : SqlToolResult(Success, Message);

/// <summary>
/// Represents the payload returned by <c>mysql_list_functions</c>.
/// </summary>
/// <param name="Success">Indicates whether the tool completed successfully.</param>
/// <param name="Functions">The fully qualified MySQL function names.</param>
/// <param name="Message">An optional diagnostic message.</param>
internal sealed record MySqlListFunctionsToolResult(bool Success, IReadOnlyList<string> Functions, string? Message = null) : SqlToolResult(Success, Message);

/// <summary>
/// Represents the payload returned by <c>mysql_list_procedures</c>.
/// </summary>
/// <param name="Success">Indicates whether the tool completed successfully.</param>
/// <param name="Procedures">The fully qualified MySQL procedure names.</param>
/// <param name="Message">An optional diagnostic message.</param>
internal sealed record MySqlListProceduresToolResult(bool Success, IReadOnlyList<string> Procedures, string? Message = null) : SqlToolResult(Success, Message);

/// <summary>
/// Represents the payload returned by <c>mysql_get_object_definition</c>.
/// </summary>
/// <param name="Success">Indicates whether the tool completed successfully.</param>
/// <param name="Definition">The resolved table, view, function, or procedure definition.</param>
/// <param name="Message">An optional diagnostic message.</param>
internal sealed record MySqlGetObjectDefinitionToolResult(bool Success, SqlObjectDefinition? Definition, string? Message = null) : SqlToolResult(Success, Message);

/// <summary>
/// Represents the payload returned by <c>mysql_run_query</c>.
/// </summary>
/// <param name="Success">Indicates whether the tool completed successfully.</param>
/// <param name="Result">The classified query execution result.</param>
/// <param name="Message">An optional diagnostic message.</param>
internal sealed record MySqlRunQueryToolResult(bool Success, SqlExecuteQueryResult? Result, string? Message = null) : SqlToolResult(Success, Message);

/// <summary>
/// Represents the payload returned by <c>mysql_explain_query</c>.
/// </summary>
/// <param name="Success">Indicates whether the tool completed successfully.</param>
/// <param name="Result">The explain-plan summary produced for the query.</param>
/// <param name="Message">An optional diagnostic message.</param>
internal sealed record MySqlExplainQueryToolResult(bool Success, SqlExplainQueryResult? Result, string? Message = null) : SqlToolResult(Success, Message);
