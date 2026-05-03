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
          notes           AS Notes,
          vault_entry_id  AS VaultEntryId,
          sort_order      AS SortOrder,
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
        if (e.SortOrder == 0)
        {
            // Append at the end of the user-defined order. Falls back to 1 on
            // an empty table so the column never holds the seed default 0.
            var maxOrder = await conn.ExecuteScalarAsync<long?>(new CommandDefinition(
                "SELECT MAX(sort_order) FROM insurance;",
                cancellationToken: ct)).ConfigureAwait(false) ?? 0;
            e.SortOrder = (int)Math.Min(int.MaxValue, maxOrder + 1);
        }
        var id = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            @"INSERT INTO insurance(insurer_company, policy_number, insurance_type, website, owner,
                                    renewal_date, notes, vault_entry_id, sort_order, created_at, updated_at)
              VALUES (@InsurerCompany, @PolicyNumber, @InsuranceType, @Website, @Owner,
                      @RenewalDate, @Notes, @VaultEntryId, @SortOrder, @CreatedAt, @UpdatedAt);
              SELECT last_insert_rowid();",
            new
            {
                e.InsurerCompany, e.PolicyNumber, e.InsuranceType, e.Website, e.Owner,
                RenewalDate = e.RenewalDate?.ToString("yyyy-MM-dd"),
                e.Notes, e.VaultEntryId, e.SortOrder, e.CreatedAt, e.UpdatedAt,
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
                    renewal_date=@RenewalDate,       notes=@Notes,
                    vault_entry_id=@VaultEntryId,    updated_at=@UpdatedAt
              WHERE id=@Id;",
            new
            {
                e.Id, e.InsurerCompany, e.PolicyNumber, e.InsuranceType, e.Website, e.Owner,
                RenewalDate = e.RenewalDate?.ToString("yyyy-MM-dd"),
                e.Notes, e.VaultEntryId, e.UpdatedAt,
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

    /// <summary>Sorted by the user-controlled drag-and-drop order. Ties
    /// (legacy rows backfilled to sort_order = id) fall back to renewal date
    /// then insurer name so newly migrated installs still surface what's
    /// expiring soonest until the user reorders.</summary>
    public async Task<IReadOnlyList<Insurance>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        var rows = await conn.QueryAsync<Insurance>(new CommandDefinition(
            $@"SELECT {SelectColumns} FROM insurance
               ORDER BY sort_order, (renewal_date IS NULL), renewal_date, insurer_company;",
            cancellationToken: ct)).ConfigureAwait(false);
        return rows.AsList();
    }

    /// <summary>
    /// Persists drag-and-drop order. Caller supplies IDs in their desired
    /// display order; this method writes sort_order = (index + 1) inside a
    /// single transaction. IDs not in the list are untouched.
    /// </summary>
    public async Task UpdateSortOrderAsync(IReadOnlyList<long> orderedIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(orderedIds);
        if (orderedIds.Count == 0) return;
        await using var conn = _factory.Open();
        await using var tx = (Microsoft.Data.Sqlite.SqliteTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        var stamp = DateTimeOffset.UtcNow;
        for (var i = 0; i < orderedIds.Count; i++)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "UPDATE insurance SET sort_order=@order, updated_at=@stamp WHERE id=@id;",
                new { order = i + 1, stamp, id = orderedIds[i] },
                tx, cancellationToken: ct)).ConfigureAwait(false);
        }
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }
}
