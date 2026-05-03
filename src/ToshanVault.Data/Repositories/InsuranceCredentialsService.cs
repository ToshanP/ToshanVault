using System.Security.Cryptography;
using System.Text;
using Dapper;
using ToshanVault.Core.Models;
using ToshanVault.Core.Security;
using ToshanVault.Data.Schema;

namespace ToshanVault.Data.Repositories;

/// <summary>
/// Atomic save of insurance portal credentials for one (insurance, owner) pair.
/// Mirrors <see cref="BankCredentialsService"/>: wraps insurance_credential +
/// vault_entry creation/linking + per-field upsert/delete in a single SQLite
/// transaction. All field labels are namespaced under <see cref="LabelPrefix"/>.
/// </summary>
public sealed class InsuranceCredentialsService
{
    public const string LabelPrefix    = "insurance.";
    public const string UsernameLabel  = LabelPrefix + "username";
    public const string PasswordLabel  = LabelPrefix + "password";
    public const string NotesLabel     = LabelPrefix + "notes";
    public const string QuestionLabelPrefix = LabelPrefix + "q";
    public const string AnswerLabelPrefix   = LabelPrefix + "a";
    public const int MaxQa = 10;

    /// <summary>Fixed list of owner labels offered in the UI dropdown.</summary>
    public static readonly IReadOnlyList<string> KnownOwners = BankCredentialsService.KnownOwners;

    private readonly IDbConnectionFactory _factory;
    private readonly Vault _vault;
    private readonly InsuranceRepository _insRepo;

    public InsuranceCredentialsService(
        IDbConnectionFactory factory,
        Vault vault,
        VaultEntryRepository entries,
        InsuranceRepository insRepo)
    {
        DapperSetup.EnsureInitialised();
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _vault   = vault   ?? throw new ArgumentNullException(nameof(vault));
        _insRepo = insRepo ?? throw new ArgumentNullException(nameof(insRepo));
        // entries kept for signature compat; not needed in new multi-owner flow
    }

    public sealed record FieldSpec(string Label, string? Value, bool IsSecret);

    /// <summary>Decrypted credential fields keyed by namespaced label for
    /// the given vault_entry. Returns empty when the entry id is null.</summary>
    public async Task<IReadOnlyDictionary<string, string>> LoadAsync(long? vaultEntryId, CancellationToken ct = default)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (vaultEntryId is null) return result;

        await using var conn = _factory.Open();
        var rows = await conn.QueryAsync<VaultFieldRow>(new CommandDefinition(
            @"SELECT id, entry_id, label, value_enc, iv, tag, is_secret
              FROM vault_field WHERE entry_id=@entryId AND label LIKE @prefix;",
            new { entryId = vaultEntryId.Value, prefix = LabelPrefix + "%" },
            cancellationToken: ct)).ConfigureAwait(false);

