using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ToshanVault.Core.Security;

namespace ToshanVault.Tests.Security;

[TestClass]
public class VaultTests
{
    private const string GoodPassword = "!Arvind@Nivas83!";
    private const string BadPassword = "wrong-password";

    private static Vault NewVault() => new(new InMemoryMetaStore());

    [TestMethod]
    public async Task Initialise_LeavesVaultUnlocked()
    {
        var v = NewVault();
        v.IsUnlocked.Should().BeFalse();
        await v.InitialiseAsync(GoodPassword);
        v.IsUnlocked.Should().BeTrue();
    }

    [TestMethod]
    public async Task Initialise_Twice_Throws()
    {
        var store = new InMemoryMetaStore();
        var v1 = new Vault(store);
        await v1.InitialiseAsync(GoodPassword);

        var v2 = new Vault(store);
        var act = async () => await v2.InitialiseAsync(GoodPassword);
        await act.Should().ThrowAsync<VaultAlreadyInitialisedException>();
    }

    [TestMethod]
    public async Task Unlock_BeforeInitialise_Throws()
    {
        var v = NewVault();
        var act = async () => await v.UnlockAsync(GoodPassword);
        await act.Should().ThrowAsync<VaultNotInitialisedException>();
    }

    [TestMethod]
    public async Task Unlock_WithCorrectPassword_Succeeds()
    {
        var store = new InMemoryMetaStore();
        var v1 = new Vault(store);
        await v1.InitialiseAsync(GoodPassword);
        v1.Lock();

        var v2 = new Vault(store);
        await v2.UnlockAsync(GoodPassword);
        v2.IsUnlocked.Should().BeTrue();
    }

    [TestMethod]
    public async Task Unlock_WithWrongPassword_Throws()
    {
        var store = new InMemoryMetaStore();
        var v1 = new Vault(store);
        await v1.InitialiseAsync(GoodPassword);
        v1.Lock();

        var v2 = new Vault(store);
        var act = async () => await v2.UnlockAsync(BadPassword);
        await act.Should().ThrowAsync<WrongPasswordException>();
        v2.IsUnlocked.Should().BeFalse();
    }

    [TestMethod]
    public async Task EncryptField_WhenLocked_Throws()
    {
        var v = NewVault();
        await v.InitialiseAsync(GoodPassword);
        v.Lock();
        var act = () => v.EncryptField(new byte[] { 1 });
        act.Should().Throw<VaultLockedException>();
    }

    [TestMethod]
    public async Task EncryptField_RoundTrip_AcrossLockUnlock()
    {
        var store = new InMemoryMetaStore();
        var v1 = new Vault(store);
        await v1.InitialiseAsync(GoodPassword);

        var plaintext = System.Text.Encoding.UTF8.GetBytes("PAN: ABCDE1234F");
        var sealedBlob = v1.EncryptField(plaintext);
        v1.Lock();

        var v2 = new Vault(store);
        await v2.UnlockAsync(GoodPassword);
        var roundTripped = v2.DecryptField(sealedBlob.Iv, sealedBlob.Ciphertext, sealedBlob.Tag);
        roundTripped.Should().BeEquivalentTo(plaintext);
    }

    [TestMethod]
    public async Task Lock_ZeroisesInMemoryKey_BehaviourCheck()
    {
        var v = NewVault();
        await v.InitialiseAsync(GoodPassword);
        v.Lock();
        v.IsUnlocked.Should().BeFalse();

        // Subsequent crypto must fail without re-unlock.
        var act = () => v.EncryptField(new byte[] { 1 });
        act.Should().Throw<VaultLockedException>();
    }

    [TestMethod]
    public async Task Dispose_LocksVault()
    {
        var v = NewVault();
        await v.InitialiseAsync(GoodPassword);
        v.Dispose();
        v.IsUnlocked.Should().BeFalse();
    }

    [TestMethod]
    public async Task Unlock_WithTamperedIterationCount_Throws()
    {
        var store = new InMemoryMetaStore();
        var v1 = new Vault(store);
        await v1.InitialiseAsync(GoodPassword);
        v1.Lock();

        store.TamperIterations(kekIter: 1000, verifierIter: 1000);

        var v2 = new Vault(store);
        var act = async () => await v2.UnlockAsync(GoodPassword);
        await act.Should().ThrowAsync<TamperedDataException>();
    }
}

internal sealed class InMemoryMetaStore : IMetaStore
{
    private VaultMeta? _meta;

    public Task<bool> IsInitialisedAsync(CancellationToken ct = default)
        => Task.FromResult(_meta is not null);

    public Task WriteInitialAsync(VaultMeta meta, CancellationToken ct = default)
    {
        if (_meta is not null) throw new VaultAlreadyInitialisedException();
        _meta = meta;
        return Task.CompletedTask;
    }

    public Task<VaultMeta> ReadAsync(CancellationToken ct = default)
        => Task.FromResult(_meta ?? throw new InvalidOperationException("not initialised"));

    /// <summary>Test-only: forcibly mutate iteration counts to simulate DB tamper.</summary>
    public void TamperIterations(int kekIter, int verifierIter)
    {
        if (_meta is null) throw new InvalidOperationException();
        _meta = _meta with { KekIterations = kekIter, VerifierIterations = verifierIter };
    }
}
