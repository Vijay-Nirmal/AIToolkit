using Microsoft.Data.SqlClient;

namespace AIToolkit.Tools.Sql.SqlServer;

/// <summary>
/// Small testable wrapper around <see cref="SqlConnection"/> creation and opening.
/// </summary>
/// <remarks>
/// The resolver uses this indirection so tests can substitute a fake factory without needing a real SQL Server connection.
/// </remarks>
internal class SqlServerConnectionFactory
{
    /// <summary>
    /// Creates a closed SQL Server connection for the supplied connection string.
    /// </summary>
    /// <param name="connectionString">The provider connection string to materialize.</param>
    /// <returns>A closed <see cref="SqlConnection"/>.</returns>
    public virtual SqlConnection CreateConnection(string connectionString) => new(connectionString);

    /// <summary>
    /// Creates and opens a SQL Server connection.
    /// </summary>
    /// <param name="connectionString">The provider connection string to open.</param>
    /// <param name="cancellationToken">Cancels the asynchronous open operation.</param>
    /// <returns>An open <see cref="SqlConnection"/>.</returns>
    public virtual async ValueTask<SqlConnection> OpenConnectionAsync(
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
