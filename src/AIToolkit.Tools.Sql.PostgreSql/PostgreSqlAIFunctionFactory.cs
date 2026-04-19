using Microsoft.Extensions.AI;
using System.Reflection;

namespace AIToolkit.Tools.Sql.PostgreSql;

/// <summary>
/// Maps the internal PostgreSQL tool service methods to the public <c>pgsql_*</c> AI function names.
/// </summary>
/// <remarks>
/// Tool behavior lives in <see cref="PostgreSqlToolService"/>, while this factory owns the stable function names and descriptions presented to
/// AI hosts. That separation keeps prompt contracts stable even when the implementation evolves.
/// </remarks>
/// <param name="toolService">The service instance whose public methods are exposed as AI functions.</param>
internal sealed class PostgreSqlAIFunctionFactory(PostgreSqlToolService toolService)
{
    private readonly PostgreSqlToolService _toolService = toolService ?? throw new ArgumentNullException(nameof(toolService));

    /// <summary>
    /// Creates the complete PostgreSQL AI function set.
    /// </summary>
    /// <returns>The <c>pgsql_*</c> functions backed by <see cref="PostgreSqlToolService"/>.</returns>
    public IReadOnlyList<AIFunction> CreateAll() =>
    [
        Create(nameof(PostgreSqlToolService.ListServersAsync), "pgsql_list_servers", "List all PostgreSQL connection profiles registered with the host."),
        Create(nameof(PostgreSqlToolService.ListDatabasesAsync), "pgsql_list_databases", "List the databases visible to a named PostgreSQL connection."),
        Create(nameof(PostgreSqlToolService.ListExtensionsAsync), "pgsql_list_extensions", "List the installed extensions in the current PostgreSQL database for a named connection."),
        Create(nameof(PostgreSqlToolService.ListTablesAsync), "pgsql_list_tables", "List the tables in the current database for a named PostgreSQL connection."),
        Create(nameof(PostgreSqlToolService.ListSchemasAsync), "pgsql_list_schemas", "List the schemas in the current database for a named PostgreSQL connection."),
        Create(nameof(PostgreSqlToolService.ListViewsAsync), "pgsql_list_views", "List the views in the current database for a named PostgreSQL connection."),
        Create(nameof(PostgreSqlToolService.ListFunctionsAsync), "pgsql_list_functions", "List the functions in the current database for a named PostgreSQL connection."),
        Create(nameof(PostgreSqlToolService.ListProceduresAsync), "pgsql_list_procedures", "List the stored procedures in the current database for a named PostgreSQL connection."),
        Create(nameof(PostgreSqlToolService.GetObjectDefinitionAsync), "pgsql_get_object_definition", "Get the PostgreSQL definition or structure for a database object using a named connection."),
        Create(nameof(PostgreSqlToolService.ExplainQueryAsync), "pgsql_explain_query", "Analyze a read-only PostgreSQL query and return an execution plan plus performance statistics."),
        Create(nameof(PostgreSqlToolService.RunQueryAsync), "pgsql_run_query", "Run a SQL query using a named PostgreSQL connection, subject to the execution policy."),
    ];

    private AIFunction Create(string methodName, string name, string description)
    {
        var method = typeof(PostgreSqlToolService).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException($"Method '{methodName}' was not found on {nameof(PostgreSqlToolService)}.");

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
