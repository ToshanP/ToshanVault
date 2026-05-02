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
}
