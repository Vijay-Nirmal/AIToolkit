using MySqlConnector;

namespace AIToolkit.Tools.Sql.MySql;

/// <summary>
/// Small testable wrapper around <see cref="MySqlConnection"/> creation and opening.
/// </summary>
/// <remarks>
/// The resolver depends on this indirection so tests can substitute a fake connection factory without requiring a live MySQL server.
/// </remarks>
internal class MySqlConnectionFactory
{
    /// <summary>
    /// Creates a closed MySQL connection for the supplied connection string.
    /// </summary>
    /// <param name="connectionString">The provider connection string to materialize.</param>
    /// <returns>A closed <see cref="MySqlConnection"/>.</returns>
    public virtual MySqlConnection CreateConnection(string connectionString) => new(connectionString);

    /// <summary>
    /// Creates and opens a MySQL connection.
    /// </summary>
    /// <param name="connectionString">The provider connection string to open.</param>
    /// <param name="cancellationToken">Cancels the asynchronous open operation.</param>
    /// <returns>An open <see cref="MySqlConnection"/>.</returns>
    public virtual async ValueTask<MySqlConnection> OpenConnectionAsync(
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
