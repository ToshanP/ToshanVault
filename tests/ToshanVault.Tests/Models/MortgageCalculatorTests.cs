using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ToshanVault.Core.Models;

namespace ToshanVault.Tests.Models;

[TestClass]
public class MortgageCalculatorTests
{
    [TestMethod]
    public void ScheduledPayment_KnownGood_545kAt6PctOver30YrMonthly()
    {
        // Online amortization calculators: 545000 @ 6% / 30yr monthly ≈ 3267.66
        var p = MortgageCalculator.ScheduledPayment(545000, 6.0, 30, RepaymentFrequency.Monthly);
        p.Should().BeInRange(3267.0, 3268.5);
    }

    [TestMethod]
    public void ScheduledPayment_ZeroRate_DividesEvenly()
    {
        var p = MortgageCalculator.ScheduledPayment(120000, 0.0, 10, RepaymentFrequency.Monthly);
        p.Should().BeApproximately(1000.0, 0.001);
    }

    [TestMethod]
    public void Amortize_NoExtra_FullySchedules()
    {
        var start = new DateOnly(2026, 1, 1);
        var r = MortgageCalculator.Amortize(545000, 6.0, 30, RepaymentFrequency.Monthly, 0, start);

        r.PeriodsToPayoff.Should().Be(360);
        r.PayoffDate.Should().Be(start.AddMonths(360));
        r.YearSummaries.Count.Should().Be(30);
        r.YearSummaries[^1].EndingBalance.Should().BeLessThan(0.01);
        r.TotalInterest.Should().BeApproximately(r.TotalPaid - 545000, 0.5);
    }

    [TestMethod]
    public void Amortize_ExtraPayment_ShortensTermAndSavesInterest()
    {
        var start = new DateOnly(2026, 1, 1);
        var baseline  = MortgageCalculator.Amortize(545000, 6.0, 30, RepaymentFrequency.Fortnightly, 0,   start);
        var withExtra = MortgageCalculator.Amortize(545000, 6.0, 30, RepaymentFrequency.Fortnightly, 500, start);

        withExtra.PeriodsToPayoff.Should().BeLessThan(baseline.PeriodsToPayoff);
        withExtra.TotalInterest.Should().BeLessThan(baseline.TotalInterest);
        withExtra.PayoffDate.Should().BeBefore(baseline.PayoffDate);
    }

    [TestMethod]
    public void AmortizeWithMinimumPayment_UsesEnteredMinimumPlusExtra()
    {
        var start = new DateOnly(2026, 1, 1);
        var minimum = 1600;

        var baseline = MortgageCalculator.AmortizeWithMinimumPayment(
            545000, 6.0, RepaymentFrequency.Fortnightly, minimum, 0, start);
        var withExtra = MortgageCalculator.AmortizeWithMinimumPayment(
            545000, 6.0, RepaymentFrequency.Fortnightly, minimum, 500, start);

        baseline.ScheduledPayment.Should().Be(minimum);
        baseline.ActualPayment.Should().Be(minimum);
        withExtra.ScheduledPayment.Should().Be(minimum);
        withExtra.ActualPayment.Should().Be(minimum + 500);
        withExtra.PeriodsToPayoff.Should().BeLessThan(baseline.PeriodsToPayoff);
    }

    [TestMethod]
    public void Amortize_ZeroRate_PaysExactlyPrincipal()
    {
        var start = new DateOnly(2026, 1, 1);
        var r = MortgageCalculator.Amortize(120000, 0.0, 10, RepaymentFrequency.Monthly, 0, start);
        r.PeriodsToPayoff.Should().Be(120);
        r.TotalInterest.Should().BeApproximately(0.0, 0.001);
        r.TotalPaid.Should().BeApproximately(120000.0, 0.001);
    }

    [TestMethod]
    public void Amortize_ExtraExceedsBalance_PaysOffInOnePeriod()
    {
        var start = new DateOnly(2026, 1, 1);
        var r = MortgageCalculator.Amortize(1000, 6.0, 30, RepaymentFrequency.Monthly, 5000, start);
        r.PeriodsToPayoff.Should().Be(1);
        r.TotalPaid.Should().BeApproximately(1005.0, 0.5);
    }

    [TestMethod]
    public void PeriodsPerYear_MapsCorrectly()
    {
        MortgageCalculator.PeriodsPerYear(RepaymentFrequency.Weekly).Should().Be(52);
        MortgageCalculator.PeriodsPerYear(RepaymentFrequency.Fortnightly).Should().Be(26);
        MortgageCalculator.PeriodsPerYear(RepaymentFrequency.Monthly).Should().Be(12);
    }
}

[TestClass]
public class GoldAccumulatorTests
{
    [TestMethod]
    public void Project_ZeroGrowth_TotalEqualsContributions()
    {
        var start = new DateOnly(2026, 1, 1);
        // 26 fortnights = 1 year of contributions
        var r = GoldAccumulator.Project(500, 0.0, RepaymentFrequency.Fortnightly, start, start, 26);
        r.TotalContributed.Should().BeApproximately(13000.0, 0.001);
        r.FinalValue.Should().BeApproximately(13000.0, 0.001);
    }

    [TestMethod]
    public void Project_Growth_BalanceExceedsContributions()
    {
        var start = new DateOnly(2026, 1, 1);
        var r = GoldAccumulator.Project(500, 5.0, RepaymentFrequency.Fortnightly, start, start, 260); // 10 yrs
        r.TotalContributed.Should().BeApproximately(130000.0, 0.001);
        r.FinalValue.Should().BeGreaterThan(r.TotalContributed);
    }

    [TestMethod]
    public void Project_StartDateInFuture_NoContributionsBefore()
    {
        var start     = new DateOnly(2026, 1, 1);
        var goldStart = new DateOnly(2028, 5, 1);
        var r = GoldAccumulator.Project(500, 0.0, RepaymentFrequency.Fortnightly, start, goldStart, 52); // 2 yrs
        // Periods within the first ~2 years prior to gold start contribute nothing.
        r.TotalContributed.Should().BeLessThan(13000); // less than a full year of contributions
    }

    [TestMethod]
    public void Project_YearSummariesAlignWithLoanYears()
    {
        var start = new DateOnly(2026, 1, 1);
        var r = GoldAccumulator.Project(500, 5.0, RepaymentFrequency.Fortnightly, start, start, 78); // 3 yrs
        r.YearValues.Count.Should().Be(3);
    }

    [TestMethod]
    public void Project_PhysicalPurchase_DoesNotDoubleCountCashOrBuyExtraBoundary()
    {
        var start = new DateOnly(2026, 1, 1);
        var plan = new GoldAccumulator.PhysicalPurchasePlan(
            Enabled: true,
            BarOunces: 1,
            IntervalMonths: 1,
            PricePerOunceAud: 100,
            StartDate: start);

        var r = GoldAccumulator.Project(
            perPeriod: 500,
            annualGrowthPct: 0,
            frequency: RepaymentFrequency.Monthly,
            loanStart: start,
            goldStart: start,
            totalPeriods: 12,
            physicalPlan: plan);

        r.TotalContributed.Should().Be(6000);
        r.TotalPhysicalSpent.Should().Be(1200);
        r.TotalOunces.Should().Be(12);
        r.FinalValue.Should().Be(6000);
    }
}
