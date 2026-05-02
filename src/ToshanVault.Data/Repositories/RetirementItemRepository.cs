using Dapper;
using ToshanVault.Core.Models;
using ToshanVault.Data.Schema;

namespace ToshanVault.Data.Repositories;

public sealed class RetirementItemRepository
{
    private readonly IDbConnectionFactory _factory;

    public RetirementItemRepository(IDbConnectionFactory factory)
    {
        DapperSetup.EnsureInitialised();
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public async Task<long> InsertAsync(RetirementItem r, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(r);
        await using var conn = _factory.Open();
        var id = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            @"INSERT INTO retirement_item(label, kind, monthly_amount_jan2025, inflation_pct,
                                          indexed, start_age, end_age, notes)
              VALUES (@Label, @Kind, @MonthlyAmountJan2025, @InflationPct,
                      @Indexed, @StartAge, @EndAge, @Notes);
              SELECT last_insert_rowid();",
            new
            {
                r.Label, Kind = r.Kind.ToString(), r.MonthlyAmountJan2025, r.InflationPct,
                r.Indexed, r.StartAge, r.EndAge, r.Notes,
            },
            cancellationToken: ct)).ConfigureAwait(false);
        r.Id = id;
        return id;
    }

    public async Task UpdateAsync(RetirementItem r, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(r);
        await using var conn = _factory.Open();
        var rows = await conn.ExecuteAsync(new CommandDefinition(
            @"UPDATE retirement_item
              SET label=@Label, kind=@Kind, monthly_amount_jan2025=@MonthlyAmountJan2025,
                  inflation_pct=@InflationPct, indexed=@Indexed,
                  start_age=@StartAge, end_age=@EndAge, notes=@Notes
              WHERE id=@Id;",
            new
            {
                r.Id, r.Label, Kind = r.Kind.ToString(), r.MonthlyAmountJan2025, r.InflationPct,
                r.Indexed, r.StartAge, r.EndAge, r.Notes,
            },
            cancellationToken: ct)).ConfigureAwait(false);
        if (rows == 0) throw new InvalidOperationException($"RetirementItem {r.Id} not found.");
    }

    public async Task<int> DeleteAsync(long id, CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        return await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM retirement_item WHERE id=@id;", new { id }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<RetirementItem?> GetAsync(long id, CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        return await conn.QuerySingleOrDefaultAsync<RetirementItem>(new CommandDefinition(
            @"SELECT id, label, kind, monthly_amount_jan2025, inflation_pct,
                     indexed, start_age, end_age, notes
              FROM retirement_item WHERE id=@id;",
            new { id }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RetirementItem>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        var rows = await conn.QueryAsync<RetirementItem>(new CommandDefinition(
            @"SELECT id, label, kind, monthly_amount_jan2025, inflation_pct,
                     indexed, start_age, end_age, notes
              FROM retirement_item ORDER BY kind, label;",
            cancellationToken: ct)).ConfigureAwait(false);
        return rows.AsList();
    }
}
