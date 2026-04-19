using Microsoft.Data.Sqlite;

namespace AIToolkit.Tools.Sql.Sqlite;

/// <summary>
/// Small testable wrapper around <see cref="SqliteConnection"/> creation and opening.
/// </summary>
/// <remarks>
/// The resolver depends on this indirection so unit tests can intercept connection creation without touching a real SQLite file.
/// </remarks>
internal class SqliteConnectionFactory
{
    /// <summary>
    /// Creates a closed SQLite connection for the supplied connection string.
    /// </summary>
    /// <param name="connectionString">The provider connection string to materialize.</param>
    /// <returns>A closed <see cref="SqliteConnection"/>.</returns>
    public virtual SqliteConnection CreateConnection(string connectionString) => new(connectionString);

    /// <summary>
    /// Creates and opens a SQLite connection.
    /// </summary>
    /// <param name="connectionString">The provider connection string to open.</param>
    /// <param name="cancellationToken">Cancels the asynchronous open operation.</param>
    /// <returns>An open <see cref="SqliteConnection"/>.</returns>
    /// <exception cref="Exception">
    /// Propagates any provider exception that occurs while opening the connection after disposing the partially created connection.
    /// </exception>
    public virtual async ValueTask<SqliteConnection> OpenConnectionAsync(
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
