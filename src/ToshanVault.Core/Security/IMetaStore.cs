namespace ToshanVault.Core.Security;

/// <summary>
/// Persistence boundary for the vault's master crypto material.
/// Implemented in ToshanVault.Data.MetaRepository.
/// </summary>
public interface IMetaStore
{
    Task<bool> IsInitialisedAsync(CancellationToken ct = default);

    Task WriteInitialAsync(VaultMeta meta, CancellationToken ct = default);

    /// <summary>
    /// Atomically replaces all crypto material (salt, verifier, KEK-wrapped DEK).
    /// Used exclusively by the change-password flow.
    /// </summary>
    Task UpdateMetaAsync(VaultMeta meta, CancellationToken ct = default);

    Task<VaultMeta> ReadAsync(CancellationToken ct = default);
}

public sealed record VaultMeta : IDisposable
{
    public byte[] Salt { get; init; } = Array.Empty<byte>();
    public int VerifierIterations { get; init; }
    public byte[] PwdVerifier { get; init; } = Array.Empty<byte>();
    public int KekIterations { get; init; }
    public byte[] DekIv { get; init; } = Array.Empty<byte>();
    public byte[] DekWrapped { get; init; } = Array.Empty<byte>();
    public byte[] DekTag { get; init; } = Array.Empty<byte>();

    /// <summary>
    /// Optional Windows Hello-wrapped KEK (filled in a later phase).
    /// Null until Hello enrolment has happened.
    /// </summary>
    public byte[]? HelloBlob { get; init; }

    public void Dispose()
    {
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(Salt);
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(PwdVerifier);
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(DekIv);
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(DekWrapped);
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(DekTag);
        if (HelloBlob is not null)
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(HelloBlob);
    }
}
