using System.Security.Cryptography;
using System.Text;

namespace ToshanVault.Core.Security;

/// <summary>
/// PBKDF2-SHA256 key/verifier derivation. Pure function. Caller owns inputs.
/// </summary>
public static class KeyDerivation
{
    public static byte[] DeriveKek(string password, ReadOnlySpan<byte> salt, int iterations = CryptoConstants.KekIterations)
        => Derive(password, salt, iterations, CryptoConstants.KekBytes);

    public static byte[] DeriveVerifier(string password, ReadOnlySpan<byte> salt, int iterations = CryptoConstants.VerifierIterations)
        => Derive(password, salt, iterations, CryptoConstants.VerifierBytes);

    private static byte[] Derive(string password, ReadOnlySpan<byte> salt, int iterations, int outputBytes)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        if (salt.Length < CryptoConstants.SaltBytes) throw new ArgumentException("Salt too short.", nameof(salt));
        if (iterations < 10_000) throw new ArgumentOutOfRangeException(nameof(iterations));
        if (outputBytes <= 0) throw new ArgumentOutOfRangeException(nameof(outputBytes));

        var pwdBytes = Encoding.UTF8.GetBytes(password);
        try
        {
            return Rfc2898DeriveBytes.Pbkdf2(pwdBytes, salt, iterations, HashAlgorithmName.SHA256, outputBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pwdBytes);
        }
    }
}
