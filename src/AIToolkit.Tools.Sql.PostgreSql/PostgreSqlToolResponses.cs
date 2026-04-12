namespace AIToolkit.Tools.Sql.PostgreSql;

internal sealed record PostgreSqlExtensionInfo(
    string Name,
    string Version,
    string Schema,
    string? Description = null);

internal sealed record PostgreSqlListServersToolResult(
    bool Success,
    IReadOnlyList<SqlConnectionProfileSummary> Servers,
    string? Message = null)
    : SqlToolResult(Success, Message);

internal sealed record PostgreSqlListDatabasesToolResult(
    bool Success,
    IReadOnlyList<string> Databases,
    string? Message = null)
    : SqlToolResult(Success, Message);

internal sealed record PostgreSqlListExtensionsToolResult(
    bool Success,
    IReadOnlyList<PostgreSqlExtensionInfo> Extensions,
    string? Message = null)
    : SqlToolResult(Success, Message);

internal sealed record PostgreSqlListSchemasToolResult(
    bool Success,
    IReadOnlyList<string> Schemas,
    string? Message = null)
    : SqlToolResult(Success, Message);

internal sealed record PostgreSqlListTablesToolResult(
    bool Success,
    IReadOnlyList<string> Tables,
    string? Message = null)
    : SqlToolResult(Success, Message);

internal sealed record PostgreSqlListViewsToolResult(
    bool Success,
    IReadOnlyList<string> Views,
    string? Message = null)
    : SqlToolResult(Success, Message);

internal sealed record PostgreSqlListFunctionsToolResult(
    bool Success,
    IReadOnlyList<string> Functions,
    string? Message = null)
    : SqlToolResult(Success, Message);

internal sealed record PostgreSqlListProceduresToolResult(
    bool Success,
    IReadOnlyList<string> Procedures,
    string? Message = null)
    : SqlToolResult(Success, Message);

internal sealed record PostgreSqlGetObjectDefinitionToolResult(
    bool Success,
    SqlObjectDefinition? Definition,
    string? Message = null)
    : SqlToolResult(Success, Message);

internal sealed record PostgreSqlRunQueryToolResult(
    bool Success,
    SqlExecuteQueryResult? Result,
    string? Message = null)
    : SqlToolResult(Success, Message);

internal sealed record PostgreSqlExplainQueryToolResult(
    bool Success,
    SqlExplainQueryResult? Result,
    string? Message = null)
    : SqlToolResult(Success, Message);