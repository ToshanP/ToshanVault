using Dapper;
using ToshanVault.Core.Models;
using ToshanVault.Data.Schema;

namespace ToshanVault.Data.Repositories;

/// <summary>
/// Read-side repository for <see cref="BankAccountCredential"/>. Writes are
/// owned by <see cref="BankCredentialsService"/> because creating a credential
/// always implies creating/encrypting the linked vault_entry in the same
/// transaction. This repo only lists/looks up existing rows and handles
/// the explicit delete path.
/// </summary>
public sealed class BankAccountCredentialRepository
{
    private readonly IDbConnectionFactory _factory;

    public BankAccountCredentialRepository(IDbConnectionFactory factory)
    {
        DapperSetup.EnsureInitialised();
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public async Task<IReadOnlyList<BankAccountCredential>> GetByAccountAsync(long bankAccountId, CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        var rows = await conn.QueryAsync<BankAccountCredential>(new CommandDefinition(
            @"SELECT id, bank_account_id AS BankAccountId, owner,
                     vault_entry_id AS VaultEntryId, created_at AS CreatedAt, updated_at AS UpdatedAt
              FROM bank_account_credential
              WHERE bank_account_id=@id
              ORDER BY owner;",
            new { id = bankAccountId }, cancellationToken: ct)).ConfigureAwait(false);
        return rows.AsList();
    }

    public async Task<BankAccountCredential?> GetAsync(long id, CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        return await conn.QuerySingleOrDefaultAsync<BankAccountCredential>(new CommandDefinition(
            @"SELECT id, bank_account_id AS BankAccountId, owner,
                     vault_entry_id AS VaultEntryId, created_at AS CreatedAt, updated_at AS UpdatedAt
              FROM bank_account_credential WHERE id=@id;",
            new { id }, cancellationToken: ct)).ConfigureAwait(false);
    }

    /// <summary>Deletes the credential row. The AFTER-DELETE trigger
    /// (migration 006) cascades through <c>vault_entry</c> → <c>vault_field</c>
    /// so all encrypted blobs for this credential are removed atomically.</summary>
    public async Task<int> DeleteAsync(long id, CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        return await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM bank_account_credential WHERE id=@id;",
            new { id }, cancellationToken: ct)).ConfigureAwait(false);
    }
}
