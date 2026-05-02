using System.Security.Cryptography;
using System.Text;
using Dapper;
using ToshanVault.Core.Models;
using ToshanVault.Core.Security;
using ToshanVault.Data.Schema;

namespace ToshanVault.Data.Repositories;

/// <summary>
/// Atomic save of internet-banking credentials for one (bank account, owner)
/// pair. Wraps bank_account_credential + vault_entry creation/linking + per-
/// field upsert/delete in a single SQLite transaction so an idle-lock or
/// exception mid-save can never leave the vault in a half-updated state. All
/// field labels are namespaced under <see cref="LabelPrefix"/> to avoid
/// collision with user-created fields.
/// </summary>
public sealed class BankCredentialsService
{
    /// <summary>Namespace prefix for all credential field labels.</summary>
    public const string LabelPrefix = "bank_login.";
    public const string UsernameLabel = LabelPrefix + "username";
    public const string ClientIdLabel = LabelPrefix + "client_id";
    public const string PasswordLabel = LabelPrefix + "password";
    public const string CardPinLabel = LabelPrefix + "card_pin";
    public const string PhonePinLabel = LabelPrefix + "phone_pin";
    public const string NotesLabel = LabelPrefix + "notes";
    public const string QuestionLabelPrefix = LabelPrefix + "q";
    public const string AnswerLabelPrefix = LabelPrefix + "a";
    public const int MaxQa = 10;

    /// <summary>Fixed list of owner labels offered in the UI dropdown.
    /// The DB column is plain TEXT so this can grow without a migration.</summary>
    public static readonly IReadOnlyList<string> KnownOwners = new[]
    {
        "Toshan", "Devangini", "Prachi", "Saloni",
    };

    private readonly IDbConnectionFactory _factory;
    private readonly Vault _vault;

    public BankCredentialsService(IDbConnectionFactory factory, Vault vault)
    {
        DapperSetup.EnsureInitialised();
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
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
    /// Persist all credential fields for one (bank account, owner) pair in a
    /// single transaction. Creates the bank_account_credential row + its
    /// vault_entry on first save (idempotent on the unique key). Empty/null
    /// values delete the existing field rather than store empty encrypted blobs.
    /// </summary>
    /// <returns>The vault_entry id used for this credential (existing or new).</returns>
    public async Task<long> SaveAsync(
        long bankAccountId,
        string owner,
        string entryName,
        IReadOnlyList<FieldSpec> fields,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fields);
        if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentException("Owner required.", nameof(owner));

        // Encrypt outside the transaction (fast-fail on lock; minimises tx hold time).
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
                // Per-iteration try/finally so plaintext is zeroed even if
                // EncryptField throws (e.g. VaultLockedException) before we
                // can hand ownership to sealedFields for the outer cleanup.
                byte[]? pt = null;
                try
                {
                    pt = Encoding.UTF8.GetBytes(f.Value);
                    var blob = _vault.EncryptField(pt);
                    sealedFields.Add((f.Label, blob, true, f.IsSecret, pt));
                    pt = null; // ownership transferred to sealedFields
                }
                finally
                {
                    if (pt is not null) CryptographicOperations.ZeroMemory(pt);
                }
            }

            await using var conn = _factory.Open();
            await using var tx = (Microsoft.Data.Sqlite.SqliteTransaction)
                await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

            // 1. Verify the bank_account exists (FK violations would otherwise
            //    only surface on commit with a less actionable error message).
            var accountExists = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT COUNT(1) FROM bank_account WHERE id=@id;",
                new { id = bankAccountId }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
            if (accountExists == 0) throw new InvalidOperationException($"BankAccount {bankAccountId} not found.");

            // 2. Lookup or create the credential row + its vault_entry. The
            //    UNIQUE(bank_account_id, owner) index guarantees one row per pair.
            var existing = await conn.QuerySingleOrDefaultAsync<(long CredId, long EntryId)?>(new CommandDefinition(
                @"SELECT id AS CredId, vault_entry_id AS EntryId
                  FROM bank_account_credential
                  WHERE bank_account_id=@id AND owner=@owner;",
                new { id = bankAccountId, owner }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            long entryId;
            if (existing is { } pair)
            {
                // Validate the linked entry still exists (defensive: a manual DB
                // edit could orphan us). Recreate if missing and update the link.
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
                        "UPDATE bank_account_credential SET vault_entry_id=@e, updated_at=@n WHERE id=@id;",
                        new { e = entryId, n = DateTimeOffset.UtcNow, id = pair.CredId },
                        transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
                }
            }
            else
            {
                entryId = await CreateVaultEntryAsync(conn, tx, entryName, ct).ConfigureAwait(false);
                var now = DateTimeOffset.UtcNow;
                await conn.ExecuteAsync(new CommandDefinition(
                    @"INSERT INTO bank_account_credential
                        (bank_account_id, owner, vault_entry_id, created_at, updated_at)
                      VALUES (@a, @o, @e, @n, @n);",
                    new { a = bankAccountId, o = owner, e = entryId, n = now },
                    transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
            }

            // 3. Per-label upsert/delete inside the same transaction.
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
                        @"UPDATE vault_field
                          SET value_enc=@ValueEnc, iv=@Iv, tag=@Tag, is_secret=@IsSecret
                          WHERE id=@Id;",
                        new { Id = existingId.Value,
                              ValueEnc = sf.Sealed.Ciphertext, Iv = sf.Sealed.Iv, Tag = sf.Sealed.Tag,
                              IsSecret = sf.IsSecret }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
                }
            }

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
        Microsoft.Data.Sqlite.SqliteConnection conn,
        Microsoft.Data.Sqlite.SqliteTransaction tx,
        string entryName,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        return await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            @"INSERT INTO vault_entry(kind, name, category, tags, created_at, updated_at)
              VALUES ('bank_login', @Name, NULL, NULL, @Now, @Now);
              SELECT last_insert_rowid();",
            new { Name = entryName, Now = now }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
    }
}
