using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ToshanVault.Core.Models;
using ToshanVault.Importer;

namespace ToshanVault.Tests.Importer;

[TestClass]
public class GoldImporterTests
{
    [TestMethod]
    public void TolaToGrams_UsesIndianTroyConstant()
    {
        // 1 tola = 11.6638038 g (Indian troy measurement).
        GoldImporter.TolaToGrams(1).Should().BeApproximately(11.6638038, 1e-6);
        GoldImporter.TolaToGrams(15).Should().BeApproximately(174.957057, 1e-4);
        GoldImporter.TolaToGrams(0).Should().Be(0);
    }

    [TestMethod]
    public void ParseRows_SkipsHeaderAndBlankRows()
    {
        var rows = GoldImporter.ParseRows(new (string, string, string)[]
        {
            ("",           "",     ""),       // empty (row 1 styling)
            ("Description", "Qty", "Tola"),   // header band (row 2) — col B=="Qty"
            ("",           "",     ""),       // separator (row 3)
            ("Sangam",     "1",    "12"),
            ("Bangles",    "14",   "15"),
            ("",           "",     ""),       // trailing blank
        });

        rows.Should().HaveCount(2);
        rows[0].Description.Should().Be("Sangam");
        rows[0].Qty.Should().Be(1);
        rows[0].Tola.Should().Be(12);
        rows[1].Description.Should().Be("Bangles");
        rows[1].Tola.Should().Be(15);
    }

    [TestMethod]
    public void ParseRows_TreatsUnparseableNumbersAsZero()
    {
        var rows = GoldImporter.ParseRows(new (string, string, string)[]
        {
            ("Mystery item", "?", "n/a"),
        });

        rows.Should().HaveCount(1);
        rows[0].Qty.Should().Be(0);
        rows[0].Tola.Should().Be(0);
    }

    [TestMethod]
    public void ParseRows_DropsRowsWithBlankDescription()
    {
        var rows = GoldImporter.ParseRows(new (string, string, string)[]
        {
            ("",        "1", "2"),  // no description — drop
            ("Anklet",  "1", "1"),
        });

        rows.Should().HaveCount(1);
        rows[0].Description.Should().Be("Anklet");
    }
}

[TestClass]
public class GoldPriceServiceMathTests
{
    [DataTestMethod]
    [DataRow("24K",     1.0)]
    [DataRow("22K",     22.0 / 24.0)]
    [DataRow("18K",     0.75)]
    [DataRow("14K",     14.0 / 24.0)]
    [DataRow("10K",     10.0 / 24.0)]
    [DataRow("Diamond", 0.0)]
    [DataRow("",        0.0)]
    [DataRow(null,      0.0)]
    public void PurityFraction_MatchesKaratRatio(string? purity, double expected)
        => GoldValueCalculator.PurityFraction(purity).Should().BeApproximately(expected, 1e-6);

    [TestMethod]
    public void EstimateValue_AppliesPurityAndPrice()
    {
        var v = GoldValueCalculator.EstimateValue(11.6638, "22K", 100);
        v.Should().BeApproximately(11.6638 * (22.0/24.0) * 100, 1e-6);
    }

    [TestMethod]
    public void EstimateValue_DiamondReturnsZero()
        => GoldValueCalculator.EstimateValue(50, "Diamond", 200).Should().Be(0);

    [TestMethod]
    public void EstimateValue_ZeroPriceReturnsZero()
        => GoldValueCalculator.EstimateValue(50, "22K", 0).Should().Be(0);
}
