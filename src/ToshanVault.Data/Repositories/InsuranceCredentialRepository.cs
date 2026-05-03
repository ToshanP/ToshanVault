using Dapper;
using ToshanVault.Core.Models;
using ToshanVault.Data.Schema;

namespace ToshanVault.Data.Repositories;

/// <summary>
/// Read-side repository for <see cref="InsuranceCredential"/>. Writes are
/// owned by <see cref="InsuranceCredentialsService"/> because creating a
/// credential always implies creating/encrypting the linked vault_entry in
/// the same transaction.
/// </summary>
public sealed class InsuranceCredentialRepository
{
    private readonly IDbConnectionFactory _factory;

    public InsuranceCredentialRepository(IDbConnectionFactory factory)
    {
        DapperSetup.EnsureInitialised();
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public async Task<IReadOnlyList<InsuranceCredential>> GetByInsuranceAsync(long insuranceId, CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        var rows = await conn.QueryAsync<InsuranceCredential>(new CommandDefinition(
            @"SELECT id, insurance_id AS InsuranceId, owner,
                     vault_entry_id AS VaultEntryId, created_at AS CreatedAt, updated_at AS UpdatedAt
              FROM insurance_credential
              WHERE insurance_id=@id
              ORDER BY owner;",
            new { id = insuranceId }, cancellationToken: ct)).ConfigureAwait(false);
        return rows.AsList();
    }

    public async Task<InsuranceCredential?> GetAsync(long id, CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        return await conn.QuerySingleOrDefaultAsync<InsuranceCredential>(new CommandDefinition(
            @"SELECT id, insurance_id AS InsuranceId, owner,
                     vault_entry_id AS VaultEntryId, created_at AS CreatedAt, updated_at AS UpdatedAt
              FROM insurance_credential WHERE id=@id;",
            new { id }, cancellationToken: ct)).ConfigureAwait(false);
    }

    /// <summary>Deletes the credential row. The AFTER-DELETE trigger cascades
    /// through vault_entry → vault_field so all encrypted blobs are removed.</summary>
    public async Task<int> DeleteAsync(long id, CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        return await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM insurance_credential WHERE id=@id;",
            new { id }, cancellationToken: ct)).ConfigureAwait(false);
    }
}