        foreach (var row in rows)
        {
            var pt = _vault.DecryptField(row.Iv, row.ValueEnc, row.Tag);
            try { result[row.Label] = Encoding.UTF8.GetString(pt); }
            finally { CryptographicOperations.ZeroMemory(pt); }
        }
        return result;
    }

    /// <summary>
    /// Persist all credential fields for one (insurance, owner) pair in a
    /// single transaction. Creates the insurance_credential row + its
    /// vault_entry on first save (idempotent on the unique key).
    /// </summary>
    /// <returns>The vault_entry id used for this credential.</returns>
    public async Task<long> SaveAsync(
        long insuranceId,
        string owner,
        string entryName,
        IReadOnlyList<FieldSpec> fields,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fields);
        if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentException("Owner required.", nameof(owner));

        var sealedFields = new List<(string Label, AesGcmCrypto.Sealed Sealed, bool HasValue, bool IsSecret, byte[]? Plaintext)>(fields.Count);
        try
        {
            foreach (var f in fields)
            {
                if (string.IsNullOrEmpty(f.Value))
                {
                    sealedFields.Add((f.Label, default, false, f.IsSecret, null));
                    continue;
                }
                byte[]? pt = null;
                try
                {
                    pt = Encoding.UTF8.GetBytes(f.Value);
                    var blob = _vault.EncryptField(pt);
                    sealedFields.Add((f.Label, blob, true, f.IsSecret, pt));
                    pt = null;
                }
                finally
                {
                    if (pt is not null) CryptographicOperations.ZeroMemory(pt);
                }
            }

            await using var conn = _factory.Open();
            await using var tx = (Microsoft.Data.Sqlite.SqliteTransaction)
                await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

            var insExists = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT COUNT(1) FROM insurance WHERE id=@id;",
                new { id = insuranceId }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
            if (insExists == 0) throw new InvalidOperationException($"Insurance {insuranceId} not found.");

            var existing = await conn.QuerySingleOrDefaultAsync<(long CredId, long EntryId)?>(new CommandDefinition(
                @"SELECT id AS CredId, vault_entry_id AS EntryId
                  FROM insurance_credential
                  WHERE insurance_id=@id AND owner=@owner;",
                new { id = insuranceId, owner }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            long entryId;
            if (existing is { } pair)
            {
                var entryExists = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
                    "SELECT COUNT(1) FROM vault_entry WHERE id=@id;",
                    new { id = pair.EntryId }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
                if (entryExists == 1)
                {
                    entryId = pair.EntryId;
                }
                else
                {
                    entryId = await CreateVaultEntryAsync(conn, tx, entryName, ct).ConfigureAwait(false);
                    await conn.ExecuteAsync(new CommandDefinition(
                        "UPDATE insurance_credential SET vault_entry_id=@e, updated_at=@n WHERE id=@id;",
                        new { e = entryId, n = DateTimeOffset.UtcNow, id = pair.CredId },
                        transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
                }
            }
            else
            {
                entryId = await CreateVaultEntryAsync(conn, tx, entryName, ct).ConfigureAwait(false);
                var now = DateTimeOffset.UtcNow;
                await conn.ExecuteAsync(new CommandDefinition(
                    @"INSERT INTO insurance_credential
                        (insurance_id, owner, vault_entry_id, created_at, updated_at)
                      VALUES (@a, @o, @e, @n, @n);",
                    new { a = insuranceId, o = owner, e = entryId, n = now },
                    transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
            }

            foreach (var sf in sealedFields)
            {
                if (!sf.HasValue)
                {
                    await conn.ExecuteAsync(new CommandDefinition(
                        "DELETE FROM vault_field WHERE entry_id=@e AND label=@l;",
                        new { e = entryId, l = sf.Label }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
                    continue;
                }

                var existingId = await conn.ExecuteScalarAsync<long?>(new CommandDefinition(
                    "SELECT id FROM vault_field WHERE entry_id=@e AND label=@l;",
                    new { e = entryId, l = sf.Label }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

                if (existingId is null)
                {
                    await conn.ExecuteAsync(new CommandDefinition(
                        @"INSERT INTO vault_field(entry_id, label, value_enc, iv, tag, is_secret)
                          VALUES (@EntryId, @Label, @ValueEnc, @Iv, @Tag, @IsSecret);",
                        new { EntryId = entryId, Label = sf.Label,
                              ValueEnc = sf.Sealed.Ciphertext, Iv = sf.Sealed.Iv, Tag = sf.Sealed.Tag,
                              IsSecret = sf.IsSecret }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
                }
                else
                {
                    await conn.ExecuteAsync(new CommandDefinition(
                        @"UPDATE vault_field SET value_enc=@ValueEnc, iv=@Iv, tag=@Tag, is_secret=@IsSecret
                          WHERE id=@Id;",
                        new { Id = existingId.Value,
                              ValueEnc = sf.Sealed.Ciphertext, Iv = sf.Sealed.Iv, Tag = sf.Sealed.Tag,
                              IsSecret = sf.IsSecret }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
                }
            }

            await conn.ExecuteAsync(new CommandDefinition(
                "UPDATE vault_entry SET updated_at=@now WHERE id=@id;",
                new { now = DateTimeOffset.UtcNow, id = entryId },
                transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            await tx.CommitAsync(ct).ConfigureAwait(false);
            return entryId;
        }
        finally
        {
            foreach (var sf in sealedFields)
                if (sf.Plaintext is not null) CryptographicOperations.ZeroMemory(sf.Plaintext);
        }
    }

    private static async Task<long> CreateVaultEntryAsync(
        System.Data.Common.DbConnection conn,
        System.Data.Common.DbTransaction tx,
        string name,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        return await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            @"INSERT INTO vault_entry(kind, name, created_at, updated_at)
              VALUES (@kind, @name, @now, @now);
              SELECT last_insert_rowid();",
            new { kind = Insurance.CredentialsEntryKind, name, now },
            transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
    }

    /// <summary>
    /// One-time migration: decrypts old encrypted notes from vault_field and
    /// copies them into the plaintext insurance.notes column. Idempotent.
    /// </summary>
    public async Task MigrateNotesToColumnAsync(CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        var candidates = await conn.QueryAsync<(long Id, long VaultEntryId)>(new CommandDefinition(
            @"SELECT id, vault_entry_id FROM insurance
              WHERE vault_entry_id IS NOT NULL AND (notes IS NULL OR notes = '');",
            cancellationToken: ct)).ConfigureAwait(false);

        foreach (var (insId, entryId) in candidates)
        {
            var row = await conn.QuerySingleOrDefaultAsync<VaultFieldRow>(new CommandDefinition(
                @"SELECT id, entry_id, label, value_enc, iv, tag, is_secret
                  FROM vault_field WHERE entry_id=@entryId AND label=@label;",
                new { entryId, label = NotesLabel },
                cancellationToken: ct)).ConfigureAwait(false);

            if (row is null) continue;

            var pt = _vault.DecryptField(row.Iv, row.ValueEnc, row.Tag);
            string plaintext;
            try { plaintext = Encoding.UTF8.GetString(pt); }
            finally { CryptographicOperations.ZeroMemory(pt); }

            if (string.IsNullOrWhiteSpace(plaintext)) continue;

            await conn.ExecuteAsync(new CommandDefinition(
                "UPDATE insurance SET notes=@notes, updated_at=@now WHERE id=@id;",
                new { notes = plaintext, now = DateTimeOffset.UtcNow, id = insId },
                cancellationToken: ct)).ConfigureAwait(false);

            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM vault_field WHERE id=@id;",
                new { id = row.Id },
                cancellationToken: ct)).ConfigureAwait(false);
        }
    }
}
