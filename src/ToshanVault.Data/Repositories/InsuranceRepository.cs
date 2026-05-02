using Dapper;
using ToshanVault.Core.Models;
using ToshanVault.Data.Schema;

namespace ToshanVault.Data.Repositories;

/// <summary>
/// Dapper CRUD for the insurance entity. Encrypted credentials/notes live in
/// the linked vault_entry (see <see cref="InsuranceCredentialsService"/>) so
/// this repository deals only with non-secret display fields.
/// </summary>
public sealed class InsuranceRepository
{
    private readonly IDbConnectionFactory _factory;

    public InsuranceRepository(IDbConnectionFactory factory)
    {
        DapperSetup.EnsureInitialised();
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    private const string SelectColumns =
        @"id            AS Id,
          insurer_company AS InsurerCompany,
          policy_number   AS PolicyNumber,
          insurance_type  AS InsuranceType,
          website         AS Website,
          owner           AS Owner,
          renewal_date    AS RenewalDate,
          vault_entry_id  AS VaultEntryId,
          created_at      AS CreatedAt,
          updated_at      AS UpdatedAt";

    public async Task<long> InsertAsync(Insurance e, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (string.IsNullOrWhiteSpace(e.InsurerCompany))
            throw new ArgumentException("InsurerCompany is required.", nameof(e));
        if (e.CreatedAt == default) e.CreatedAt = DateTimeOffset.UtcNow;
        e.UpdatedAt = DateTimeOffset.UtcNow;
        await using var conn = _factory.Open();
        var id = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            @"INSERT INTO insurance(insurer_company, policy_number, insurance_type, website, owner,
                                    renewal_date, vault_entry_id, created_at, updated_at)
              VALUES (@InsurerCompany, @PolicyNumber, @InsuranceType, @Website, @Owner,
                      @RenewalDate, @VaultEntryId, @CreatedAt, @UpdatedAt);
              SELECT last_insert_rowid();",
            new
            {
                e.InsurerCompany, e.PolicyNumber, e.InsuranceType, e.Website, e.Owner,
                RenewalDate = e.RenewalDate?.ToString("yyyy-MM-dd"),
                e.VaultEntryId, e.CreatedAt, e.UpdatedAt,
            },
            cancellationToken: ct)).ConfigureAwait(false);
        e.Id = id;
        return id;
    }

    public async Task UpdateAsync(Insurance e, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (string.IsNullOrWhiteSpace(e.InsurerCompany))
            throw new ArgumentException("InsurerCompany is required.", nameof(e));
        e.UpdatedAt = DateTimeOffset.UtcNow;
        await using var conn = _factory.Open();
        var rows = await conn.ExecuteAsync(new CommandDefinition(
            @"UPDATE insurance
                SET insurer_company=@InsurerCompany, policy_number=@PolicyNumber,
                    insurance_type=@InsuranceType,   website=@Website, owner=@Owner,
                    renewal_date=@RenewalDate,       vault_entry_id=@VaultEntryId,
                    updated_at=@UpdatedAt
              WHERE id=@Id;",
            new
            {
                e.Id, e.InsurerCompany, e.PolicyNumber, e.InsuranceType, e.Website, e.Owner,
                RenewalDate = e.RenewalDate?.ToString("yyyy-MM-dd"),
                e.VaultEntryId, e.UpdatedAt,
            },
            cancellationToken: ct)).ConfigureAwait(false);
        if (rows == 0) throw new InvalidOperationException($"Insurance {e.Id} not found.");
    }

    /// <summary>Cascades to the linked vault_entry (and its vault_field rows)
    /// via the trg_insurance_after_delete trigger from migration 010.</summary>
    public async Task<int> DeleteAsync(long id, CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        return await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM insurance WHERE id=@id;", new { id }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<Insurance?> GetAsync(long id, CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        // RenewalDate is text; Dapper's DateOnly mapper handles "yyyy-MM-dd".
        return await conn.QuerySingleOrDefaultAsync<Insurance>(new CommandDefinition(
            $"SELECT {SelectColumns} FROM insurance WHERE id=@id;",
            new { id }, cancellationToken: ct)).ConfigureAwait(false);
    }

    /// <summary>Sorted by renewal date (nulls last) then insurer name so the
    /// list naturally surfaces what's expiring soonest.</summary>
    public async Task<IReadOnlyList<Insurance>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        var rows = await conn.QueryAsync<Insurance>(new CommandDefinition(
            $@"SELECT {SelectColumns} FROM insurance
               ORDER BY (renewal_date IS NULL), renewal_date, insurer_company;",
            cancellationToken: ct)).ConfigureAwait(false);
        return rows.AsList();
    }
}
