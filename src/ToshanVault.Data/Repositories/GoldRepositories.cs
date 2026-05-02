using Dapper;
using ToshanVault.Core.Models;
using ToshanVault.Data.Schema;

namespace ToshanVault.Data.Repositories;

public sealed class GoldItemRepository
{
    private readonly IDbConnectionFactory _factory;

    public GoldItemRepository(IDbConnectionFactory factory)
    {
        DapperSetup.EnsureInitialised();
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public async Task<long> InsertAsync(GoldItem g, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(g);
        await using var conn = _factory.Open();
        var id = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            @"INSERT INTO gold_item(item_name, purity, qty, tola, grams_calc, notes)
              VALUES (@ItemName, @Purity, @Qty, @Tola, @GramsCalc, @Notes);
              SELECT last_insert_rowid();",
            g, cancellationToken: ct)).ConfigureAwait(false);
        g.Id = id;
        return id;
    }

    public async Task UpdateAsync(GoldItem g, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(g);
        await using var conn = _factory.Open();
        var rows = await conn.ExecuteAsync(new CommandDefinition(
            @"UPDATE gold_item
              SET item_name=@ItemName, purity=@Purity, qty=@Qty,
                  tola=@Tola, grams_calc=@GramsCalc, notes=@Notes
              WHERE id=@Id;",
            g, cancellationToken: ct)).ConfigureAwait(false);
        if (rows == 0) throw new InvalidOperationException($"GoldItem {g.Id} not found.");
    }

    public async Task<int> DeleteAsync(long id, CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        return await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM gold_item WHERE id=@id;", new { id }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<GoldItem?> GetAsync(long id, CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        return await conn.QuerySingleOrDefaultAsync<GoldItem>(new CommandDefinition(
            @"SELECT id, item_name, purity, qty, tola, grams_calc, notes
              FROM gold_item WHERE id=@id;",
            new { id }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<GoldItem>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        var rows = await conn.QueryAsync<GoldItem>(new CommandDefinition(
            @"SELECT id, item_name, purity, qty, tola, grams_calc, notes
              FROM gold_item ORDER BY item_name;",
            cancellationToken: ct)).ConfigureAwait(false);
        return rows.AsList();
    }
}

public sealed class GoldPriceCacheRepository
{
    private readonly IDbConnectionFactory _factory;

    public GoldPriceCacheRepository(IDbConnectionFactory factory)
    {
        DapperSetup.EnsureInitialised();
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public async Task UpsertAsync(GoldPriceCache p, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(p);
        await using var conn = _factory.Open();
        await conn.ExecuteAsync(new CommandDefinition(
            @"INSERT INTO gold_price_cache(currency, price_per_gram_24k, fetched_at)
              VALUES (@Currency, @PricePerGram24k, @FetchedAt)
              ON CONFLICT(currency) DO UPDATE SET
                price_per_gram_24k=excluded.price_per_gram_24k,
                fetched_at=excluded.fetched_at
              WHERE excluded.fetched_at > gold_price_cache.fetched_at;",
            p, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<GoldPriceCache?> GetAsync(string currency, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(currency);
        await using var conn = _factory.Open();
        return await conn.QuerySingleOrDefaultAsync<GoldPriceCache>(new CommandDefinition(
            @"SELECT currency, price_per_gram_24k, fetched_at FROM gold_price_cache WHERE currency=@currency;",
            new { currency }, cancellationToken: ct)).ConfigureAwait(false);
    }
}
