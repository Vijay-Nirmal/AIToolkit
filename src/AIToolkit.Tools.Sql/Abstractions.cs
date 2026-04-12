using System.Data.Common;

namespace AIToolkit.Tools.Sql;

/// <summary>
/// Exposes the named SQL connections that a host makes available to AI tools.
/// </summary>
public interface ISqlConnectionProfileCatalog
{
    /// <summary>
    /// Lists the named connections that can be used by stateless SQL tool calls.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel the asynchronous operation.</param>
    /// <returns>A list of connection summaries that are safe to show to an AI model.</returns>
    ValueTask<IReadOnlyList<SqlConnectionProfileSummary>> ListProfilesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Opens provider-specific database connections for stateless SQL tool calls.
/// </summary>
public interface ISqlConnectionOpener
{
    /// <summary>
    /// Opens a connection for the specified named target.
    /// </summary>
    /// <param name="target">The named connection target to open.</param>
    /// <param name="cancellationToken">The token used to cancel the asynchronous operation.</param>
    /// <returns>An open provider-specific database connection.</returns>
    ValueTask<DbConnection> OpenConnectionAsync(
        SqlConnectionTarget target,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads provider metadata such as databases, schemas, tables, views, and routines.
/// </summary>
public interface ISqlMetadataProvider
{
    /// <summary>
    /// Lists the databases visible to the specified named connection.
    /// </summary>
    /// <param name="target">The named connection target to query.</param>
    /// <param name="cancellationToken">The token used to cancel the asynchronous operation.</param>
    /// <returns>The databases visible to the current login.</returns>
    ValueTask<IReadOnlyList<SqlDatabaseInfo>> ListDatabasesAsync(
        SqlConnectionTarget target,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the schemas in the current database.
    /// </summary>
    /// <param name="target">The named connection target to query.</param>
    /// <param name="cancellationToken">The token used to cancel the asynchronous operation.</param>
    /// <returns>The schemas in the current database.</returns>
    ValueTask<IReadOnlyList<SqlSchemaInfo>> ListSchemasAsync(
        SqlConnectionTarget target,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the tables in the current database.
    /// </summary>
    /// <param name="target">The named connection target to query.</param>
    /// <param name="cancellationToken">The token used to cancel the asynchronous operation.</param>
    /// <returns>The tables in the current database.</returns>
    ValueTask<IReadOnlyList<SqlTableInfo>> ListTablesAsync(
        SqlConnectionTarget target,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the views in the current database.
    /// </summary>
    /// <param name="target">The named connection target to query.</param>
    /// <param name="cancellationToken">The token used to cancel the asynchronous operation.</param>
    /// <returns>The views in the current database.</returns>
    ValueTask<IReadOnlyList<SqlViewInfo>> ListViewsAsync(
        SqlConnectionTarget target,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the scalar, inline table-valued, and multi-statement functions in the current database.
    /// </summary>
    /// <param name="target">The named connection target to query.</param>
    /// <param name="cancellationToken">The token used to cancel the asynchronous operation.</param>
    /// <returns>The functions in the current database.</returns>
    ValueTask<IReadOnlyList<SqlRoutineInfo>> ListFunctionsAsync(
        SqlConnectionTarget target,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the stored procedures in the current database.
    /// </summary>
    /// <param name="target">The named connection target to query.</param>
    /// <param name="cancellationToken">The token used to cancel the asynchronous operation.</param>
    /// <returns>The stored procedures in the current database.</returns>
    ValueTask<IReadOnlyList<SqlRoutineInfo>> ListProceduresAsync(
        SqlConnectionTarget target,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the definition for a database object such as a table, view, function, or procedure.
    /// </summary>
    /// <param name="target">The named connection target to query.</param>
    /// <param name="schemaName">The schema name, or <see langword="null"/> to use provider-specific defaults.</param>
    /// <param name="objectName">The object name to resolve.</param>
    /// <param name="objectKind">The expected object kind, or <see cref="SqlObjectKind.Unknown"/> to resolve it automatically.</param>
    /// <param name="cancellationToken">The token used to cancel the asynchronous operation.</param>
    /// <returns>The resolved object definition.</returns>
    ValueTask<SqlObjectDefinition> GetObjectDefinitionAsync(
        SqlConnectionTarget target,
        string? schemaName,
        string objectName,
        SqlObjectKind objectKind,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a compact schema snapshot containing tables, views, functions, and procedures.
    /// </summary>
    /// <param name="target">The named connection target to query.</param>
    /// <param name="cancellationToken">The token used to cancel the asynchronous operation.</param>
    /// <returns>A schema overview that can be used to ground later tool calls.</returns>
    ValueTask<SqlSchemaOverview> GetSchemaOverviewAsync(
        SqlConnectionTarget target,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Classifies SQL text into safe read-only, approval-required, or blocked categories.
/// </summary>
public interface ISqlQueryClassifier
{
    /// <summary>
    /// Classifies a SQL statement or batch.
    /// </summary>
    /// <param name="query">The SQL text to classify.</param>
    /// <returns>The statement types, safety level, and an optional reason.</returns>
    SqlQueryClassification Classify(string query);
}

/// <summary>
/// Approves or rejects SQL mutations before execution.
/// </summary>
public interface ISqlMutationApprover
{
    /// <summary>
    /// Produces an approval decision for a mutation-capable SQL operation.
    /// </summary>
    /// <param name="request">The pending mutation request.</param>
    /// <param name="cancellationToken">The token used to cancel the asynchronous operation.</param>
    /// <returns>The decision indicating whether execution may continue.</returns>
    ValueTask<SqlMutationApprovalDecision> ApproveAsync(
        SqlMutationApprovalRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Executes SQL text against a named provider connection.
/// </summary>
public interface ISqlQueryExecutor
{
    /// <summary>
    /// Executes a SQL statement or batch against a named connection target.
    /// </summary>
    /// <param name="target">The named connection target to use.</param>
    /// <param name="query">The SQL text to execute.</param>
    /// <param name="cancellationToken">The token used to cancel the asynchronous operation.</param>
    /// <returns>The execution result, including classification and any returned result sets.</returns>
    ValueTask<SqlExecuteQueryResult> ExecuteAsync(
        SqlConnectionTarget target,
        string query,
        CancellationToken cancellationToken = default);
}