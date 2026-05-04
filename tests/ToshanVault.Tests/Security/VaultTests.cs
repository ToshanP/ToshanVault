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

    [TestMethod]
    public async Task ChangePassword_WithCorrectCurrent_SucceedsAndNewPasswordWorks()
    {
        const string newPassword = "NewSecure#2026";
        var store = new InMemoryMetaStore();
        var v = new Vault(store);
        await v.InitialiseAsync(GoodPassword);

        await v.ChangePasswordAsync(GoodPassword, newPassword);
        v.IsUnlocked.Should().BeTrue();
        v.Lock();

        // Old password should fail
        var v2 = new Vault(store);
        var act = async () => await v2.UnlockAsync(GoodPassword);
        await act.Should().ThrowAsync<WrongPasswordException>();

        // New password should work
        await v2.UnlockAsync(newPassword);
        v2.IsUnlocked.Should().BeTrue();
    }

    [TestMethod]
    public async Task ChangePassword_WithWrongCurrent_Throws()
    {
        var store = new InMemoryMetaStore();
        var v = new Vault(store);
        await v.InitialiseAsync(GoodPassword);

        var act = async () => await v.ChangePasswordAsync(BadPassword, "anything");
        await act.Should().ThrowAsync<WrongPasswordException>();
    }

    [TestMethod]
    public async Task ChangePassword_WhenLocked_Throws()
    {
        var store = new InMemoryMetaStore();
        var v = new Vault(store);
        await v.InitialiseAsync(GoodPassword);
        v.Lock();

        var act = async () => await v.ChangePasswordAsync(GoodPassword, "new");
        await act.Should().ThrowAsync<VaultLockedException>();
    }

    [TestMethod]
    public async Task ChangePassword_EncryptedDataRemainsAccessible()
    {
        const string newPassword = "Changed!Pass1";
        var store = new InMemoryMetaStore();
        var v = new Vault(store);
        await v.InitialiseAsync(GoodPassword);

        // Encrypt something before changing password
        var plaintext = System.Text.Encoding.UTF8.GetBytes("secret data");
        var sealed1 = v.EncryptField(plaintext);

        await v.ChangePasswordAsync(GoodPassword, newPassword);

        // Data encrypted before password change should still decrypt
        var decrypted = v.DecryptField(sealed1.Iv, sealed1.Ciphertext, sealed1.Tag);
        decrypted.Should().BeEquivalentTo(plaintext);

        // Also works after lock/unlock with new password
        v.Lock();
        var v2 = new Vault(store);
        await v2.UnlockAsync(newPassword);
        var decrypted2 = v2.DecryptField(sealed1.Iv, sealed1.Ciphertext, sealed1.Tag);
        decrypted2.Should().BeEquivalentTo(plaintext);
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
    {
        if (_meta is null) throw new InvalidOperationException("not initialised");
        // Return a copy — just like MetaRepository which reads fresh bytes from DB.
        // Prevents UnlockAsync's `using var meta` from zeroing the stored instance.
        return Task.FromResult(new VaultMeta
        {
            Salt = (byte[])_meta.Salt.Clone(),
            VerifierIterations = _meta.VerifierIterations,
            PwdVerifier = (byte[])_meta.PwdVerifier.Clone(),
            KekIterations = _meta.KekIterations,
            DekIv = (byte[])_meta.DekIv.Clone(),
            DekWrapped = (byte[])_meta.DekWrapped.Clone(),
            DekTag = (byte[])_meta.DekTag.Clone(),
            HelloBlob = _meta.HelloBlob is not null ? (byte[])_meta.HelloBlob.Clone() : null,
        });
    }

    public Task UpdateMetaAsync(VaultMeta meta, CancellationToken ct = default)
    {
        if (_meta is null) throw new InvalidOperationException("not initialised");
        _meta = meta;
        return Task.CompletedTask;
    }

    /// <summary>Test-only: forcibly mutate iteration counts to simulate DB tamper.</summary>
    public void TamperIterations(int kekIter, int verifierIter)
    {
        if (_meta is null) throw new InvalidOperationException();
        _meta = _meta with { KekIterations = kekIter, VerifierIterations = verifierIter };
    }
}
