using Npgsql;

namespace AIToolkit.Tools.Sql.PostgreSql;

/// <summary>
/// Small testable wrapper around <see cref="NpgsqlConnection"/> creation and opening.
/// </summary>
/// <remarks>
/// The resolver depends on this indirection so tests can substitute a fake connection factory without requiring a live PostgreSQL server.
/// </remarks>
internal class PostgreSqlConnectionFactory
{
    /// <summary>
    /// Creates a closed PostgreSQL connection for the supplied connection string.
    /// </summary>
    /// <param name="connectionString">The provider connection string to materialize.</param>
    /// <returns>A closed <see cref="NpgsqlConnection"/>.</returns>
    public virtual NpgsqlConnection CreateConnection(string connectionString) => new(connectionString);

    /// <summary>
    /// Creates and opens a PostgreSQL connection.
    /// </summary>
    /// <param name="connectionString">The provider connection string to open.</param>
    /// <param name="cancellationToken">Cancels the asynchronous open operation.</param>
    /// <returns>An open <see cref="NpgsqlConnection"/>.</returns>
    public virtual async ValueTask<NpgsqlConnection> OpenConnectionAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var connection = CreateConnection(connectionString);

        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
