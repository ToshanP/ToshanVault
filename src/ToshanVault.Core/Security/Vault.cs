using System.Security.Cryptography;

namespace ToshanVault.Core.Security;

/// <summary>
/// Master vault. Holds in-memory KEK/DEK while unlocked.
/// Not thread-safe — UI layer is expected to serialise calls.
/// </summary>
public sealed class Vault : IAsyncDisposable, IDisposable
{
    private readonly IMetaStore _meta;

    private byte[]? _kek;
    private byte[]? _dek;

    public Vault(IMetaStore meta)
    {
        _meta = meta ?? throw new ArgumentNullException(nameof(meta));
    }

    public bool IsUnlocked => _dek is not null;

    public Task<bool> IsInitialisedAsync(CancellationToken ct = default)
        => _meta.IsInitialisedAsync(ct);

    /// <summary>
    /// Creates the vault for the first time: generates salt + DEK,
    /// derives KEK + verifier, wraps the DEK and persists everything.
    /// Leaves the vault in an Unlocked state on success.
    /// </summary>
    public async Task InitialiseAsync(string password, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        if (await _meta.IsInitialisedAsync(ct).ConfigureAwait(false))
            throw new VaultAlreadyInitialisedException();

        var salt = RandomNumberGenerator.GetBytes(CryptoConstants.SaltBytes);
        byte[]? verifier = null;
        byte[]? kek = null;
        byte[]? dek = null;
        var success = false;

        try
        {
            verifier = KeyDerivation.DeriveVerifier(password, salt);
            kek = KeyDerivation.DeriveKek(password, salt);
            dek = RandomNumberGenerator.GetBytes(CryptoConstants.DekBytes);

            var sealedDek = AesGcmCrypto.Encrypt(kek, dek);

            var meta = new VaultMeta
            {
                Salt = salt,
                VerifierIterations = CryptoConstants.VerifierIterations,
                PwdVerifier = verifier,
                KekIterations = CryptoConstants.KekIterations,
                DekIv = sealedDek.Iv,
                DekWrapped = sealedDek.Ciphertext,
                DekTag = sealedDek.Tag,
            };

            await _meta.WriteInitialAsync(meta, ct).ConfigureAwait(false);

            _kek = kek;
            _dek = dek;
            success = true;
        }
        finally
        {
            if (!success)
            {
                if (verifier is not null) CryptographicOperations.ZeroMemory(verifier);
                if (kek is not null) CryptographicOperations.ZeroMemory(kek);
                if (dek is not null) CryptographicOperations.ZeroMemory(dek);
            }
        }
    }

    /// <summary>
    /// Verifies the supplied password and unwraps the DEK. Throws
    /// <see cref="WrongPasswordException"/> on mismatch (constant-time compare).
    /// </summary>
    public async Task UnlockAsync(string password, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        if (!await _meta.IsInitialisedAsync(ct).ConfigureAwait(false))
            throw new VaultNotInitialisedException();

        using var meta = await _meta.ReadAsync(ct).ConfigureAwait(false);

        // Pin iteration counts to the build-time constants — an attacker with DB
        // write access could otherwise downgrade KDF cost or trigger CPU-DoS.
        if (meta.KekIterations != CryptoConstants.KekIterations ||
            meta.VerifierIterations != CryptoConstants.VerifierIterations)
        {
            throw new TamperedDataException(
                "Stored KDF iteration counts do not match the expected values; database may have been tampered with.");
        }

        var candidateVerifier = KeyDerivation.DeriveVerifier(password, meta.Salt, meta.VerifierIterations);
        var match = CryptographicOperations.FixedTimeEquals(candidateVerifier, meta.PwdVerifier);
        CryptographicOperations.ZeroMemory(candidateVerifier);
        if (!match) throw new WrongPasswordException();

        var kek = KeyDerivation.DeriveKek(password, meta.Salt, meta.KekIterations);
        byte[] dek;
        try
        {
            dek = AesGcmCrypto.Decrypt(kek, meta.DekIv, meta.DekWrapped, meta.DekTag);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(kek);
            throw;
        }

        Lock();
        _kek = kek;
        _dek = dek;
    }

    public void Lock()
    {
        if (_kek is not null) { CryptographicOperations.ZeroMemory(_kek); _kek = null; }
        if (_dek is not null) { CryptographicOperations.ZeroMemory(_dek); _dek = null; }
    }

    public AesGcmCrypto.Sealed EncryptField(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData = default)
    {
        if (_dek is null) throw new VaultLockedException();
        return AesGcmCrypto.Encrypt(_dek, plaintext, associatedData);
    }

    public byte[] DecryptField(ReadOnlySpan<byte> iv, ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> tag, ReadOnlySpan<byte> associatedData = default)
    {
        if (_dek is null) throw new VaultLockedException();
        return AesGcmCrypto.Decrypt(_dek, iv, ciphertext, tag, associatedData);
    }

    public void Dispose() => Lock();

    public ValueTask DisposeAsync()
    {
        Lock();
        return ValueTask.CompletedTask;
    }
}
