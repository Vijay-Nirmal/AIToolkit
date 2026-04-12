namespace AIToolkit.Tools.Sql.MySql;

internal sealed record MySqlListServersToolResult(bool Success, IReadOnlyList<SqlConnectionProfileSummary> Servers, string? Message = null) : SqlToolResult(Success, Message);
internal sealed record MySqlListDatabasesToolResult(bool Success, IReadOnlyList<string> Databases, string? Message = null) : SqlToolResult(Success, Message);
internal sealed record MySqlListSchemasToolResult(bool Success, IReadOnlyList<string> Schemas, string? Message = null) : SqlToolResult(Success, Message);
internal sealed record MySqlListTablesToolResult(bool Success, IReadOnlyList<string> Tables, string? Message = null) : SqlToolResult(Success, Message);
internal sealed record MySqlListViewsToolResult(bool Success, IReadOnlyList<string> Views, string? Message = null) : SqlToolResult(Success, Message);
internal sealed record MySqlListFunctionsToolResult(bool Success, IReadOnlyList<string> Functions, string? Message = null) : SqlToolResult(Success, Message);
internal sealed record MySqlListProceduresToolResult(bool Success, IReadOnlyList<string> Procedures, string? Message = null) : SqlToolResult(Success, Message);
internal sealed record MySqlGetObjectDefinitionToolResult(bool Success, SqlObjectDefinition? Definition, string? Message = null) : SqlToolResult(Success, Message);
internal sealed record MySqlRunQueryToolResult(bool Success, SqlExecuteQueryResult? Result, string? Message = null) : SqlToolResult(Success, Message);
internal sealed record MySqlExplainQueryToolResult(bool Success, SqlExplainQueryResult? Result, string? Message = null) : SqlToolResult(Success, Message);