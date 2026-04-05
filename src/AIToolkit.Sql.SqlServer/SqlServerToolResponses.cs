namespace AIToolkit.Sql.SqlServer;

internal sealed record SqlServerListServersToolResult(
    bool Success,
    IReadOnlyList<SqlConnectionProfileSummary> Servers,
    string? Message = null)
    : SqlToolResult(Success, Message);

internal sealed record SqlServerListDatabasesToolResult(
    bool Success,
    IReadOnlyList<string> Databases,
    string? Message = null)
    : SqlToolResult(Success, Message);

internal sealed record SqlServerListSchemasToolResult(
    bool Success,
    IReadOnlyList<string> Schemas,
    string? Message = null)
    : SqlToolResult(Success, Message);

internal sealed record SqlServerListTablesToolResult(
    bool Success,
    IReadOnlyList<string> Tables,
    string? Message = null)
    : SqlToolResult(Success, Message);

internal sealed record SqlServerListViewsToolResult(
    bool Success,
    IReadOnlyList<string> Views,
    string? Message = null)
    : SqlToolResult(Success, Message);

internal sealed record SqlServerListFunctionsToolResult(
    bool Success,
    IReadOnlyList<string> Functions,
    string? Message = null)
    : SqlToolResult(Success, Message);

internal sealed record SqlServerListProceduresToolResult(
    bool Success,
    IReadOnlyList<string> Procedures,
    string? Message = null)
    : SqlToolResult(Success, Message);

internal sealed record SqlServerGetObjectDefinitionToolResult(
    bool Success,
    SqlObjectDefinition? Definition,
    string? Message = null)
    : SqlToolResult(Success, Message);

internal sealed record SqlServerRunQueryToolResult(
    bool Success,
    SqlExecuteQueryResult? Result,
    string? Message = null)
    : SqlToolResult(Success, Message);

internal sealed record SqlServerExplainQueryToolResult(
    bool Success,
    SqlExplainQueryResult? Result,
    string? Message = null)
    : SqlToolResult(Success, Message);