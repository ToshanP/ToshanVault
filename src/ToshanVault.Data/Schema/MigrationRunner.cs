using System.Reflection;
using Dapper;
using Microsoft.Data.Sqlite;

namespace ToshanVault.Data.Schema;

/// <summary>
/// Discovers <c>NNN_*.sql</c> embedded resources under
/// <c>ToshanVault.Data.Schema.Migrations.*</c> and applies any whose
/// numeric prefix is greater than the current <c>meta.schema_ver</c> byte.
/// Idempotent.
/// </summary>
public sealed class MigrationRunner
{
    private readonly IDbConnectionFactory _factory;

    public MigrationRunner(IDbConnectionFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        var current = await GetCurrentVersionAsync(conn, ct).ConfigureAwait(false);

        var migrations = DiscoverMigrations()
            .Where(m => m.Version > current)
            .OrderBy(m => m.Version)
            .ToList();

        foreach (var m in migrations)
        {
            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
            await conn.ExecuteAsync(new CommandDefinition(m.Sql, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            await conn.ExecuteAsync(new CommandDefinition(
                "INSERT OR REPLACE INTO meta(key, value) VALUES ('schema_ver', @v);",
                new { v = IntToBlob(m.Version) },
                tx,
                cancellationToken: ct)).ConfigureAwait(false);

            await tx.CommitAsync(ct).ConfigureAwait(false);
        }

        return migrations.Count;
    }

    private static async Task<int> GetCurrentVersionAsync(SqliteConnection conn, CancellationToken ct)
    {
        // meta might not exist yet; create only if missing so we can read schema_ver.
        await conn.ExecuteAsync(new CommandDefinition(
            "CREATE TABLE IF NOT EXISTS meta(key TEXT PRIMARY KEY, value BLOB NOT NULL) WITHOUT ROWID;",
            cancellationToken: ct)).ConfigureAwait(false);

        var blob = await conn.ExecuteScalarAsync<byte[]?>(new CommandDefinition(
            "SELECT value FROM meta WHERE key='schema_ver';",
            cancellationToken: ct)).ConfigureAwait(false);

        if (blob is null || blob.Length == 0) return 0;
        if (blob.Length == 4) return System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(blob);
        // Backward compat: prior 1-byte encoding.
        return blob[0];
    }

    private static byte[] IntToBlob(int value)
    {
        var b = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(b, value);
        return b;
    }

    private static IEnumerable<(int Version, string Sql)> DiscoverMigrations()
    {
        var asm = typeof(MigrationRunner).Assembly;
        const string prefix = "ToshanVault.Data.Schema.Migrations.";

        foreach (var name in asm.GetManifestResourceNames())
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (!name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)) continue;

            var fileName = name[prefix.Length..];
            var underscore = fileName.IndexOf('_');
            if (underscore <= 0) continue;
            if (!int.TryParse(fileName[..underscore], out var version)) continue;

            using var stream = asm.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            yield return (version, reader.ReadToEnd());
        }
    }
}
