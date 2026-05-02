using System.Security.Cryptography;

namespace ToshanVault.Core.Security;

/// <summary>
/// AES-256-GCM helper with random 12-byte IV and 16-byte authentication tag.
/// </summary>
public static class AesGcmCrypto
{
    public readonly record struct Sealed(byte[] Iv, byte[] Ciphertext, byte[] Tag);

    public static Sealed Encrypt(ReadOnlySpan<byte> key, ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData = default)
    {
        if (key.Length != CryptoConstants.KekBytes) throw new ArgumentException("Key must be 32 bytes.", nameof(key));

        var iv = RandomNumberGenerator.GetBytes(CryptoConstants.GcmIvBytes);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[CryptoConstants.GcmTagBytes];

        using var aes = new AesGcm(key, CryptoConstants.GcmTagBytes);
        aes.Encrypt(iv, plaintext, ciphertext, tag, associatedData);

        return new Sealed(iv, ciphertext, tag);
    }

    public static byte[] Decrypt(ReadOnlySpan<byte> key, ReadOnlySpan<byte> iv, ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> tag, ReadOnlySpan<byte> associatedData = default)
    {
        if (key.Length != CryptoConstants.KekBytes) throw new ArgumentException("Key must be 32 bytes.", nameof(key));
        if (iv.Length != CryptoConstants.GcmIvBytes) throw new ArgumentException("IV must be 12 bytes.", nameof(iv));
        if (tag.Length != CryptoConstants.GcmTagBytes) throw new ArgumentException("Tag must be 16 bytes.", nameof(tag));

        var plaintext = new byte[ciphertext.Length];
        try
        {
            using var aes = new AesGcm(key, CryptoConstants.GcmTagBytes);
            aes.Decrypt(iv, ciphertext, tag, plaintext, associatedData);
            return plaintext;
        }
        catch (AuthenticationTagMismatchException ex)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new TamperedDataException("Authentication tag mismatch — data has been tampered with or the wrong key was used.", ex);
        }
    }
}
