namespace AIToolkit.Tools.Sql.Sqlite;

/// <summary>
/// Represents the payload returned by <c>sqlite_list_servers</c>.
/// </summary>
/// <param name="Success">Indicates whether the tool completed successfully.</param>
/// <param name="Servers">The registered SQLite connection profiles visible to the caller.</param>
/// <param name="Message">An optional diagnostic message.</param>
internal sealed record SqliteListServersToolResult(bool Success, IReadOnlyList<SqlConnectionProfileSummary> Servers, string? Message = null) : SqlToolResult(Success, Message);

/// <summary>
/// Represents the payload returned by <c>sqlite_list_databases</c>.
/// </summary>
/// <param name="Success">Indicates whether the tool completed successfully.</param>
/// <param name="Databases">The attached SQLite catalogs visible to the connection.</param>
/// <param name="Message">An optional diagnostic message.</param>
internal sealed record SqliteListDatabasesToolResult(bool Success, IReadOnlyList<string> Databases, string? Message = null) : SqlToolResult(Success, Message);

/// <summary>
/// Represents the payload returned by <c>sqlite_list_schemas</c>.
/// </summary>
/// <param name="Success">Indicates whether the tool completed successfully.</param>
/// <param name="Schemas">The schema-equivalent SQLite catalog names.</param>
/// <param name="Message">An optional diagnostic message.</param>
internal sealed record SqliteListSchemasToolResult(bool Success, IReadOnlyList<string> Schemas, string? Message = null) : SqlToolResult(Success, Message);

/// <summary>
/// Represents the payload returned by <c>sqlite_list_tables</c>.
/// </summary>
/// <param name="Success">Indicates whether the tool completed successfully.</param>
/// <param name="Tables">The fully qualified table names in the selected catalog.</param>
/// <param name="Message">An optional diagnostic message.</param>
internal sealed record SqliteListTablesToolResult(bool Success, IReadOnlyList<string> Tables, string? Message = null) : SqlToolResult(Success, Message);

/// <summary>
/// Represents the payload returned by <c>sqlite_list_views</c>.
/// </summary>
/// <param name="Success">Indicates whether the tool completed successfully.</param>
/// <param name="Views">The fully qualified view names in the selected catalog.</param>
/// <param name="Message">An optional diagnostic message.</param>
internal sealed record SqliteListViewsToolResult(bool Success, IReadOnlyList<string> Views, string? Message = null) : SqlToolResult(Success, Message);

/// <summary>
/// Represents the payload returned by <c>sqlite_list_functions</c>.
/// </summary>
/// <param name="Success">Indicates whether the tool completed successfully.</param>
/// <param name="Functions">The discovered SQLite functions, if any.</param>
/// <param name="Message">An optional diagnostic message.</param>
internal sealed record SqliteListFunctionsToolResult(bool Success, IReadOnlyList<string> Functions, string? Message = null) : SqlToolResult(Success, Message);

/// <summary>
/// Represents the payload returned by <c>sqlite_list_procedures</c>.
/// </summary>
/// <param name="Success">Indicates whether the tool completed successfully.</param>
/// <param name="Procedures">The discovered SQLite procedures, if any.</param>
/// <param name="Message">An optional diagnostic message.</param>
internal sealed record SqliteListProceduresToolResult(bool Success, IReadOnlyList<string> Procedures, string? Message = null) : SqlToolResult(Success, Message);

/// <summary>
/// Represents the payload returned by <c>sqlite_get_object_definition</c>.
/// </summary>
/// <param name="Success">Indicates whether the tool completed successfully.</param>
/// <param name="Definition">The resolved table or view definition.</param>
/// <param name="Message">An optional diagnostic message.</param>
internal sealed record SqliteGetObjectDefinitionToolResult(bool Success, SqlObjectDefinition? Definition, string? Message = null) : SqlToolResult(Success, Message);

/// <summary>
/// Represents the payload returned by <c>sqlite_run_query</c>.
/// </summary>
/// <param name="Success">Indicates whether the tool completed successfully.</param>
/// <param name="Result">The classified execution result.</param>
/// <param name="Message">An optional diagnostic message.</param>
internal sealed record SqliteRunQueryToolResult(bool Success, SqlExecuteQueryResult? Result, string? Message = null) : SqlToolResult(Success, Message);

/// <summary>
/// Represents the payload returned by <c>sqlite_explain_query</c>.
/// </summary>
/// <param name="Success">Indicates whether the tool completed successfully.</param>
/// <param name="Result">The explain-plan summary produced for the query.</param>
/// <param name="Message">An optional diagnostic message.</param>
internal sealed record SqliteExplainQueryToolResult(bool Success, SqlExplainQueryResult? Result, string? Message = null) : SqlToolResult(Success, Message);
