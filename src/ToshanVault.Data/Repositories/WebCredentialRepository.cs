using Dapper;
using ToshanVault.Core.Models;
using ToshanVault.Data.Schema;

namespace ToshanVault.Data.Repositories;

/// <summary>
/// Read-side repository for <see cref="WebCredential"/>. Writes are owned by
/// <see cref="WebCredentialsService"/> because creating a credential always
/// implies creating/encrypting the linked vault_entry in the same transaction.
/// </summary>
public sealed class WebCredentialRepository
{
    private readonly IDbConnectionFactory _factory;

    public WebCredentialRepository(IDbConnectionFactory factory)
    {
        DapperSetup.EnsureInitialised();
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public async Task<IReadOnlyList<WebCredential>> GetByEntryAsync(long entryId, CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        var rows = await conn.QueryAsync<WebCredential>(new CommandDefinition(
            @"SELECT id, entry_id AS EntryId, owner,
                     vault_entry_id AS VaultEntryId, created_at AS CreatedAt, updated_at AS UpdatedAt
              FROM web_credential
              WHERE entry_id=@id
              ORDER BY owner;",
            new { id = entryId }, cancellationToken: ct)).ConfigureAwait(false);
        return rows.AsList();
    }

    public async Task<WebCredential?> GetAsync(long id, CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        return await conn.QuerySingleOrDefaultAsync<WebCredential>(new CommandDefinition(
            @"SELECT id, entry_id AS EntryId, owner,
                     vault_entry_id AS VaultEntryId, created_at AS CreatedAt, updated_at AS UpdatedAt
              FROM web_credential WHERE id=@id;",
            new { id }, cancellationToken: ct)).ConfigureAwait(false);
    }

    /// <summary>Deletes the credential row. The AFTER-DELETE trigger cascades
    /// through vault_entry → vault_field ONLY when vault_entry_id != entry_id
    /// (i.e. not the migrated first-owner credential).</summary>
    public async Task<int> DeleteAsync(long id, CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        return await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM web_credential WHERE id=@id;",
            new { id }, cancellationToken: ct)).ConfigureAwait(false);
    }
}
