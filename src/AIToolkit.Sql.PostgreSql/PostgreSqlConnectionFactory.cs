using Npgsql;

namespace AIToolkit.Sql.PostgreSql;

/// <summary>
/// Small testable wrapper around <see cref="NpgsqlConnection"/> creation and opening.
/// </summary>
internal class PostgreSqlConnectionFactory
{
    public virtual NpgsqlConnection CreateConnection(string connectionString) => new(connectionString);

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