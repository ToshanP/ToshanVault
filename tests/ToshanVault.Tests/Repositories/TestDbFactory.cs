using Microsoft.Data.Sqlite;
using ToshanVault.Data.Schema;

namespace ToshanVault.Tests.Repositories;

/// <summary>
/// Shared in-memory SQLite factory for repository tests. Uses a unique
/// shared-cache name per instance and a keep-alive connection so the DB
/// survives across short-lived per-call connections.
/// </summary>
internal sealed class TestDbFactory : IDbConnectionFactory, IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly string _cs;

    public TestDbFactory()
    {
        _cs = $"Data Source=file:tv-{Guid.NewGuid():N}?mode=memory&cache=shared";
        _keepAlive = new SqliteConnection(_cs);
        _keepAlive.Open();

        // Foreign keys must be enabled on every connection — including the
        // keep-alive one — for cascade deletes to fire.
        using var p = _keepAlive.CreateCommand();
        p.CommandText = "PRAGMA foreign_keys=ON;";
        p.ExecuteNonQuery();
    }

    public SqliteConnection Open()
    {
        var c = new SqliteConnection(_cs);
        c.Open();
        using var p = c.CreateCommand();
        p.CommandText = "PRAGMA foreign_keys=ON;";
        p.ExecuteNonQuery();
        return c;
    }

    public void Dispose() => _keepAlive.Dispose();

    public static async Task<TestDbFactory> CreateMigratedAsync()
    {
        var f = new TestDbFactory();
        await new MigrationRunner(f).RunAsync();
        return f;
    }
}
