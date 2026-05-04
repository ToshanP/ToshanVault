using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ToshanVault.Core.Security;
using ToshanVault.Data.Repositories;
using ToshanVault.Data.Schema;

namespace ToshanVault.Tests.Data;

[TestClass]
public class MetaRepositoryAndMigrationTests
{
    /// <summary>
    /// In-memory SQLite needs a *shared* connection per test for multiple
    /// commands to see the same database. We use a unique cache name so tests
    /// run in parallel without colliding.
    /// </summary>
    private sealed class SharedMemoryFactory : IDbConnectionFactory, IDisposable
    {
        private readonly SqliteConnection _keepAlive;
        private readonly string _cs;

        public SharedMemoryFactory()
        {
            _cs = $"Data Source=file:tv-{Guid.NewGuid():N}?mode=memory&cache=shared";
            _keepAlive = new SqliteConnection(_cs);
            _keepAlive.Open();
        }

        public SqliteConnection Open()
        {
            var c = new SqliteConnection(_cs);
            c.Open();
            return c;
        }

        public void Dispose() => _keepAlive.Dispose();
    }

    [TestMethod]
    public async Task MigrationRunner_FreshDb_AppliesAllMigrations()
    {
        using var f = new SharedMemoryFactory();
        var runner = new MigrationRunner(f);
        var applied = await runner.RunAsync();
        applied.Should().BeGreaterThan(0);

        // Re-running is a no-op.
        var second = await runner.RunAsync();
        second.Should().Be(0);

        await using var conn = f.Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('recipe', 'recipe_tag');";
        var legacyRecipeTables = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        legacyRecipeTables.Should().Be(0);
    }

    [TestMethod]
    public async Task Vault_FullPath_PersistsThroughRepository()
    {
        using var f = new SharedMemoryFactory();
        await new MigrationRunner(f).RunAsync();
        var repo = new MetaRepository(f);

        const string pwd = "!Arvind@Nivas83!";
        await using (var v = new Vault(repo))
        {
            (await v.IsInitialisedAsync()).Should().BeFalse();
            await v.InitialiseAsync(pwd);
            (await v.IsInitialisedAsync()).Should().BeTrue();
        }

        var v2 = new Vault(repo);
        await v2.UnlockAsync(pwd);
        v2.IsUnlocked.Should().BeTrue();

        var s = v2.EncryptField(System.Text.Encoding.UTF8.GetBytes("hello"));
        var pt = v2.DecryptField(s.Iv, s.Ciphertext, s.Tag);
        System.Text.Encoding.UTF8.GetString(pt).Should().Be("hello");
    }

    [TestMethod]
    public async Task Initialise_Twice_ThroughRepository_RejectedAtDbLayer()
    {
        using var f = new SharedMemoryFactory();
        await new MigrationRunner(f).RunAsync();
        var repo = new MetaRepository(f);

        var v1 = new Vault(repo);
        await v1.InitialiseAsync("first-password-1234");

        // Direct WriteInitialAsync call bypasses Vault's pre-check — exercises
        // the DB-level UNIQUE-constraint guarantee.
        var act = async () => await repo.WriteInitialAsync(new VaultMeta
        {
            Salt = new byte[16],
            VerifierIterations = 100_000,
            PwdVerifier = new byte[32],
            KekIterations = 200_000,
            DekIv = new byte[12],
            DekWrapped = new byte[32],
            DekTag = new byte[16],
        });
        await act.Should().ThrowAsync<VaultAlreadyInitialisedException>();
    }
}
