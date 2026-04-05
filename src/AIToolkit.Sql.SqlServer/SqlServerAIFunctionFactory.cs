using Microsoft.Extensions.AI;
using System.Reflection;

namespace AIToolkit.Sql.SqlServer;

/// <summary>
/// Maps the internal SQL Server tool service methods to the public <c>mssql_*</c> AI function names.
/// </summary>
/// <remarks>
/// Keeping this factory separate from <see cref="SqlServerToolService"/> lets the service focus on tool behavior while this type owns the
/// stable tool names and descriptions exposed to AI hosts.
/// </remarks>
internal sealed class SqlServerAIFunctionFactory(SqlServerToolService toolService)
{
    private readonly SqlServerToolService _toolService = toolService ?? throw new ArgumentNullException(nameof(toolService));

    public IReadOnlyList<AIFunction> CreateAll() =>
    [
        Create(nameof(SqlServerToolService.ListServersAsync), "mssql_list_servers", "List all SQL Server connection profiles registered with the host."),
        Create(nameof(SqlServerToolService.ListDatabasesAsync), "mssql_list_databases", "List the databases visible to a named SQL Server connection."),
        Create(nameof(SqlServerToolService.ListTablesAsync), "mssql_list_tables", "List the tables in the current database for a named SQL Server connection."),
        Create(nameof(SqlServerToolService.ListSchemasAsync), "mssql_list_schemas", "List the schemas in the current database for a named SQL Server connection."),
        Create(nameof(SqlServerToolService.ListViewsAsync), "mssql_list_views", "List the views in the current database for a named SQL Server connection."),
        Create(nameof(SqlServerToolService.ListFunctionsAsync), "mssql_list_functions", "List the functions in the current database for a named SQL Server connection."),
        Create(nameof(SqlServerToolService.ListProceduresAsync), "mssql_list_procedures", "List the stored procedures in the current database for a named SQL Server connection."),
        Create(nameof(SqlServerToolService.GetObjectDefinitionAsync), "mssql_get_object_definition", "Get the T-SQL definition or structure for a database object using a named SQL Server connection."),
        Create(nameof(SqlServerToolService.ExplainQueryAsync), "mssql_explain_query", "Analyze a read-only SQL Server query and return an actual execution plan plus performance statistics."),
        Create(nameof(SqlServerToolService.RunQueryAsync), "mssql_run_query", "Run a SQL query using a named SQL Server connection, subject to the execution policy."),
    ];

    private AIFunction Create(string methodName, string name, string description)
    {
        var method = typeof(SqlServerToolService).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException($"Method '{methodName}' was not found on {nameof(SqlServerToolService)}.");

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