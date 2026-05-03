using System.Security.Cryptography;
using System.Text;
using Dapper;
using ToshanVault.Core.Models;
using ToshanVault.Core.Security;
using ToshanVault.Data.Schema;

namespace ToshanVault.Data.Repositories;

/// <summary>
/// Encrypted credentials + notes for an <see cref="Insurance"/> policy. Backed
/// by a vault_entry of kind <c>insurance_login</c> linked from
/// <see cref="Insurance.VaultEntryId"/>; vault_field rows hold the actual
/// AES-GCM-encrypted blobs under labels namespaced by <see cref="LabelPrefix"/>.
///
/// Single-credential model (one username/password/notes per policy). For
/// joint policies we considered multi-owner like bank accounts but it adds
/// significant UI weight for a relatively rare case — defer until a real
/// example surfaces.
/// </summary>
public sealed class InsuranceCredentialsService
{
    public const string LabelPrefix    = "insurance.";
    public const string UsernameLabel  = LabelPrefix + "username";
    public const string PasswordLabel  = LabelPrefix + "password";
    public const string NotesLabel     = LabelPrefix + "notes";

    private readonly IDbConnectionFactory _factory;
    private readonly Vault _vault;
    private readonly VaultEntryRepository _entries;
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
        _entries = entries ?? throw new ArgumentNullException(nameof(entries));
        _insRepo = insRepo ?? throw new ArgumentNullException(nameof(insRepo));
    }

    public sealed record FieldSpec(string Label, string? Value, bool IsSecret);

    /// <summary>Loads decrypted insurance.* fields for the policy. Returns an
    /// empty dict when the policy has no credentials entry yet.</summary>
    public async Task<IReadOnlyDictionary<string, string>> LoadAsync(long insuranceId, CancellationToken ct = default)
    {
        var ins = await _insRepo.GetAsync(insuranceId, ct).ConfigureAwait(false)
                  ?? throw new InvalidOperationException($"Insurance {insuranceId} not found.");
        if (ins.VaultEntryId is null) return new Dictionary<string, string>(StringComparer.Ordinal);

        return await LoadByEntryAsync(ins.VaultEntryId.Value, ct).ConfigureAwait(false);
    }

    /// <summary>Loads only the requested labels for tile previews so we don't
    /// pull username/password into memory just to render a Notes excerpt or
    /// vice versa. Mirrors the pattern in <see cref="WebCredentialsService.LoadLabelsAsync"/>.</summary>
    public async Task<IReadOnlyDictionary<string, string>> LoadLabelsAsync(
        long insuranceId, IReadOnlyCollection<string> labels, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(labels);
        if (labels.Count == 0) return new Dictionary<string, string>(StringComparer.Ordinal);

        var ins = await _insRepo.GetAsync(insuranceId, ct).ConfigureAwait(false)
                  ?? throw new InvalidOperationException($"Insurance {insuranceId} not found.");
        if (ins.VaultEntryId is null) return new Dictionary<string, string>(StringComparer.Ordinal);

        return await LoadInternalAsync(ins.VaultEntryId.Value, labels, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyDictionary<string, string>> LoadByEntryAsync(long entryId, CancellationToken ct)
        => await LoadInternalAsync(entryId, labelFilter: null, ct).ConfigureAwait(false);

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

    /// <summary>Persists username / password / notes for a policy. Auto-creates
    /// the underlying vault_entry on first save and back-fills
    /// <see cref="Insurance.VaultEntryId"/>. Empty values delete the field
    /// rather than store empty encrypted blobs.</summary>
    public async Task SaveAsync(long insuranceId, IReadOnlyList<FieldSpec> fields, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var ins = await _insRepo.GetAsync(insuranceId, ct).ConfigureAwait(false)
                  ?? throw new InvalidOperationException($"Insurance {insuranceId} not found.");

        // Lazy-create the credentials entry the first time we actually save
        // anything non-empty. This keeps the vault clean of orphan entries
        // when the user only ever fills in display fields.
        var hasAny = fields.Any(f => !string.IsNullOrEmpty(f.Value));
        long entryId;
        if (ins.VaultEntryId is null)
        {
            if (!hasAny) return; // nothing to write, nothing to create
            var entry = new VaultEntry
            {
                Kind = Insurance.CredentialsEntryKind,
                Name = ins.InsurerCompany +
                    (string.IsNullOrWhiteSpace(ins.PolicyNumber) ? "" : " · " + ins.PolicyNumber),
            };
            entryId = await _entries.InsertAsync(entry, ct).ConfigureAwait(false);
            ins.VaultEntryId = entryId;
            await _insRepo.UpdateAsync(ins, ct).ConfigureAwait(false);
        }
        else
        {
            entryId = ins.VaultEntryId.Value;
        }

        // Encrypt outside the transaction (fast-fail on lock; minimises hold time).
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

            // Re-validate the entry still exists in case something deleted it
            // between our auto-create above and now (defence-in-depth).
            var exists = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT COUNT(1) FROM vault_entry WHERE id=@id;",
                new { id = entryId }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
            if (exists != 1) throw new InvalidOperationException($"VaultEntry {entryId} not found.");

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
        }
        finally
        {
            foreach (var sf in sealedFields)
                if (sf.Plaintext is not null) CryptographicOperations.ZeroMemory(sf.Plaintext);
        }
    }

    /// <summary>
    /// One-time migration: decrypts old encrypted notes from vault_field and
    /// copies them into the plaintext insurance.notes column. Idempotent —
    /// only processes rows where insurance.notes IS NULL and an encrypted note exists.
    /// After copying, deletes the vault_field row (notes don't need encryption).
    /// </summary>
    public async Task MigrateNotesToColumnAsync(CancellationToken ct = default)
    {
        // Find insurance entries that have a vault entry but no plaintext notes yet.
        await using var conn = _factory.Open();
        var candidates = await conn.QueryAsync<(long Id, long VaultEntryId)>(new CommandDefinition(
            @"SELECT id, vault_entry_id FROM insurance
              WHERE vault_entry_id IS NOT NULL AND (notes IS NULL OR notes = '');",
            cancellationToken: ct)).ConfigureAwait(false);

        foreach (var (insId, entryId) in candidates)
        {
            // Check if an encrypted notes field exists for this entry.
            var row = await conn.QuerySingleOrDefaultAsync<VaultFieldRow>(new CommandDefinition(
                @"SELECT id, entry_id, label, value_enc, iv, tag, is_secret
                  FROM vault_field WHERE entry_id=@entryId AND label=@label;",
                new { entryId, label = NotesLabel },
                cancellationToken: ct)).ConfigureAwait(false);

            if (row is null) continue;

            // Decrypt the notes value.
            var pt = _vault.DecryptField(row.Iv, row.ValueEnc, row.Tag);
            string plaintext;
            try { plaintext = Encoding.UTF8.GetString(pt); }
            finally { CryptographicOperations.ZeroMemory(pt); }

            if (string.IsNullOrWhiteSpace(plaintext)) continue;

            // Write to the plaintext column and delete the encrypted vault_field row.
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
