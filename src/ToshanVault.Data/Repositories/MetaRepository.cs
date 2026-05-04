using Dapper;
using ToshanVault.Core.Security;
using ToshanVault.Data.Schema;

namespace ToshanVault.Data.Repositories;

/// <summary>
/// Reads/writes the vault's crypto material from the <c>meta</c> table.
/// All values are stored as BLOBs. Integer ints (iteration counts) are
/// serialised big-endian to a 4-byte BLOB so the schema is uniformly typed.
/// </summary>
public sealed class MetaRepository : IMetaStore
{
    private const string KeySalt = "salt";
    private const string KeyVerifier = "pwd_verifier";
    private const string KeyVerifierIter = "pwd_verifier_iter";
    private const string KeyKekIter = "kek_iter";
    private const string KeyDekWrapped = "dek_wrapped";
    private const string KeyDekIv = "dek_iv";
    private const string KeyDekTag = "dek_tag";

    private const string KeyHelloBlob = "hello_blob";

    private readonly IDbConnectionFactory _factory;

    public MetaRepository(IDbConnectionFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public async Task<bool> IsInitialisedAsync(CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        var blob = await conn.ExecuteScalarAsync<byte[]?>(new CommandDefinition(
            "SELECT value FROM meta WHERE key=@k;",
            new { k = KeySalt },
            cancellationToken: ct)).ConfigureAwait(false);
        return blob is { Length: > 0 };
    }

    public async Task WriteInitialAsync(VaultMeta meta, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(meta);

        await using var conn = _factory.Open();
        await using var tx = (Microsoft.Data.Sqlite.SqliteTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

        var rows = new List<MetaRow>
        {
            new(KeySalt, meta.Salt),
            new(KeyVerifier, meta.PwdVerifier),
            new(KeyVerifierIter, IntToBlob(meta.VerifierIterations)),
            new(KeyKekIter, IntToBlob(meta.KekIterations)),
            new(KeyDekWrapped, meta.DekWrapped),
            new(KeyDekIv, meta.DekIv),
            new(KeyDekTag, meta.DekTag),
        };
        if (meta.HelloBlob is not null)
            rows.Add(new MetaRow(KeyHelloBlob, meta.HelloBlob));

        // Plain INSERT — duplicate keys raise UNIQUE constraint, surfaced as
        // VaultAlreadyInitialisedException. This is the DB-level guarantee that
        // we never silently overwrite vault crypto material.
        const string sql = "INSERT INTO meta(key, value) VALUES (@Key, @Value);";
        try
        {
            foreach (var row in rows)
            {
                await conn.ExecuteAsync(new CommandDefinition(sql, row, tx, cancellationToken: ct)).ConfigureAwait(false);
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 19 /* SQLITE_CONSTRAINT */)
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            throw new VaultAlreadyInitialisedException();
        }
    }

    public async Task<VaultMeta> ReadAsync(CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        var rows = (await conn.QueryAsync<MetaRow>(new CommandDefinition(
            "SELECT key AS Key, value AS Value FROM meta WHERE key IN (@s,@v,@vi,@ki,@dw,@di,@dt,@hb);",
            new
            {
                s = KeySalt,
                v = KeyVerifier,
                vi = KeyVerifierIter,
                ki = KeyKekIter,
                dw = KeyDekWrapped,
                di = KeyDekIv,
                dt = KeyDekTag,
                hb = KeyHelloBlob,
            },
            cancellationToken: ct)).ConfigureAwait(false))
            .ToDictionary(r => r.Key, r => r.Value, StringComparer.Ordinal);

        byte[] Get(string key) => rows.TryGetValue(key, out var v)
            ? v
            : throw new InvalidOperationException($"Required meta key '{key}' missing.");

        return new VaultMeta
        {
            Salt = Get(KeySalt),
            VerifierIterations = BlobToInt(Get(KeyVerifierIter)),
            PwdVerifier = Get(KeyVerifier),
            KekIterations = BlobToInt(Get(KeyKekIter)),
            DekIv = Get(KeyDekIv),
            DekWrapped = Get(KeyDekWrapped),
            DekTag = Get(KeyDekTag),
            HelloBlob = rows.TryGetValue(KeyHelloBlob, out var hb) ? hb : null,
        };
    }

    private static byte[] IntToBlob(int value)
    {
        var b = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(b, value);
        return b;
    }

    private static int BlobToInt(byte[] blob)
    {
        if (blob is null || blob.Length != 4)
            throw new InvalidOperationException("Iteration meta value must be a 4-byte big-endian int.");
        return System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(blob);
    }

    public async Task UpdateMetaAsync(VaultMeta meta, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(meta);

        await using var conn = _factory.Open();
        await using var tx = (Microsoft.Data.Sqlite.SqliteTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

        var rows = new List<MetaRow>
        {
            new(KeySalt, meta.Salt),
            new(KeyVerifier, meta.PwdVerifier),
            new(KeyVerifierIter, IntToBlob(meta.VerifierIterations)),
            new(KeyKekIter, IntToBlob(meta.KekIterations)),
            new(KeyDekWrapped, meta.DekWrapped),
            new(KeyDekIv, meta.DekIv),
            new(KeyDekTag, meta.DekTag),
        };

        const string sql = "UPDATE meta SET value = @Value WHERE key = @Key;";
        foreach (var row in rows)
        {
            var affected = await conn.ExecuteAsync(new CommandDefinition(sql, row, tx, cancellationToken: ct)).ConfigureAwait(false);
            if (affected == 0)
                throw new InvalidOperationException($"Meta key '{row.Key}' not found — vault may not be initialised.");
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    private sealed record MetaRow(string Key, byte[] Value);
}
