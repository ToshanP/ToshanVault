using System.Security.Cryptography;
using System.Text;
using Dapper;
using ToshanVault.Core.Security;
using ToshanVault.Data.Schema;

namespace ToshanVault.Data.Repositories;

/// <summary>
/// Encrypted load/save of the single rich-text body field for a "general note"
/// vault_entry. Mirrors <see cref="WebCredentialsService"/> in shape (transactional
/// per-label upsert + ZeroMemory of plaintext) but degenerated to a single label
/// because notes only have one freeform body.
///
/// <para>The note's <c>Name</c> and <c>Owner</c> live on vault_entry directly via
/// <see cref="VaultEntryRepository"/>. Files attached to a note are stored in the
/// <c>attachment</c> table with <c>target_kind = "general_note"</c> and
/// <c>target_id = vault_entry.id</c>; the cascade trigger added in migration 016
/// cleans them up on note delete.</para>
/// </summary>
public sealed class GeneralNotesService
{
    public const string EntryKind  = "general_note";
    public const string LabelPrefix = "general_note.";
    public const string BodyLabel   = LabelPrefix + "body";

    private readonly IDbConnectionFactory _factory;
    private readonly Vault _vault;

    public GeneralNotesService(IDbConnectionFactory factory, Vault vault)
    {
        DapperSetup.EnsureInitialised();
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _vault   = vault   ?? throw new ArgumentNullException(nameof(vault));
    }

    /// <summary>Returns the decrypted body string, or null if the note has no body yet.</summary>
    public async Task<string?> LoadBodyAsync(long entryId, CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        var row = await conn.QuerySingleOrDefaultAsync<VaultFieldRow>(new CommandDefinition(
            @"SELECT id, entry_id, label, value_enc, iv, tag, is_secret
              FROM vault_field WHERE entry_id=@entryId AND label=@label;",
            new { entryId, label = BodyLabel }, cancellationToken: ct)).ConfigureAwait(false);
        if (row is null) return null;

        var pt = _vault.DecryptField(row.Iv, row.ValueEnc, row.Tag);
        try { return Encoding.UTF8.GetString(pt); }
        finally { CryptographicOperations.ZeroMemory(pt); }
    }

    /// <summary>
    /// Persist the note body, transactionally. Empty/null body deletes the
    /// existing field row rather than storing an empty encrypted blob, so a
    /// freshly-cleared note is indistinguishable from a never-saved one and
    /// doesn't leak ciphertext for empty content. Validates the parent
    /// vault_entry still exists - throws if it was deleted concurrently.
    /// </summary>
    public async Task SaveBodyAsync(long entryId, string? body, CancellationToken ct = default)
    {
        AesGcmCrypto.Sealed sealedBody = default;
        var hasBody = !string.IsNullOrEmpty(body);
        byte[]? plaintext = null;
        try
        {
            if (hasBody)
            {
                plaintext = Encoding.UTF8.GetBytes(body!);
                sealedBody = _vault.EncryptField(plaintext);
            }

            await using var conn = _factory.Open();
            await using var tx = (Microsoft.Data.Sqlite.SqliteTransaction)
                await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

            var exists = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT COUNT(1) FROM vault_entry WHERE id=@id;",
                new { id = entryId }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
            if (exists != 1) throw new InvalidOperationException($"VaultEntry {entryId} not found.");

            if (!hasBody)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    "DELETE FROM vault_field WHERE entry_id=@e AND label=@l;",
                    new { e = entryId, l = BodyLabel }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
            }
            else
            {
                var existingId = await conn.ExecuteScalarAsync<long?>(new CommandDefinition(
                    "SELECT id FROM vault_field WHERE entry_id=@e AND label=@l;",
                    new { e = entryId, l = BodyLabel }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

                if (existingId is null)
                {
                    await conn.ExecuteAsync(new CommandDefinition(
                        @"INSERT INTO vault_field(entry_id, label, value_enc, iv, tag, is_secret)
                          VALUES (@EntryId, @Label, @ValueEnc, @Iv, @Tag, 0);",
                        new { EntryId = entryId, Label = BodyLabel,
                              ValueEnc = sealedBody.Ciphertext, Iv = sealedBody.Iv, Tag = sealedBody.Tag },
                        transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
                }
                else
                {
                    await conn.ExecuteAsync(new CommandDefinition(
                        @"UPDATE vault_field
                          SET value_enc=@ValueEnc, iv=@Iv, tag=@Tag, is_secret=0
                          WHERE id=@Id;",
                        new { Id = existingId.Value,
                              ValueEnc = sealedBody.Ciphertext, Iv = sealedBody.Iv, Tag = sealedBody.Tag },
                        transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
                }
            }

            await conn.ExecuteAsync(new CommandDefinition(
                "UPDATE vault_entry SET updated_at=@now WHERE id=@id;",
                new { now = DateTimeOffset.UtcNow, id = entryId }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}
