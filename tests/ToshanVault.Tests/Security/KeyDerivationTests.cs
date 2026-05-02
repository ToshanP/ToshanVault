using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ToshanVault.Core.Security;

namespace ToshanVault.Tests.Security;

[TestClass]
public class KeyDerivationTests
{
    private static readonly byte[] Salt = RandomNumberGenerator.GetBytes(CryptoConstants.SaltBytes);

    [TestMethod]
    public void DeriveKek_IsDeterministic()
    {
        var a = KeyDerivation.DeriveKek("hunter2", Salt, iterations: 10_000);
        var b = KeyDerivation.DeriveKek("hunter2", Salt, iterations: 10_000);
        a.Should().BeEquivalentTo(b);
        a.Length.Should().Be(CryptoConstants.KekBytes);
    }

    [TestMethod]
    public void DeriveKek_DifferentSalt_ProducesDifferentKey()
    {
        var s2 = RandomNumberGenerator.GetBytes(CryptoConstants.SaltBytes);
        var a = KeyDerivation.DeriveKek("hunter2", Salt, iterations: 10_000);
        var b = KeyDerivation.DeriveKek("hunter2", s2, iterations: 10_000);
        a.Should().NotBeEquivalentTo(b);
    }

    [TestMethod]
    public void DeriveVerifier_DifferentPassword_ProducesDifferentVerifier()
    {
        var a = KeyDerivation.DeriveVerifier("hunter2", Salt, iterations: 10_000);
        var b = KeyDerivation.DeriveVerifier("hunter3", Salt, iterations: 10_000);
        a.Should().NotBeEquivalentTo(b);
    }

    [TestMethod]
    public void Empty_Password_Throws()
    {
        var act = () => KeyDerivation.DeriveKek("", Salt);
        act.Should().Throw<ArgumentException>();
    }

    [TestMethod]
    public void Short_Salt_Throws()
    {
        var act = () => KeyDerivation.DeriveKek("hunter2", new byte[8]);
        act.Should().Throw<ArgumentException>();
    }
}
