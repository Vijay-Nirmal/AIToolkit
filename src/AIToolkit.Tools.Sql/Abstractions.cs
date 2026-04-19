using System.Data.Common;

namespace AIToolkit.Tools.Sql;

/// <summary>
/// Exposes the named SQL connections that a host makes available to AI tools.
/// </summary>
/// <remarks>
/// Provider-specific resolvers such as <c>SqliteConnectionResolver</c>, <c>MySqlConnectionResolver</c>,
/// <c>PostgreSqlConnectionResolver</c>, and <c>SqlServerConnectionResolver</c> commonly implement this interface so the same component can
/// both describe registered profiles and open provider-native connections. Tool services use the returned summaries to ground a model before
/// it attempts metadata lookup or query execution.
/// </remarks>
/// <seealso cref="ISqlConnectionOpener"/>
/// <seealso cref="SqlConnectionProfileSummary"/>
public interface ISqlConnectionProfileCatalog
{
    /// <summary>
    /// Lists the named connections that can be used by stateless SQL tool calls.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel the asynchronous operation.</param>
    /// <returns>A list of connection summaries that are safe to show to an AI model.</returns>
    /// <remarks>
    /// Implementations should avoid returning secrets or raw connection strings. The intent is to reveal only enough information for an
    /// assistant to choose a logical connection and, when useful, a default server or database name.
    /// </remarks>
    ValueTask<IReadOnlyList<SqlConnectionProfileSummary>> ListProfilesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Opens provider-specific database connections for stateless SQL tool calls.
/// </summary>
/// <remarks>
/// The shared <see cref="SqlQueryExecutor"/> and provider metadata services depend on this abstraction instead of concrete ADO.NET types.
/// Implementations are responsible for applying per-call database overrides from <see cref="SqlConnectionTarget"/>, enforcing host-selected
/// connection profiles, and returning an already-open <see cref="DbConnection"/>.
/// </remarks>
/// <seealso cref="ISqlConnectionProfileCatalog"/>
/// <seealso cref="SqlConnectionTarget"/>
public interface ISqlConnectionOpener
{
    /// <summary>
    /// Opens a connection for the specified named target.
    /// </summary>
    /// <param name="target">The named connection target to open.</param>
    /// <param name="cancellationToken">The token used to cancel the asynchronous operation.</param>
    /// <returns>An open provider-specific database connection.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the named connection cannot be resolved or when the provider cannot create a usable connection for the requested target.
    /// </exception>
    ValueTask<DbConnection> OpenConnectionAsync(
        SqlConnectionTarget target,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads provider metadata such as databases, schemas, tables, views, and routines.
/// </summary>
/// <remarks>
/// Shared tool services convert the rich models returned by this interface into provider-shaped AI tool payloads. Each provider keeps its
/// catalog SQL, identifier rules, and object-definition reconstruction logic close to the database client implementation while still sharing
/// common result models from <see cref="SqlMetadataModels"/>.
/// </remarks>
/// <seealso cref="SqlSchemaOverview"/>
/// <seealso cref="SqlObjectDefinition"/>
public interface ISqlMetadataProvider
{
    /// <summary>
    /// Lists the databases visible to the specified named connection.
    /// </summary>
    /// <param name="target">The named connection target to query.</param>
    /// <param name="cancellationToken">The token used to cancel the asynchronous operation.</param>
    /// <returns>The databases visible to the current login.</returns>
    /// <remarks>
    /// Providers may interpret "database" differently. For example, SQLite often reports attached catalogs, while server-based providers
    /// typically surface catalogs directly from system metadata tables.
    /// </remarks>
    ValueTask<IReadOnlyList<SqlDatabaseInfo>> ListDatabasesAsync(
        SqlConnectionTarget target,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the schemas in the current database.
    /// </summary>
    /// <param name="target">The named connection target to query.</param>
    /// <param name="cancellationToken">The token used to cancel the asynchronous operation.</param>
    /// <returns>The schemas in the current database.</returns>
    /// <remarks>
    /// Providers that do not expose first-class schemas may map this to their closest equivalent. SQLite, for example, commonly uses attached
    /// database names in place of schemas.
    /// </remarks>
    ValueTask<IReadOnlyList<SqlSchemaInfo>> ListSchemasAsync(
        SqlConnectionTarget target,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the tables in the current database.
    /// </summary>
    /// <param name="target">The named connection target to query.</param>
    /// <param name="cancellationToken">The token used to cancel the asynchronous operation.</param>
    /// <returns>The tables in the current database.</returns>
    /// <remarks>
    /// Implementations should return stable identifiers that callers can feed back into definition lookup and schema overview operations.
    /// </remarks>
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
    /// <remarks>
    /// Providers may return an empty list when the underlying engine does not expose routines through metadata, or when the concept is not
    /// supported by the engine at all.
    /// </remarks>
    ValueTask<IReadOnlyList<SqlRoutineInfo>> ListFunctionsAsync(
        SqlConnectionTarget target,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the stored procedures in the current database.
    /// </summary>
    /// <param name="target">The named connection target to query.</param>
    /// <param name="cancellationToken">The token used to cancel the asynchronous operation.</param>
    /// <returns>The stored procedures in the current database.</returns>
    /// <remarks>
    /// Providers may return an empty list when stored procedures are not part of the dialect, such as SQLite.
    /// </remarks>
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
    /// <exception cref="ArgumentException"><paramref name="objectName"/> is missing.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the object cannot be found, when the kind cannot be resolved, or when the provider cannot represent the requested object as
    /// definition text.
    /// </exception>
    /// <remarks>
    /// Callers may pass <see cref="SqlObjectKind.Unknown"/> to let the provider resolve the object kind by consulting provider-specific catalog
    /// metadata. Many providers also interpret <paramref name="schemaName"/> differently: SQL Server defaults to <c>dbo</c>, PostgreSQL to
    /// <c>public</c>, and MySQL often falls back to the currently selected database.
    /// </remarks>
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
    /// <remarks>
    /// This is typically used to give an AI model a compact snapshot before it decides which object-definition or query tool to call next.
    /// Implementations should prefer inexpensive catalog reads over heavyweight inspection where possible.
    /// </remarks>
    ValueTask<SqlSchemaOverview> GetSchemaOverviewAsync(
        SqlConnectionTarget target,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Classifies SQL text into safe read-only, approval-required, or blocked categories.
/// </summary>
/// <remarks>
/// The shared executor asks the classifier to inspect the raw query text before opening a connection. Providers keep their own classifier so
/// they can account for dialect-specific mutation verbs, administrative commands, quoting rules, and common read-only statements.
/// </remarks>
/// <seealso cref="SqlQueryClassification"/>
/// <seealso cref="SqlStatementSafety"/>
public interface ISqlQueryClassifier
{
    /// <summary>
    /// Classifies a SQL statement or batch.
    /// </summary>
    /// <param name="query">The SQL text to classify.</param>
    /// <returns>The statement types, safety level, and an optional reason.</returns>
    /// <remarks>
    /// Implementations are intentionally conservative. When a statement cannot be confidently identified as safe, it should generally be
    /// classified as <see cref="SqlStatementSafety.Blocked"/> or <see cref="SqlStatementSafety.ApprovalRequired"/>.
    /// </remarks>
    SqlQueryClassification Classify(string query);
}

/// <summary>
/// Approves or rejects SQL mutations before execution.
/// </summary>
/// <remarks>
/// This approval step only participates when <see cref="SqlExecutionPolicy.AllowMutations"/> is enabled and
/// <see cref="SqlExecutionPolicy.RequireApprovalForMutations"/> remains <see langword="true"/>. Hosts can wire human approval, policy engines,
/// audit logging, or deterministic allow/deny behavior behind this interface.
/// </remarks>
/// <seealso cref="SqlMutationApprovalRequest"/>
/// <seealso cref="SqlMutationApprovalDecision"/>
public interface ISqlMutationApprover
{
    /// <summary>
    /// Produces an approval decision for a mutation-capable SQL operation.
    /// </summary>
    /// <param name="request">The pending mutation request.</param>
    /// <param name="cancellationToken">The token used to cancel the asynchronous operation.</param>
    /// <returns>The decision indicating whether execution may continue.</returns>
    /// <remarks>
    /// Returning a denial reason is recommended because the tool layer can surface that explanation directly to the caller.
    /// </remarks>
    ValueTask<SqlMutationApprovalDecision> ApproveAsync(
        SqlMutationApprovalRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Executes SQL text against a named provider connection.
/// </summary>
/// <remarks>
/// Providers may wrap the shared <see cref="SqlQueryExecutor"/> with dialect-specific behavior, but callers interact with a single execution
/// contract. Execution typically flows through classification, optional mutation approval, connection opening, command execution, and
/// result-set materialization.
/// </remarks>
/// <seealso cref="SqlQueryExecutor"/>
/// <seealso cref="SqlExecuteQueryResult"/>
public interface ISqlQueryExecutor
{
    /// <summary>
    /// Executes a SQL statement or batch against a named connection target.
    /// </summary>
    /// <param name="target">The named connection target to use.</param>
    /// <param name="query">The SQL text to execute.</param>
    /// <param name="cancellationToken">The token used to cancel the asynchronous operation.</param>
    /// <returns>The execution result, including classification and any returned result sets.</returns>
    /// <remarks>
    /// Implementations should prefer returning a failed <see cref="SqlExecuteQueryResult"/> over throwing for user-driven execution failures so
    /// the tool layer can surface provider messages consistently.
    /// </remarks>
    ValueTask<SqlExecuteQueryResult> ExecuteAsync(
        SqlConnectionTarget target,
        string query,
        CancellationToken cancellationToken = default);
}
