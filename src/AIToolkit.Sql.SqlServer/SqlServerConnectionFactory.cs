using Microsoft.Data.SqlClient;

namespace AIToolkit.Sql.SqlServer;

/// <summary>
/// Small testable wrapper around <see cref="SqlConnection"/> creation and opening.
/// </summary>
/// <remarks>
/// The resolver uses this indirection so tests can substitute a fake factory without needing a real SQL Server connection.
/// </remarks>
internal class SqlServerConnectionFactory
{
    public virtual SqlConnection CreateConnection(string connectionString) => new(connectionString);

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