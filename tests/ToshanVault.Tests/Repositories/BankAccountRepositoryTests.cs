using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ToshanVault.Core.Models;
using ToshanVault.Core.Security;
using ToshanVault.Data.Repositories;

namespace ToshanVault.Tests.Repositories;

[TestClass]
public class BankAccountRepositoryTests
{
    private static BankAccount Sample(string bank = "ANZ", string name = "Joint Everyday") => new()
    {
        Bank = bank,
        AccountName = name,
        Bsb = "012-345",
        AccountNumber = "12345678",
        AccountType = BankAccountType.Savings,
        HolderName = "T. Patel",
        InterestRatePct = 4.5,
        Notes = "primary",
        Website = "https://www.anz.com",
    };

    [TestMethod]
    public async Task Insert_RoundTrips_AllFields_AndStampsTimestamps()
    {
        using var f = await TestDbFactory.CreateMigratedAsync();
        var repo = new BankAccountRepository(f);

        var a = Sample();
        await repo.InsertAsync(a);
        a.Id.Should().BeGreaterThan(0);
        a.CreatedAt.Should().NotBe(default);
        a.UpdatedAt.Should().NotBe(default);

        var got = await repo.GetAsync(a.Id);
        got.Should().NotBeNull();
        got!.Bank.Should().Be("ANZ");
        got.AccountName.Should().Be("Joint Everyday");
        got.AccountType.Should().Be(BankAccountType.Savings);
        got.InterestRatePct.Should().Be(4.5);
        got.IsClosed.Should().BeFalse();
        got.ClosedDate.Should().BeNull();
        got.VaultEntryId.Should().BeNull();
        got.Website.Should().Be("https://www.anz.com");
    }

    [TestMethod]
    public async Task GetActive_And_GetClosed_FilterCorrectly()
    {
        using var f = await TestDbFactory.CreateMigratedAsync();
        var repo = new BankAccountRepository(f);

        var a1 = Sample("ANZ", "Everyday"); await repo.InsertAsync(a1);
        var a2 = Sample("CBA", "Savings");  await repo.InsertAsync(a2);
        await repo.CloseAsync(a2.Id, "switched providers");

        (await repo.GetActiveAsync()).Should().ContainSingle().Which.Bank.Should().Be("ANZ");
        (await repo.GetClosedAsync()).Should().ContainSingle().Which.Bank.Should().Be("CBA");
    }

    [TestMethod]
    public async Task CloseAsync_SetsFlag_StampDate_AndReason()
    {
        using var f = await TestDbFactory.CreateMigratedAsync();
        var repo = new BankAccountRepository(f);

        var a = Sample(); await repo.InsertAsync(a);
        var when = new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero);
        await repo.CloseAsync(a.Id, "moved out", when);

        var got = await repo.GetAsync(a.Id);
        got!.IsClosed.Should().BeTrue();
        got.ClosedDate.Should().Be(when);
        got.CloseReason.Should().Be("moved out");
    }

    [TestMethod]
    public async Task CloseAsync_IsIdempotent_DoesNotOverwriteOriginalStamp()
    {
        using var f = await TestDbFactory.CreateMigratedAsync();
        var repo = new BankAccountRepository(f);

        var a = Sample(); await repo.InsertAsync(a);
        var first = new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero);
        await repo.CloseAsync(a.Id, "first", first);
        await repo.CloseAsync(a.Id, "second", first.AddDays(1));

        var got = await repo.GetAsync(a.Id);
        got!.ClosedDate.Should().Be(first);
        got.CloseReason.Should().Be("first");
    }

    [TestMethod]
    public async Task ReopenAsync_ClearsClosedFields()
    {
        using var f = await TestDbFactory.CreateMigratedAsync();
        var repo = new BankAccountRepository(f);

        var a = Sample(); await repo.InsertAsync(a);
        await repo.CloseAsync(a.Id, "oops");
        await repo.ReopenAsync(a.Id);

        var got = await repo.GetAsync(a.Id);
        got!.IsClosed.Should().BeFalse();
        got.ClosedDate.Should().BeNull();
        got.CloseReason.Should().BeNull();
    }

    [TestMethod]
    public async Task ReopenAsync_OnAlreadyOpenAccount_Throws()
    {
        using var f = await TestDbFactory.CreateMigratedAsync();
        var repo = new BankAccountRepository(f);

        var a = Sample(); await repo.InsertAsync(a);
        var act = async () => await repo.ReopenAsync(a.Id);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [TestMethod]
    public async Task VaultEntryDelete_NullsOutLink_NotCascades()
    {
        using var f = await TestDbFactory.CreateMigratedAsync();
        var meta = new MetaRepository(f);
        var vault = new Vault(meta);
        await vault.InitialiseAsync("!Arvind@Nivas83!");
        using var _v = vault;

        var entryRepo = new VaultEntryRepository(f);
        var bankRepo  = new BankAccountRepository(f);

        var entryId = await entryRepo.InsertAsync(new VaultEntry { Kind = "bank_login", Name = "ANZ login" });
        var a = Sample(); a.VaultEntryId = entryId;
        await bankRepo.InsertAsync(a);

        await entryRepo.DeleteAsync(entryId);

        var got = await bankRepo.GetAsync(a.Id);
        got.Should().NotBeNull("deleting the linked vault entry must not delete the bank row");
        got!.VaultEntryId.Should().BeNull();
    }

    [TestMethod]
    public async Task Insert_RejectsBlankBankOrName()
    {
        using var f = await TestDbFactory.CreateMigratedAsync();
        var repo = new BankAccountRepository(f);

        var act = async () => await repo.InsertAsync(new BankAccount { Bank = "", AccountName = "x", AccountType = BankAccountType.Other });
        await act.Should().ThrowAsync<ArgumentException>();

        var act2 = async () => await repo.InsertAsync(new BankAccount { Bank = "ANZ", AccountName = "", AccountType = BankAccountType.Other });
        await act2.Should().ThrowAsync<ArgumentException>();
    }

    [TestMethod]
    public async Task UpdateSortOrder_PersistsOrder_AndRespectsOrderingInGet()
    {
        using var f = await TestDbFactory.CreateMigratedAsync();
        var repo = new BankAccountRepository(f);

        var a = Sample("ANZ", "A"); await repo.InsertAsync(a);
        var b = Sample("CBA", "B"); await repo.InsertAsync(b);
        var c = Sample("WBC", "C"); await repo.InsertAsync(c);

        // New order: c, a, b
        await repo.UpdateSortOrderAsync(new[] { c.Id, a.Id, b.Id });

        var open = await repo.GetActiveAsync();
        open.Select(x => x.Id).Should().Equal(new[] { c.Id, a.Id, b.Id });
    }

    [TestMethod]
    public async Task UpdateSortOrder_LeavesUnlistedRowsAlone()
    {
        using var f = await TestDbFactory.CreateMigratedAsync();
        var repo = new BankAccountRepository(f);

        var a = Sample("ANZ", "A"); await repo.InsertAsync(a);
        var b = Sample("CBA", "B"); await repo.InsertAsync(b);
        var c = Sample("WBC", "C"); await repo.InsertAsync(c);
        await repo.CloseAsync(c.Id, "done", DateTimeOffset.UtcNow);

        // Reorder only the open list. Closed item must keep its slot.
        await repo.UpdateSortOrderAsync(new[] { b.Id, a.Id });

        var open = await repo.GetActiveAsync();
        open.Select(x => x.Id).Should().Equal(new[] { b.Id, a.Id });
        var closed = await repo.GetClosedAsync();
        closed.Single().Id.Should().Be(c.Id);
    }
}
