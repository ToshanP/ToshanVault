using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ToshanVault.Tests;

[TestClass]
public class SmokeTests
{
    [TestMethod]
    public void TestRunner_Works()
    {
        (1 + 1).Should().Be(2);
    }
}
