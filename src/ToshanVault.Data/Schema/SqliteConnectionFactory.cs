using Microsoft.Data.Sqlite;

namespace ToshanVault.Data.Schema;

/// <summary>
/// Opens a fresh <see cref="SqliteConnection"/> per call against a fixed
/// connection string. Production connection strings should set
/// <c>Mode=ReadWriteCreate</c>, <c>Cache=Private</c>, <c>Pooling=False</c>.
/// </summary>
public sealed class SqliteConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public SqliteConnectionFactory(string connectionString)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionString);
        _connectionString = connectionString;
    }

    public SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
        pragma.ExecuteNonQuery();

        return conn;
    }
}
