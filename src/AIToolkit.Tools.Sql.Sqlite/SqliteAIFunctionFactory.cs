using Microsoft.Extensions.AI;
using System.Reflection;

namespace AIToolkit.Tools.Sql.Sqlite;

/// <summary>
/// Maps the internal SQLite tool service methods to the public <c>sqlite_*</c> AI function names.
/// </summary>
internal sealed class SqliteAIFunctionFactory(SqliteToolService toolService)
{
    private readonly SqliteToolService _toolService = toolService ?? throw new ArgumentNullException(nameof(toolService));

    public IReadOnlyList<AIFunction> CreateAll() =>
    [
        Create(nameof(SqliteToolService.ListServersAsync), "sqlite_list_servers", "List all SQLite connection profiles registered with the host."),
        Create(nameof(SqliteToolService.ListDatabasesAsync), "sqlite_list_databases", "List attached SQLite databases for a named connection."),
        Create(nameof(SqliteToolService.ListTablesAsync), "sqlite_list_tables", "List the tables in the selected SQLite catalog for a named connection."),
        Create(nameof(SqliteToolService.ListSchemasAsync), "sqlite_list_schemas", "List the attached SQLite catalogs visible to a named connection."),
        Create(nameof(SqliteToolService.ListViewsAsync), "sqlite_list_views", "List the views in the selected SQLite catalog for a named connection."),
        Create(nameof(SqliteToolService.ListFunctionsAsync), "sqlite_list_functions", "List SQLite functions registered by metadata. Returns an empty result because SQLite has no built-in routine catalog."),
        Create(nameof(SqliteToolService.ListProceduresAsync), "sqlite_list_procedures", "List SQLite procedures registered by metadata. Returns an empty result because SQLite does not support stored procedures."),
        Create(nameof(SqliteToolService.GetObjectDefinitionAsync), "sqlite_get_object_definition", "Get the SQLite schema definition for a table or view using a named connection."),
        Create(nameof(SqliteToolService.ExplainQueryAsync), "sqlite_explain_query", "Analyze a read-only SQLite query and return high-level planner output from EXPLAIN QUERY PLAN."),
        Create(nameof(SqliteToolService.RunQueryAsync), "sqlite_run_query", "Run a SQL query using a named SQLite connection, subject to the execution policy."),
    ];

    private AIFunction Create(string methodName, string name, string description)
    {
        var method = typeof(SqliteToolService).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException($"Method '{methodName}' was not found on {nameof(SqliteToolService)}.");

        return AIFunctionFactory.Create(method, _toolService, new AIFunctionFactoryOptions { Name = name, Description = description });
    }
}