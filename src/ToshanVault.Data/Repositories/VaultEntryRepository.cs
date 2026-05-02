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
        var id = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            @"INSERT INTO vault_entry(kind, name, category, tags, owner, created_at, updated_at)
              VALUES (@Kind, @Name, @Category, @Tags, @Owner, @CreatedAt, @UpdatedAt);
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
            @"SELECT id, kind, name, category, tags, owner, created_at, updated_at
              FROM vault_entry WHERE id=@id;",
            new { id }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<VaultEntry>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        var rows = await conn.QueryAsync<VaultEntry>(new CommandDefinition(
            @"SELECT id, kind, name, category, tags, owner, created_at, updated_at
              FROM vault_entry ORDER BY kind, name;",
            cancellationToken: ct)).ConfigureAwait(false);
        return rows.AsList();
    }

    public async Task<IReadOnlyList<VaultEntry>> GetByKindAsync(string kind, CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        var rows = await conn.QueryAsync<VaultEntry>(new CommandDefinition(
            @"SELECT id, kind, name, category, tags, owner, created_at, updated_at
              FROM vault_entry WHERE kind=@kind ORDER BY name;",
            new { kind }, cancellationToken: ct)).ConfigureAwait(false);
        return rows.AsList();
    }
}
