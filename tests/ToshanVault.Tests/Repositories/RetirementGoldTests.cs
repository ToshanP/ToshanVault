using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ToshanVault.Core.Models;
using ToshanVault.Data.Repositories;

namespace ToshanVault.Tests.Repositories;

[TestClass]
public class RetirementGoldTests
{
    [TestMethod]
    public async Task RetirementItem_CrudRoundTrip()
    {
        using var f = await TestDbFactory.CreateMigratedAsync();
        var repo = new RetirementItemRepository(f);

        var r = new RetirementItem
        {
            Label = "Pension",
            Kind = RetirementKind.Income,
            MonthlyAmountJan2025 = 4000,
            InflationPct = 2.5,
            Indexed = true,
            StartAge = 67,
            EndAge = null,
            Notes = "indexed to CPI",
        };
        await repo.InsertAsync(r);

        var got = await repo.GetAsync(r.Id);
        got!.Indexed.Should().BeTrue();
        got.StartAge.Should().Be(67);
        got.EndAge.Should().BeNull();
        got.Kind.Should().Be(RetirementKind.Income);
    }

    [TestMethod]
    public async Task GoldItem_CrudRoundTrip()
    {
        using var f = await TestDbFactory.CreateMigratedAsync();
        var repo = new GoldItemRepository(f);
        var g = new GoldItem
        {
            ItemName = "Mangalsutra", Purity = "22K", Qty = 1, Tola = 2, GramsCalc = 23.32,
        };
        await repo.InsertAsync(g);
        var got = await repo.GetAsync(g.Id);
        got!.GramsCalc.Should().Be(23.32);
    }

    [TestMethod]
    public async Task GoldPriceCache_UpsertReplacesExisting()
    {
        using var f = await TestDbFactory.CreateMigratedAsync();
        var repo = new GoldPriceCacheRepository(f);

        var stamp1 = DateTimeOffset.UtcNow.AddHours(-1);
        await repo.UpsertAsync(new GoldPriceCache { Currency = "AUD", PricePerGram24k = 150, FetchedAt = stamp1 });
        (await repo.GetAsync("AUD"))!.PricePerGram24k.Should().Be(150);

        var stamp2 = DateTimeOffset.UtcNow;
        await repo.UpsertAsync(new GoldPriceCache { Currency = "AUD", PricePerGram24k = 152, FetchedAt = stamp2 });
        var got = await repo.GetAsync("AUD");
        got!.PricePerGram24k.Should().Be(152);
        got.FetchedAt.Should().BeCloseTo(stamp2, TimeSpan.FromSeconds(1));
    }

    [TestMethod]
    public async Task GoldPriceCache_StaleUpsert_DoesNotClobberFresh()
    {
        using var f = await TestDbFactory.CreateMigratedAsync();
        var repo = new GoldPriceCacheRepository(f);

        var fresh = DateTimeOffset.UtcNow;
        var stale = fresh.AddMinutes(-30);

        await repo.UpsertAsync(new GoldPriceCache { Currency = "AUD", PricePerGram24k = 200, FetchedAt = fresh });
        await repo.UpsertAsync(new GoldPriceCache { Currency = "AUD", PricePerGram24k = 100, FetchedAt = stale });

        var got = await repo.GetAsync("AUD");
        got!.PricePerGram24k.Should().Be(200);
        got.FetchedAt.Should().BeCloseTo(fresh, TimeSpan.FromSeconds(1));
    }

    [TestMethod]
    public void MintInvestmentCalculator_AccumulatesCashAndPhysicalGold()
    {
        var plan = new MintInvestmentPlan
        {
            AccountStartDate = new DateOnly(2026, 1, 1),
            FortnightlyContributionAud = 500,
            WorkingUnitOunces = 1,
            PricePerOunceAud = 1500,
            ConsolidationTargetOunces = 10,
        };
        var due = new DateOnly(2026, 1, 29);
        var purchases = new[]
        {
            new MintInvestmentPurchase
            {
                DueDate = due,
                CompletedDate = due,
                Ounces = 1,
                PricePerOunceAud = 1500,
            },
        };

        var summary = MintInvestmentCalculator.Summarise(plan, purchases, due);

        summary.ContributionsToDate.Should().Be(1500);
        summary.CompletedPurchaseCost.Should().Be(1500);
        summary.MintAccountCash.Should().Be(0);
        summary.PhysicalOunces.Should().Be(1);
        summary.PhysicalValue.Should().Be(1500);
    }

