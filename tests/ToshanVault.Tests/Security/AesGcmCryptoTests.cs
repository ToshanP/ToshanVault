using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ToshanVault.Core.Security;

namespace ToshanVault.Tests.Security;

[TestClass]
public class AesGcmCryptoTests
{
    private static readonly byte[] Key = RandomNumberGenerator.GetBytes(CryptoConstants.KekBytes);

    [TestMethod]
    public void RoundTrip_PreservesPlaintext()
    {
        var plaintext = Encoding.UTF8.GetBytes("This is a secret. ₹1,23,456");
        var sealedBlob = AesGcmCrypto.Encrypt(Key, plaintext);
        var roundTripped = AesGcmCrypto.Decrypt(Key, sealedBlob.Iv, sealedBlob.Ciphertext, sealedBlob.Tag);
        roundTripped.Should().BeEquivalentTo(plaintext);
    }

    [TestMethod]
    public void Encrypt_ProducesUniqueIv_PerCall()
    {
        var plaintext = new byte[] { 1, 2, 3 };
        var a = AesGcmCrypto.Encrypt(Key, plaintext);
        var b = AesGcmCrypto.Encrypt(Key, plaintext);
        a.Iv.Should().NotBeEquivalentTo(b.Iv);
        a.Ciphertext.Should().NotBeEquivalentTo(b.Ciphertext);
    }

    [TestMethod]
    public void Tampered_Tag_Throws()
    {
        var s = AesGcmCrypto.Encrypt(Key, new byte[] { 1, 2, 3 });
        s.Tag[0] ^= 0xFF;
        var act = () => AesGcmCrypto.Decrypt(Key, s.Iv, s.Ciphertext, s.Tag);
        act.Should().Throw<TamperedDataException>();
    }

    [TestMethod]
    public void Tampered_Ciphertext_Throws()
    {
        var s = AesGcmCrypto.Encrypt(Key, new byte[] { 1, 2, 3 });
        s.Ciphertext[0] ^= 0xFF;
        var act = () => AesGcmCrypto.Decrypt(Key, s.Iv, s.Ciphertext, s.Tag);
        act.Should().Throw<TamperedDataException>();
    }

    [TestMethod]
    public void WrongKey_Throws()
    {
        var s = AesGcmCrypto.Encrypt(Key, new byte[] { 1, 2, 3 });
        var wrongKey = RandomNumberGenerator.GetBytes(CryptoConstants.KekBytes);
        var act = () => AesGcmCrypto.Decrypt(wrongKey, s.Iv, s.Ciphertext, s.Tag);
        act.Should().Throw<TamperedDataException>();
    }

    [TestMethod]
    public void AssociatedData_MustMatch()
    {
        var aad = Encoding.UTF8.GetBytes("context-A");
        var aadBad = Encoding.UTF8.GetBytes("context-B");
        var s = AesGcmCrypto.Encrypt(Key, new byte[] { 1, 2, 3 }, aad);
        var act = () => AesGcmCrypto.Decrypt(Key, s.Iv, s.Ciphertext, s.Tag, aadBad);
        act.Should().Throw<TamperedDataException>();
    }

    [TestMethod]
    public void WrongKeySize_Throws()
    {
        var act = () => AesGcmCrypto.Encrypt(new byte[16], new byte[] { 1 });
        act.Should().Throw<ArgumentException>();
    }
}
