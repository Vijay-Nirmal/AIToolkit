namespace AIToolkit.Tools.Sql.Sqlite;

internal sealed record SqliteListServersToolResult(bool Success, IReadOnlyList<SqlConnectionProfileSummary> Servers, string? Message = null) : SqlToolResult(Success, Message);
internal sealed record SqliteListDatabasesToolResult(bool Success, IReadOnlyList<string> Databases, string? Message = null) : SqlToolResult(Success, Message);
internal sealed record SqliteListSchemasToolResult(bool Success, IReadOnlyList<string> Schemas, string? Message = null) : SqlToolResult(Success, Message);
internal sealed record SqliteListTablesToolResult(bool Success, IReadOnlyList<string> Tables, string? Message = null) : SqlToolResult(Success, Message);
internal sealed record SqliteListViewsToolResult(bool Success, IReadOnlyList<string> Views, string? Message = null) : SqlToolResult(Success, Message);
internal sealed record SqliteListFunctionsToolResult(bool Success, IReadOnlyList<string> Functions, string? Message = null) : SqlToolResult(Success, Message);
internal sealed record SqliteListProceduresToolResult(bool Success, IReadOnlyList<string> Procedures, string? Message = null) : SqlToolResult(Success, Message);
internal sealed record SqliteGetObjectDefinitionToolResult(bool Success, SqlObjectDefinition? Definition, string? Message = null) : SqlToolResult(Success, Message);
internal sealed record SqliteRunQueryToolResult(bool Success, SqlExecuteQueryResult? Result, string? Message = null) : SqlToolResult(Success, Message);
internal sealed record SqliteExplainQueryToolResult(bool Success, SqlExplainQueryResult? Result, string? Message = null) : SqlToolResult(Success, Message);