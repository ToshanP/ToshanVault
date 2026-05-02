using Dapper;
using ToshanVault.Core.Models;
using ToshanVault.Data.Schema;

namespace ToshanVault.Data.Repositories;

public sealed class BudgetItemRepository
{
    private readonly IDbConnectionFactory _factory;

    public BudgetItemRepository(IDbConnectionFactory factory)
    {
        DapperSetup.EnsureInitialised();
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public async Task<long> InsertAsync(BudgetItem i, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(i);
        await using var conn = _factory.Open();
        var id = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            @"INSERT INTO budget_item(category_id, label, amount, frequency, notes, sort_order)
              VALUES (@CategoryId, @Label, @Amount, @Frequency, @Notes, @SortOrder);
              SELECT last_insert_rowid();",
            new { i.CategoryId, i.Label, i.Amount, Frequency = i.Frequency.ToString(), i.Notes, i.SortOrder },
            cancellationToken: ct)).ConfigureAwait(false);
        i.Id = id;
        return id;
    }

    public async Task UpdateAsync(BudgetItem i, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(i);
        await using var conn = _factory.Open();
        var rows = await conn.ExecuteAsync(new CommandDefinition(
            @"UPDATE budget_item
              SET category_id=@CategoryId, label=@Label, amount=@Amount,
                  frequency=@Frequency, notes=@Notes, sort_order=@SortOrder
              WHERE id=@Id;",
            new { i.Id, i.CategoryId, i.Label, i.Amount, Frequency = i.Frequency.ToString(), i.Notes, i.SortOrder },
            cancellationToken: ct)).ConfigureAwait(false);
        if (rows == 0) throw new InvalidOperationException($"BudgetItem {i.Id} not found.");
    }

    public async Task<int> DeleteAsync(long id, CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        return await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM budget_item WHERE id=@id;", new { id }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<BudgetItem?> GetAsync(long id, CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        return await conn.QuerySingleOrDefaultAsync<BudgetItem>(new CommandDefinition(
            @"SELECT id, category_id, label, amount, frequency, notes, sort_order
              FROM budget_item WHERE id=@id;",
            new { id }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<BudgetItem>> GetByCategoryAsync(long categoryId, CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        var rows = await conn.QueryAsync<BudgetItem>(new CommandDefinition(
            @"SELECT id, category_id, label, amount, frequency, notes, sort_order
              FROM budget_item WHERE category_id=@categoryId ORDER BY sort_order, label;",
            new { categoryId }, cancellationToken: ct)).ConfigureAwait(false);
        return rows.AsList();
    }

    public async Task<IReadOnlyList<BudgetItem>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        var rows = await conn.QueryAsync<BudgetItem>(new CommandDefinition(
            @"SELECT id, category_id, label, amount, frequency, notes, sort_order
              FROM budget_item ORDER BY category_id, sort_order, label;",
            cancellationToken: ct)).ConfigureAwait(false);
        return rows.AsList();
    }
}
