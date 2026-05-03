using System.Security.Cryptography;
using System.Text;
using Dapper;
using ToshanVault.Core.Models;
using ToshanVault.Core.Security;
using ToshanVault.Data.Schema;

namespace ToshanVault.Data.Repositories;

/// <summary>
/// Atomic save of website / membership login fields tied to a vault_entry.
/// Mirrors <see cref="BankCredentialsService"/> but targets vault_entry rows
/// directly (no separate parent table). All field labels are namespaced under
/// <see cref="LabelPrefix"/> so they don't collide with bank_login.* fields or
/// any user-created vault fields.
/// </summary>
public sealed class WebCredentialsService
{
    public const string LabelPrefix = "web_login.";
    public const string NumberLabel             = LabelPrefix + "number";
    public const string WebsiteLabel            = LabelPrefix + "website";
    public const string AdditionalDetailsLabel  = LabelPrefix + "additional_details";
    public const string UsernameLabel           = LabelPrefix + "username";
    public const string PasswordLabel           = LabelPrefix + "password";
    public const string QuestionLabelPrefix     = LabelPrefix + "q";
    public const string AnswerLabelPrefix       = LabelPrefix + "a";
    public const int MaxQa = 10;
    public const string EntryKind = "web_login";

    /// <summary>Fixed list of owner labels offered in the UI dropdown.</summary>
    public static readonly IReadOnlyList<string> KnownOwners = BankCredentialsService.KnownOwners;

    private readonly IDbConnectionFactory _factory;
    private readonly Vault _vault;

    public WebCredentialsService(IDbConnectionFactory factory, Vault vault)
    {
        DapperSetup.EnsureInitialised();
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
    }

    public sealed record FieldSpec(string Label, string? Value, bool IsSecret);

    /// <summary>Decrypted fields keyed by namespaced label. Empty dict for new entries.</summary>
    public async Task<IReadOnlyDictionary<string, string>> LoadAsync(long entryId, CancellationToken ct = default)
        => await LoadInternalAsync(entryId, labelFilter: null, ct).ConfigureAwait(false);

