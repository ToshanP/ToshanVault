using Dapper;
using ToshanVault.Core.Models;
using ToshanVault.Core.Security;
using ToshanVault.Data.Schema;

namespace ToshanVault.Data.Repositories;

public sealed class VaultEntryRepository
{
    private readonly IDbConnectionFactory _factory;

    public VaultEntryRepository(IDbConnectionFactory factory)
    {
        DapperSetup.EnsureInitialised();
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public async Task<long> InsertAsync(VaultEntry e, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.CreatedAt == default) e.CreatedAt = DateTimeOffset.UtcNow;
        e.UpdatedAt = DateTimeOffset.UtcNow;
        await using var conn = _factory.Open();
        if (e.SortOrder == 0)
        {
            // Append at the end — within-kind ordering is what the UI cares
            // about (each Vault page is filtered by kind), but we keep a
            // single global sequence to keep the column simple.
            var maxOrder = await conn.ExecuteScalarAsync<long?>(new CommandDefinition(
                "SELECT MAX(sort_order) FROM vault_entry;",
                cancellationToken: ct)).ConfigureAwait(false) ?? 0;
            e.SortOrder = (int)Math.Min(int.MaxValue, maxOrder + 1);
        }
        var id = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            @"INSERT INTO vault_entry(kind, name, category, tags, owner, sort_order, created_at, updated_at)
              VALUES (@Kind, @Name, @Category, @Tags, @Owner, @SortOrder, @CreatedAt, @UpdatedAt);
              SELECT last_insert_rowid();",
            e, cancellationToken: ct)).ConfigureAwait(false);
        e.Id = id;
        return id;
    }

    public async Task UpdateAsync(VaultEntry e, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(e);
        e.UpdatedAt = DateTimeOffset.UtcNow;
        await using var conn = _factory.Open();
        var rows = await conn.ExecuteAsync(new CommandDefinition(
            @"UPDATE vault_entry SET kind=@Kind, name=@Name, category=@Category,
                                     tags=@Tags, owner=@Owner, updated_at=@UpdatedAt
              WHERE id=@Id;",
            e, cancellationToken: ct)).ConfigureAwait(false);
        if (rows == 0) throw new InvalidOperationException($"VaultEntry {e.Id} not found.");
    }

    /// <summary>Cascade-deletes child vault_field rows via the FK ON DELETE CASCADE.</summary>
    public async Task<int> DeleteAsync(long id, CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        return await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM vault_entry WHERE id=@id;", new { id }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<VaultEntry?> GetAsync(long id, CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        return await conn.QuerySingleOrDefaultAsync<VaultEntry>(new CommandDefinition(
            @"SELECT id, kind, name, category, tags, owner, sort_order, created_at, updated_at
              FROM vault_entry WHERE id=@id;",
            new { id }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<VaultEntry>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        var rows = await conn.QueryAsync<VaultEntry>(new CommandDefinition(
            @"SELECT id, kind, name, category, tags, owner, sort_order, created_at, updated_at
              FROM vault_entry ORDER BY sort_order, id;",
            cancellationToken: ct)).ConfigureAwait(false);
        return rows.AsList();
    }

    public async Task<IReadOnlyList<VaultEntry>> GetByKindAsync(string kind, CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        var rows = await conn.QueryAsync<VaultEntry>(new CommandDefinition(
            @"SELECT id, kind, name, category, tags, owner, sort_order, created_at, updated_at
              FROM vault_entry WHERE kind=@kind ORDER BY sort_order, id;",
            new { kind }, cancellationToken: ct)).ConfigureAwait(false);
        return rows.AsList();
    }

    /// <summary>
    /// Persists the user-controlled drag-and-drop order. Caller supplies the
    /// IDs in their desired display order; this method writes sort_order =
    /// index (1-based) in a single transaction. IDs not in the list are
    /// untouched so other Vault kinds keep their ordering.
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
                "UPDATE vault_entry SET sort_order=@order, updated_at=@stamp WHERE id=@id;",
                new { order = i + 1, stamp, id = orderedIds[i] },
                tx, cancellationToken: ct)).ConfigureAwait(false);
        }
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }
}
