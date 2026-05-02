using System.Security.Cryptography;
using System.Text;
using Dapper;
using ToshanVault.Core.Models;
using ToshanVault.Core.Security;
using ToshanVault.Data.Schema;

namespace ToshanVault.Data.Repositories;

/// <summary>
/// Handles encrypted-at-rest vault fields. Plaintext flows in/out only at the
/// API boundary; ciphertext + IV + tag are persisted. Encryption uses the
/// supplied <see cref="Vault"/> (which must be unlocked).
/// </summary>
public sealed class VaultFieldRepository
{
    private readonly IDbConnectionFactory _factory;
    private readonly Vault _vault;

    public VaultFieldRepository(IDbConnectionFactory factory, Vault vault)
    {
        DapperSetup.EnsureInitialised();
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
    }

    public async Task<long> InsertAsync(VaultField f, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(f);
        var plaintext = Encoding.UTF8.GetBytes(f.Value);
        try
        {
            var sealedBlob = _vault.EncryptField(plaintext);
            var row = new VaultFieldRow
            {
                EntryId = f.EntryId,
                Label = f.Label,
                ValueEnc = sealedBlob.Ciphertext,
                Iv = sealedBlob.Iv,
                Tag = sealedBlob.Tag,
                IsSecret = f.IsSecret,
            };
            await using var conn = _factory.Open();
            var id = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
                @"INSERT INTO vault_field(entry_id, label, value_enc, iv, tag, is_secret)
                  VALUES (@EntryId, @Label, @ValueEnc, @Iv, @Tag, @IsSecret);
                  SELECT last_insert_rowid();",
                row, cancellationToken: ct)).ConfigureAwait(false);
            f.Id = id;
            return id;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public async Task UpdateAsync(VaultField f, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(f);
        var plaintext = Encoding.UTF8.GetBytes(f.Value);
        try
        {
            var sealedBlob = _vault.EncryptField(plaintext);
            var row = new VaultFieldRow
            {
                Id = f.Id,
                EntryId = f.EntryId,
                Label = f.Label,
                ValueEnc = sealedBlob.Ciphertext,
                Iv = sealedBlob.Iv,
                Tag = sealedBlob.Tag,
                IsSecret = f.IsSecret,
            };
            await using var conn = _factory.Open();
            var rows = await conn.ExecuteAsync(new CommandDefinition(
                @"UPDATE vault_field
                  SET entry_id=@EntryId, label=@Label, value_enc=@ValueEnc,
                      iv=@Iv, tag=@Tag, is_secret=@IsSecret
                  WHERE id=@Id;",
                row, cancellationToken: ct)).ConfigureAwait(false);
            if (rows == 0) throw new InvalidOperationException($"VaultField {f.Id} not found.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public async Task<int> DeleteAsync(long id, CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        return await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM vault_field WHERE id=@id;", new { id }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<VaultField?> GetAsync(long id, CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        var row = await conn.QuerySingleOrDefaultAsync<VaultFieldRow>(new CommandDefinition(
            @"SELECT id, entry_id, label, value_enc, iv, tag, is_secret
              FROM vault_field WHERE id=@id;",
            new { id }, cancellationToken: ct)).ConfigureAwait(false);
        return row is null ? null : Decrypt(row);
    }

    public async Task<IReadOnlyList<VaultField>> GetByEntryAsync(long entryId, CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        var rows = await conn.QueryAsync<VaultFieldRow>(new CommandDefinition(
            @"SELECT id, entry_id, label, value_enc, iv, tag, is_secret
              FROM vault_field WHERE entry_id=@entryId ORDER BY label;",
            new { entryId }, cancellationToken: ct)).ConfigureAwait(false);
        return rows.Select(Decrypt).ToList();
    }

    private VaultField Decrypt(VaultFieldRow row)
    {
        var plaintext = _vault.DecryptField(row.Iv, row.ValueEnc, row.Tag);
        try
        {
            return new VaultField
            {
                Id = row.Id,
                EntryId = row.EntryId,
                Label = row.Label,
                Value = Encoding.UTF8.GetString(plaintext),
                IsSecret = row.IsSecret,
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}
