namespace AIToolkit.Tools.Sql.PostgreSql;

/// <summary>
/// Describes one PostgreSQL extension returned by <c>pgsql_list_extensions</c>.
/// </summary>
/// <param name="Name">The extension name.</param>
/// <param name="Version">The installed extension version.</param>
/// <param name="Schema">The schema that owns the extension objects.</param>
/// <param name="Description">An optional extension description.</param>
internal sealed record PostgreSqlExtensionInfo(
    string Name,
    string Version,
    string Schema,
    string? Description = null);

/// <summary>
/// Represents the payload returned by <c>pgsql_list_servers</c>.
/// </summary>
/// <param name="Success">Indicates whether the tool completed successfully.</param>
/// <param name="Servers">The registered PostgreSQL connection profiles visible to the caller.</param>
/// <param name="Message">An optional diagnostic message.</param>
internal sealed record PostgreSqlListServersToolResult(
    bool Success,
    IReadOnlyList<SqlConnectionProfileSummary> Servers,
    string? Message = null)
    : SqlToolResult(Success, Message);

/// <summary>
/// Represents the payload returned by <c>pgsql_list_databases</c>.
/// </summary>
/// <param name="Success">Indicates whether the tool completed successfully.</param>
/// <param name="Databases">The visible PostgreSQL databases.</param>
/// <param name="Message">An optional diagnostic message.</param>
internal sealed record PostgreSqlListDatabasesToolResult(
    bool Success,
    IReadOnlyList<string> Databases,
    string? Message = null)
    : SqlToolResult(Success, Message);

/// <summary>
/// Represents the payload returned by <c>pgsql_list_extensions</c>.
/// </summary>
/// <param name="Success">Indicates whether the tool completed successfully.</param>
/// <param name="Extensions">The installed PostgreSQL extensions in the selected database.</param>
/// <param name="Message">An optional diagnostic message.</param>
internal sealed record PostgreSqlListExtensionsToolResult(
    bool Success,
    IReadOnlyList<PostgreSqlExtensionInfo> Extensions,
    string? Message = null)
    : SqlToolResult(Success, Message);

/// <summary>
/// Represents the payload returned by <c>pgsql_list_schemas</c>.
/// </summary>
/// <param name="Success">Indicates whether the tool completed successfully.</param>
/// <param name="Schemas">The visible non-system schemas.</param>
/// <param name="Message">An optional diagnostic message.</param>
internal sealed record PostgreSqlListSchemasToolResult(
    bool Success,
    IReadOnlyList<string> Schemas,
    string? Message = null)
    : SqlToolResult(Success, Message);

/// <summary>
/// Represents the payload returned by <c>pgsql_list_tables</c>.
/// </summary>
/// <param name="Success">Indicates whether the tool completed successfully.</param>
/// <param name="Tables">The fully qualified table names in the selected database.</param>
/// <param name="Message">An optional diagnostic message.</param>
internal sealed record PostgreSqlListTablesToolResult(
    bool Success,
    IReadOnlyList<string> Tables,
    string? Message = null)
    : SqlToolResult(Success, Message);

/// <summary>
/// Represents the payload returned by <c>pgsql_list_views</c>.
/// </summary>
/// <param name="Success">Indicates whether the tool completed successfully.</param>
/// <param name="Views">The fully qualified view names in the selected database.</param>
/// <param name="Message">An optional diagnostic message.</param>
internal sealed record PostgreSqlListViewsToolResult(
    bool Success,
    IReadOnlyList<string> Views,
    string? Message = null)
    : SqlToolResult(Success, Message);

/// <summary>
/// Represents the payload returned by <c>pgsql_list_functions</c>.
/// </summary>
/// <param name="Success">Indicates whether the tool completed successfully.</param>
/// <param name="Functions">The fully qualified PostgreSQL function names.</param>
/// <param name="Message">An optional diagnostic message.</param>
internal sealed record PostgreSqlListFunctionsToolResult(
    bool Success,
    IReadOnlyList<string> Functions,
    string? Message = null)
    : SqlToolResult(Success, Message);

/// <summary>
/// Represents the payload returned by <c>pgsql_list_procedures</c>.
/// </summary>
/// <param name="Success">Indicates whether the tool completed successfully.</param>
/// <param name="Procedures">The fully qualified PostgreSQL procedure names.</param>
/// <param name="Message">An optional diagnostic message.</param>
internal sealed record PostgreSqlListProceduresToolResult(
    bool Success,
    IReadOnlyList<string> Procedures,
    string? Message = null)
    : SqlToolResult(Success, Message);

/// <summary>
/// Represents the payload returned by <c>pgsql_get_object_definition</c>.
/// </summary>
/// <param name="Success">Indicates whether the tool completed successfully.</param>
/// <param name="Definition">The resolved table, view, function, or procedure definition.</param>
/// <param name="Message">An optional diagnostic message.</param>
internal sealed record PostgreSqlGetObjectDefinitionToolResult(
    bool Success,
    SqlObjectDefinition? Definition,
    string? Message = null)
    : SqlToolResult(Success, Message);

/// <summary>
/// Represents the payload returned by <c>pgsql_run_query</c>.
/// </summary>
/// <param name="Success">Indicates whether the tool completed successfully.</param>
/// <param name="Result">The classified query execution result.</param>
/// <param name="Message">An optional diagnostic message.</param>
internal sealed record PostgreSqlRunQueryToolResult(
    bool Success,
    SqlExecuteQueryResult? Result,
    string? Message = null)
    : SqlToolResult(Success, Message);

/// <summary>
/// Represents the payload returned by <c>pgsql_explain_query</c>.
/// </summary>
/// <param name="Success">Indicates whether the tool completed successfully.</param>
/// <param name="Result">The explain-plan summary produced for the query.</param>
/// <param name="Message">An optional diagnostic message.</param>
internal sealed record PostgreSqlExplainQueryToolResult(
    bool Success,
    SqlExplainQueryResult? Result,
    string? Message = null)
    : SqlToolResult(Success, Message);
