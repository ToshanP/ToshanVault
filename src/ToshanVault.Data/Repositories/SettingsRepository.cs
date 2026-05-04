using Dapper;
using ToshanVault.Data.Schema;

namespace ToshanVault.Data.Repositories;

/// <summary>
/// Plain key-value settings stored outside the encrypted vault.
/// </summary>
public sealed class SettingsRepository
{
    private readonly IDbConnectionFactory _factory;

    public SettingsRepository(IDbConnectionFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        return await conn.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT value FROM settings WHERE key = @k;",
            new { k = key },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        await conn.ExecuteAsync(new CommandDefinition(
            "INSERT INTO settings(key, value) VALUES (@k, @v) ON CONFLICT(key) DO UPDATE SET value = excluded.value;",
            new { k = key, v = value },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<bool> GetBoolAsync(string key, CancellationToken ct = default)
    {
        var val = await GetAsync(key, ct).ConfigureAwait(false);
        return val == "1";
    }

    public Task SetBoolAsync(string key, bool value, CancellationToken ct = default)
        => SetAsync(key, value ? "1" : "0", ct);
}
