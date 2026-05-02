using Dapper;
using ToshanVault.Core.Models;
using ToshanVault.Data.Schema;

namespace ToshanVault.Data.Repositories;

public sealed class BudgetCategoryRepository
{
    private readonly IDbConnectionFactory _factory;

    public BudgetCategoryRepository(IDbConnectionFactory factory)
    {
        DapperSetup.EnsureInitialised();
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public async Task<long> InsertAsync(BudgetCategory c, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(c);
        await using var conn = _factory.Open();
        var id = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            @"INSERT INTO budget_category(name, type) VALUES (@Name, @Type);
              SELECT last_insert_rowid();",
            new { c.Name, Type = c.Type.ToString() }, cancellationToken: ct)).ConfigureAwait(false);
        c.Id = id;
        return id;
    }

    public async Task UpdateAsync(BudgetCategory c, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(c);
        await using var conn = _factory.Open();
        var rows = await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE budget_category SET name=@Name, type=@Type WHERE id=@Id;",
            new { c.Id, c.Name, Type = c.Type.ToString() }, cancellationToken: ct)).ConfigureAwait(false);
        if (rows == 0) throw new InvalidOperationException($"BudgetCategory {c.Id} not found.");
    }

    public async Task<int> DeleteAsync(long id, CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        return await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM budget_category WHERE id=@id;", new { id }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<BudgetCategory?> GetAsync(long id, CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        return await conn.QuerySingleOrDefaultAsync<BudgetCategory>(new CommandDefinition(
            "SELECT id, name, type FROM budget_category WHERE id=@id;",
            new { id }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<BudgetCategory>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        var rows = await conn.QueryAsync<BudgetCategory>(new CommandDefinition(
            "SELECT id, name, type FROM budget_category ORDER BY type, name;",
            cancellationToken: ct)).ConfigureAwait(false);
        return rows.AsList();
    }
}
