using MySqlConnector;

namespace AIToolkit.Sql.MySql;

/// <summary>
/// Small testable wrapper around <see cref="MySqlConnection"/> creation and opening.
/// </summary>
internal class MySqlConnectionFactory
{
    public virtual MySqlConnection CreateConnection(string connectionString) => new(connectionString);

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