    [TestMethod]
    public void MintInvestmentCalculator_ReplaysCompletedPurchasesAfterAssumptionChange()
    {
        var plan = new MintInvestmentPlan
        {
            AccountStartDate = new DateOnly(2026, 1, 1),
            FortnightlyContributionAud = 500,
            WorkingUnitOunces = 1,
            PricePerOunceAud = 5000,
            ConsolidationTargetOunces = 10,
        };
        var purchases = new[]
        {
            new MintInvestmentPurchase
            {
                DueDate = new DateOnly(2026, 1, 29),
                CompletedDate = new DateOnly(2026, 1, 29),
                Ounces = 1,
                PricePerOunceAud = 2500,
            },
        };

        var rows = MintInvestmentCalculator.GenerateSchedule(
            plan,
            purchases,
            new DateOnly(2026, 2, 1),
            futureRows: 1);

        rows.Should().ContainSingle();
        rows[0].DueDate.Should().Be(new DateOnly(2026, 7, 16));
        rows[0].CompletedDate.Should().BeNull();
        rows[0].EstimatedCost.Should().Be(5000);
    }

    [TestMethod]
    public void MintInvestmentCalculator_KeepsOverdueUntickedPurchasesInSchedule()
    {
        var plan = new MintInvestmentPlan
        {
            AccountStartDate = new DateOnly(2026, 1, 1),
            FortnightlyContributionAud = 500,
            WorkingUnitOunces = 1,
            PricePerOunceAud = 1500,
            ConsolidationTargetOunces = 10,
        };

        var rows = MintInvestmentCalculator.GenerateSchedule(
            plan,
            Array.Empty<MintInvestmentPurchase>(),
            new DateOnly(2026, 2, 10),
            futureRows: 2);

        rows.Should().HaveCount(2);
        rows[0].DueDate.Should().Be(new DateOnly(2026, 1, 29));
        rows[0].CompletedDate.Should().BeNull();
        rows[1].DueDate.Should().Be(new DateOnly(2026, 3, 12));
    }

    [TestMethod]
    public void MintInvestmentCalculator_GeneratesScheduleWhenRemindersDisabled()
    {
        var plan = new MintInvestmentPlan
        {
            Enabled = false,
            AccountStartDate = new DateOnly(2026, 1, 1),
            FortnightlyContributionAud = 500,
            WorkingUnitOunces = 1,
            PricePerOunceAud = 1500,
            ConsolidationTargetOunces = 10,
        };

        var rows = MintInvestmentCalculator.GenerateSchedule(
            plan,
            Array.Empty<MintInvestmentPurchase>(),
            new DateOnly(2026, 1, 1),
            futureRows: 1);

        rows.Should().ContainSingle();
        rows[0].DueDate.Should().Be(new DateOnly(2026, 1, 29));
    }

    [TestMethod]
    public async Task MintInvestmentRepository_PersistsPlanAndPurchases()
    {
        using var f = await TestDbFactory.CreateMigratedAsync();
        var repo = new MintInvestmentRepository(f);
        var plan = new MintInvestmentPlan
        {
            AccountStartDate = new DateOnly(2026, 2, 1),
            FortnightlyContributionAud = 500,
            WorkingUnitOunces = 1,
            PricePerOunceAud = 5200,
            ReminderLeadDays = 10,
            ConsolidationTargetOunces = 10,
            Notes = "Perth Mint account",
        };

        await repo.UpsertPlanAsync(plan);
        var saved = await repo.GetPlanAsync();

        saved.AccountStartDate.Should().Be(new DateOnly(2026, 2, 1));
        saved.PricePerOunceAud.Should().Be(5200);
        saved.FortnightlyContributionAud.Should().Be(500);

        await repo.UpsertCompletedPurchaseAsync(new MintInvestmentPurchase
        {
            DueDate = new DateOnly(2026, 4, 26),
            CompletedDate = new DateOnly(2026, 4, 27),
            Ounces = 1,
            PricePerOunceAud = 5200,
        });

        var purchases = await repo.GetPurchasesAsync();
        purchases.Should().ContainSingle();
        purchases[0].CompletedDate.Should().Be(new DateOnly(2026, 4, 27));
        purchases[0].Ounces.Should().Be(1);
    }
}
