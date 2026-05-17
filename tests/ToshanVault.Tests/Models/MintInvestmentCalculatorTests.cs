using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ToshanVault.Core.Models;

namespace ToshanVault.Tests.Models;

[TestClass]
public class MintInvestmentCalculatorTests
{
    private static MintInvestmentPlan MakePlan(DateOnly start) => new()
    {
        AccountStartDate = start,
        PricePerOunceAud = 4000,
        FortnightlyContributionAud = 500,
        WorkingUnitOunces = 0.5,
    };

    [TestMethod]
    public void GenerateFortnightDetails_ReturnsOnlyRowsWithinFY()
    {
        var plan = MakePlan(new DateOnly(2024, 7, 1));
        var actuals = new Dictionary<DateOnly, MintFortnightActual>();
        var fyStart = new DateOnly(2024, 6, 30);
        var fyEnd = new DateOnly(2025, 6, 30);

        var result = MintInvestmentCalculator.GenerateFortnightDetails(plan, actuals, fyStart, fyEnd);

        result.Should().NotBeEmpty();
        result.Should().AllSatisfy(r =>
        {
            r.Date.Should().BeAfter(fyStart);
            r.Date.Should().BeOnOrBefore(fyEnd);
        });
    }

    [TestMethod]
    public void GenerateFortnightDetails_UsesActualWhenProvided()
    {
        var plan = MakePlan(new DateOnly(2024, 7, 1));
        var overrideDate = new DateOnly(2024, 7, 1);
        var actuals = new Dictionary<DateOnly, MintFortnightActual>
        {
            [overrideDate] = new() { FortnightDate = overrideDate, ActualOz = 1.5, ActualContribution = 999 },
        };
        var fyStart = new DateOnly(2024, 6, 30);
        var fyEnd = new DateOnly(2025, 6, 30);

        var result = MintInvestmentCalculator.GenerateFortnightDetails(plan, actuals, fyStart, fyEnd);

        var first = result[0];
        first.Date.Should().Be(overrideDate);
        first.Contribution.Should().Be(999);
        first.PurchaseOz.Should().Be(1.5);
    }

    [TestMethod]
    public void GenerateFortnightDetails_RunningOzAccumulates()
    {
        var plan = MakePlan(new DateOnly(2024, 7, 1));
        var actuals = new Dictionary<DateOnly, MintFortnightActual>();
        var fyStart = new DateOnly(2024, 6, 30);
        var fyEnd = new DateOnly(2025, 6, 30);

        var result = MintInvestmentCalculator.GenerateFortnightDetails(plan, actuals, fyStart, fyEnd);

        double prevOz = 0;
        foreach (var snap in result)
        {
            snap.RunningOz.Should().BeGreaterThanOrEqualTo(prevOz, $"RunningOz should be non-decreasing at {snap.Date}");
            prevOz = snap.RunningOz;
        }
    }

    [TestMethod]
    public void GenerateFortnightDetails_ForwardPropagation_AffectsSubsequentRows()
    {
        var plan = MakePlan(new DateOnly(2024, 7, 1));
        var overrideDate = new DateOnly(2024, 7, 1);

        var noOverride = MintInvestmentCalculator.GenerateFortnightDetails(
            plan, new Dictionary<DateOnly, MintFortnightActual>(),
            new DateOnly(2024, 6, 30), new DateOnly(2025, 6, 30));

        var actuals = new Dictionary<DateOnly, MintFortnightActual>
        {
            [overrideDate] = new() { FortnightDate = overrideDate, ActualOz = 5.0, ActualContribution = 500 },
        };
        var withOverride = MintInvestmentCalculator.GenerateFortnightDetails(
            plan, actuals, new DateOnly(2024, 6, 30), new DateOnly(2025, 6, 30));

        withOverride[0].RunningOz.Should().BeGreaterThan(noOverride[0].RunningOz);
        withOverride[0].CashBalance.Should().BeLessThan(noOverride[0].CashBalance);
    }
}
