namespace ToshanVault.Core.Security;

/// <summary>
/// Hard-coded crypto parameters for the vault. Values are spec'd in
/// project-instructions.md §6.
/// </summary>
public static class CryptoConstants
{
    public const int SaltBytes = 16;
    public const int KekBytes = 32;
    public const int DekBytes = 32;
    public const int GcmIvBytes = 12;
    public const int GcmTagBytes = 16;
    public const int VerifierBytes = 32;

    public const int KekIterations = 200_000;
    public const int VerifierIterations = 100_000;
}
