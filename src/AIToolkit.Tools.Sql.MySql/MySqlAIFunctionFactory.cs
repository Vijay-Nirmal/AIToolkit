using Microsoft.Extensions.AI;
using System.Reflection;

namespace AIToolkit.Tools.Sql.MySql;

/// <summary>
/// Maps the internal MySQL tool service methods to the public <c>mysql_*</c> AI function names.
/// </summary>
/// <remarks>
/// Tool behavior lives in <see cref="MySqlToolService"/>, while this factory owns the stable function names and descriptions presented to AI
/// hosts. That separation keeps prompt contracts stable even when the implementation evolves.
/// </remarks>
/// <param name="toolService">The service instance whose public methods are exposed as AI functions.</param>
internal sealed class MySqlAIFunctionFactory(MySqlToolService toolService)
{
    private readonly MySqlToolService _toolService = toolService ?? throw new ArgumentNullException(nameof(toolService));

    /// <summary>
    /// Creates the complete MySQL AI function set.
    /// </summary>
    /// <returns>The <c>mysql_*</c> functions backed by <see cref="MySqlToolService"/>.</returns>
    public IReadOnlyList<AIFunction> CreateAll() =>
    [
        Create(nameof(MySqlToolService.ListServersAsync), "mysql_list_servers", "List all MySQL connection profiles registered with the host."),
        Create(nameof(MySqlToolService.ListDatabasesAsync), "mysql_list_databases", "List the databases visible to a named MySQL connection."),
        Create(nameof(MySqlToolService.ListTablesAsync), "mysql_list_tables", "List the tables in the current database for a named MySQL connection."),
        Create(nameof(MySqlToolService.ListSchemasAsync), "mysql_list_schemas", "List the schemas exposed by the current MySQL database selection."),
        Create(nameof(MySqlToolService.ListViewsAsync), "mysql_list_views", "List the views in the current database for a named MySQL connection."),
        Create(nameof(MySqlToolService.ListFunctionsAsync), "mysql_list_functions", "List the functions in the current database for a named MySQL connection."),
        Create(nameof(MySqlToolService.ListProceduresAsync), "mysql_list_procedures", "List the stored procedures in the current database for a named MySQL connection."),
        Create(nameof(MySqlToolService.GetObjectDefinitionAsync), "mysql_get_object_definition", "Get the MySQL definition or structure for a database object using a named connection."),
        Create(nameof(MySqlToolService.ExplainQueryAsync), "mysql_explain_query", "Analyze a read-only MySQL query and return an execution plan plus runtime iterator statistics when supported by the server."),
        Create(nameof(MySqlToolService.RunQueryAsync), "mysql_run_query", "Run a SQL query using a named MySQL connection, subject to the execution policy."),
    ];

    private AIFunction Create(string methodName, string name, string description)
    {
        var method = typeof(MySqlToolService).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException($"Method '{methodName}' was not found on {nameof(MySqlToolService)}.");

        return AIFunctionFactory.Create(
            method,
            _toolService,
            new AIFunctionFactoryOptions
            {
                Name = name,
                Description = description,
            });
    }
}
