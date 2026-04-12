namespace AIToolkit.Tools.Sql;

/// <summary>
/// Identifies a general category of database object.
/// </summary>
public enum SqlObjectKind
{
    /// <summary>Unknown or unresolved object kind.</summary>
    Unknown = 0,

    /// <summary>A database.</summary>
    Database,

    /// <summary>A schema.</summary>
    Schema,

    /// <summary>A table.</summary>
    Table,

    /// <summary>A view.</summary>
    View,

    /// <summary>A function.</summary>
    Function,

    /// <summary>A stored procedure.</summary>
    Procedure,
}

/// <summary>
/// Identifies whether a routine is a function or stored procedure.
/// </summary>
public enum SqlRoutineKind
{
    /// <summary>A SQL function.</summary>
    Function = 0,

    /// <summary>A SQL stored procedure.</summary>
    Procedure,
}

/// <summary>
/// Identifies an object by schema and name.
/// </summary>
/// <param name="Schema">The schema name, or <see langword="null"/> when the object is not schema-scoped.</param>
/// <param name="Name">The object name.</param>
public sealed record SqlObjectIdentifier(string? Schema, string Name)
{
    /// <summary>
    /// Gets the identifier in <c>schema.name</c> form when a schema is present, or just <see cref="Name"/> otherwise.
    /// </summary>
    public string FullyQualifiedName => string.IsNullOrWhiteSpace(Schema) ? Name : $"{Schema}.{Name}";
}

/// <summary>
/// Represents a database name returned by a metadata query.
/// </summary>
/// <param name="Name">The database name.</param>
public sealed record SqlDatabaseInfo(string Name);

/// <summary>
/// Represents a schema name returned by a metadata query.
/// </summary>
/// <param name="Name">The schema name.</param>
public sealed record SqlSchemaInfo(string Name);

/// <summary>
/// Describes a table column.
/// </summary>
/// <param name="Name">The column name.</param>
/// <param name="DataType">The provider-specific SQL type name.</param>
/// <param name="IsNullable"><see langword="true"/> when the column accepts null values.</param>
/// <param name="OrdinalPosition">The zero-based column position.</param>
public sealed record SqlColumnInfo(
    string Name,
    string DataType,
    bool IsNullable,
    int OrdinalPosition);

/// <summary>
/// Represents a table returned by a metadata query.
/// </summary>
/// <param name="Table">The table identifier.</param>
public sealed record SqlTableInfo(SqlObjectIdentifier Table);

/// <summary>
/// Represents a view returned by a metadata query.
/// </summary>
/// <param name="View">The view identifier.</param>
public sealed record SqlViewInfo(SqlObjectIdentifier View);

/// <summary>
/// Represents a function or procedure returned by a metadata query.
/// </summary>
/// <param name="Routine">The routine identifier.</param>
/// <param name="Kind">The routine kind.</param>
public sealed record SqlRoutineInfo(SqlObjectIdentifier Routine, SqlRoutineKind Kind);

/// <summary>
/// Represents a table and its columns.
/// </summary>
/// <param name="Table">The table identifier.</param>
/// <param name="Columns">The table columns in ordinal order.</param>
public sealed record SqlTableDetails(SqlObjectIdentifier Table, IReadOnlyList<SqlColumnInfo> Columns);

/// <summary>
/// Represents the resolved definition text for a schema object.
/// </summary>
/// <param name="Identifier">The resolved object identifier.</param>
/// <param name="Kind">The object kind.</param>
/// <param name="Definition">The provider-specific definition text.</param>
public sealed record SqlObjectDefinition(
    SqlObjectIdentifier Identifier,
    SqlObjectKind Kind,
    string Definition);

/// <summary>
/// Captures a compact snapshot of a database schema.
/// </summary>
/// <param name="Tables">The tables in the current database.</param>
/// <param name="Views">The views in the current database.</param>
/// <param name="Functions">The functions in the current database.</param>
/// <param name="Procedures">The procedures in the current database.</param>
public sealed record SqlSchemaOverview(
    IReadOnlyList<SqlTableInfo> Tables,
    IReadOnlyList<SqlViewInfo> Views,
    IReadOnlyList<SqlRoutineInfo> Functions,
    IReadOnlyList<SqlRoutineInfo> Procedures);