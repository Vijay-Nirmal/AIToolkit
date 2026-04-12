using Microsoft.Data.Sqlite;

namespace AIToolkit.Tools.Sql.Sqlite;

/// <summary>
/// Small testable wrapper around <see cref="SqliteConnection"/> creation and opening.
/// </summary>
internal class SqliteConnectionFactory
{
    public virtual SqliteConnection CreateConnection(string connectionString) => new(connectionString);

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