using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ToshanVault.Core.Models;

namespace ToshanVault.Tests.Models;

[TestClass]
public class RecipeCategorizerTests
{
    [DataTestMethod]
    [DataRow("Butter Chicken",                     "Chicken")]
    [DataRow("Hyderabadi Chicken Biryani",         "Chicken")]
    [DataRow("Tandoori chicken Tikka",             "Chicken")]
    [DataRow("Egg Curry",                          "Egg")]
    [DataRow("Eggless Cake",                       "Egg")]
    [DataRow("Paneer Bhurji",                      "Other")]
    [DataRow("Daal Tadka",                         "Other")]
    [DataRow("",                                   "Other")]
    [DataRow(null,                                 "Other")]
    public void Classify_BucketsCorrectly(string? title, string expected)
        => RecipeCategorizer.Classify(title).Should().Be(expected);

    [TestMethod]
    public void Classify_DoesNotMatchSubstrings()
    {
        // Word-boundary regex prevents these false positives.
        RecipeCategorizer.Classify("Eggplant Curry").Should().Be("Other");
        RecipeCategorizer.Classify("Chickpea Curry").Should().Be("Other");
    }

    [TestMethod]
    public void Classify_EggBeatsChicken_WhenBothPresent()
    {
        RecipeCategorizer.Classify("Chicken Egg Curry").Should().Be("Egg");
    }
}