    /// <summary>Loads only the requested labels. Use this for tile/list previews
    /// to avoid pulling password + security answers into memory just to render
    /// non-secret display fields.</summary>
    public async Task<IReadOnlyDictionary<string, string>> LoadLabelsAsync(
        long entryId, IReadOnlyCollection<string> labels, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(labels);
        if (labels.Count == 0) return new Dictionary<string, string>(StringComparer.Ordinal);
        return await LoadInternalAsync(entryId, labels, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyDictionary<string, string>> LoadInternalAsync(
        long entryId, IReadOnlyCollection<string>? labelFilter, CancellationToken ct)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var conn = _factory.Open();
        var rows = labelFilter is null
            ? await conn.QueryAsync<VaultFieldRow>(new CommandDefinition(
                @"SELECT id, entry_id, label, value_enc, iv, tag, is_secret
                  FROM vault_field WHERE entry_id=@entryId AND label LIKE @prefix;",
                new { entryId, prefix = LabelPrefix + "%" },
                cancellationToken: ct)).ConfigureAwait(false)
            : await conn.QueryAsync<VaultFieldRow>(new CommandDefinition(
                // Dapper expands the IN clause from the IEnumerable parameter.
                @"SELECT id, entry_id, label, value_enc, iv, tag, is_secret
                  FROM vault_field WHERE entry_id=@entryId AND label IN @labels;",
                new { entryId, labels = labelFilter },
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
    /// Persist all fields for one vault_entry in a single transaction.
    /// Empty/null values delete the existing field rather than store empty
    /// encrypted blobs. Validates the entry still exists before writing — if
    /// it was deleted concurrently the call throws and nothing is written.
    /// </summary>
    public async Task SaveAsync(
        long entryId,
        IReadOnlyList<FieldSpec> fields,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fields);

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
                var pt = Encoding.UTF8.GetBytes(f.Value);
                var blob = _vault.EncryptField(pt);
                sealedFields.Add((f.Label, blob, true, f.IsSecret, pt));
            }

            await using var conn = _factory.Open();
            await using var tx = (Microsoft.Data.Sqlite.SqliteTransaction)
                await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

            // Validate entry exists. We do not auto-create it here — the entry
            // is always created beforehand by VaultEntryRepository.InsertAsync
            // through the Add dialog, which collects name + owner.
            var exists = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT COUNT(1) FROM vault_entry WHERE id=@id;",
                new { id = entryId }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
            if (exists != 1) throw new InvalidOperationException($"VaultEntry {entryId} not found.");

            // Per-label upsert/delete inside the transaction.
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

            // Touch the entry's updated_at so the list reflects last edit.
            await conn.ExecuteAsync(new CommandDefinition(
                "UPDATE vault_entry SET updated_at=@now WHERE id=@id;",
                new { now = DateTimeOffset.UtcNow, id = entryId }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            foreach (var sf in sealedFields)
                if (sf.Plaintext is not null) CryptographicOperations.ZeroMemory(sf.Plaintext);
        }
    }

    /// <summary>
    /// Persist credential fields for one (entry, owner) pair via the web_credential
    /// table. Creates the web_credential row + its vault_entry on first save.
    /// For migrated entries the credential vault_entry_id may equal the parent entry_id.
    /// </summary>
    /// <returns>The vault_entry id used for this credential.</returns>
    public async Task<long> SaveCredentialsAsync(
        long entryId,
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

            var parentExists = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT COUNT(1) FROM vault_entry WHERE id=@id;",
                new { id = entryId }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
            if (parentExists != 1) throw new InvalidOperationException($"VaultEntry {entryId} not found.");

            var existing = await conn.QuerySingleOrDefaultAsync<(long CredId, long VaultEntryId)?>(new CommandDefinition(
                @"SELECT id AS CredId, vault_entry_id AS VaultEntryId
                  FROM web_credential
                  WHERE entry_id=@id AND owner=@owner;",
                new { id = entryId, owner }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            long credEntryId;
            if (existing is { } pair)
            {
                var entryExists = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
                    "SELECT COUNT(1) FROM vault_entry WHERE id=@id;",
                    new { id = pair.VaultEntryId }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
                if (entryExists == 1)
                {
                    credEntryId = pair.VaultEntryId;
                }
                else
                {
                    credEntryId = await CreateCredentialEntryAsync(conn, tx, entryName, ct).ConfigureAwait(false);
                    await conn.ExecuteAsync(new CommandDefinition(
                        "UPDATE web_credential SET vault_entry_id=@e, updated_at=@n WHERE id=@id;",
                        new { e = credEntryId, n = DateTimeOffset.UtcNow, id = pair.CredId },
                        transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
                }
            }
            else
            {
                credEntryId = await CreateCredentialEntryAsync(conn, tx, entryName, ct).ConfigureAwait(false);
                var now = DateTimeOffset.UtcNow;
                await conn.ExecuteAsync(new CommandDefinition(
                    @"INSERT INTO web_credential (entry_id, owner, vault_entry_id, created_at, updated_at)
                      VALUES (@e, @o, @ve, @n, @n);",
                    new { e = entryId, o = owner, ve = credEntryId, n = now },
                    transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
            }

            foreach (var sf in sealedFields)
            {
                if (!sf.HasValue)
                {
                    await conn.ExecuteAsync(new CommandDefinition(
                        "DELETE FROM vault_field WHERE entry_id=@e AND label=@l;",
                        new { e = credEntryId, l = sf.Label }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
                    continue;
                }

                var existingId = await conn.ExecuteScalarAsync<long?>(new CommandDefinition(
                    "SELECT id FROM vault_field WHERE entry_id=@e AND label=@l;",
                    new { e = credEntryId, l = sf.Label }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

                if (existingId is null)
                {
                    await conn.ExecuteAsync(new CommandDefinition(
                        @"INSERT INTO vault_field(entry_id, label, value_enc, iv, tag, is_secret)
                          VALUES (@EntryId, @Label, @ValueEnc, @Iv, @Tag, @IsSecret);",
                        new { EntryId = credEntryId, Label = sf.Label,
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
                new { now = DateTimeOffset.UtcNow, id = credEntryId },
                transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            await tx.CommitAsync(ct).ConfigureAwait(false);
            return credEntryId;
        }
        finally
        {
            foreach (var sf in sealedFields)
                if (sf.Plaintext is not null) CryptographicOperations.ZeroMemory(sf.Plaintext);
        }
    }

    private static async Task<long> CreateCredentialEntryAsync(
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
            new { kind = "web_credential", name, now },
            transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
    }
}
