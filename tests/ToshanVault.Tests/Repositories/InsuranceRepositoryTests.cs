using Dapper;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ToshanVault.Core.Models;
using ToshanVault.Core.Security;
using ToshanVault.Data.Repositories;

namespace ToshanVault.Tests.Repositories;

[TestClass]
public class InsuranceRepositoryTests
{
    private static Insurance Sample(string insurer = "Bupa", string? policy = "POL-12345") => new()
    {
        InsurerCompany = insurer,
        PolicyNumber   = policy,
        InsuranceType  = "Health",
        Website        = "https://www.bupa.com.au",
        Owner          = "Toshan",
        RenewalDate    = new DateOnly(2026, 6, 1),
    };

    [TestMethod]
    public async Task Insert_RoundTrips_AllFields_AndStampsTimestamps()
    {
        using var f = await TestDbFactory.CreateMigratedAsync();
        var repo = new InsuranceRepository(f);

        var i = Sample();
        await repo.InsertAsync(i);
        i.Id.Should().BeGreaterThan(0);
        i.CreatedAt.Should().NotBe(default);
        i.UpdatedAt.Should().NotBe(default);

        var got = await repo.GetAsync(i.Id);
        got.Should().NotBeNull();
        got!.InsurerCompany.Should().Be("Bupa");
        got.PolicyNumber.Should().Be("POL-12345");
        got.InsuranceType.Should().Be("Health");
        got.Website.Should().Be("https://www.bupa.com.au");
        got.Owner.Should().Be("Toshan");
        got.RenewalDate.Should().Be(new DateOnly(2026, 6, 1));
        got.VaultEntryId.Should().BeNull();
    }

    [TestMethod]
    public async Task Insert_AllowsNullOwner_AndUpdate_PersistsOwnerChange()
    {
        using var f = await TestDbFactory.CreateMigratedAsync();
        var repo = new InsuranceRepository(f);

        var i = new Insurance { InsurerCompany = "Allianz", Owner = null };
        await repo.InsertAsync(i);
        var got = await repo.GetAsync(i.Id);
        got!.Owner.Should().BeNull("legacy rows from migration 010 lack owner");

        got.Owner = "Devu";
        await repo.UpdateAsync(got);
        var reloaded = await repo.GetAsync(i.Id);
        reloaded!.Owner.Should().Be("Devu");
    }

    [TestMethod]
    public async Task Insert_RejectsBlankInsurer()
    {
        using var f = await TestDbFactory.CreateMigratedAsync();
        var repo = new InsuranceRepository(f);

        var act = async () => await repo.InsertAsync(new Insurance { InsurerCompany = "" });
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [TestMethod]
    public async Task GetAll_OrdersBySortOrder_ThenRenewalDateAsTieBreaker()
    {
        // After migration 015 the primary sort is the user-controlled
        // sort_order column. New inserts append at the end (max+1), so the
        // natural order is insertion order. Renewal date / nulls-last only
        // matters as a tie-breaker for legacy rows whose sort_order collides.
        using var f = await TestDbFactory.CreateMigratedAsync();
        var repo = new InsuranceRepository(f);

        await repo.InsertAsync(new Insurance { InsurerCompany = "Z-no-date", RenewalDate = null });
        await repo.InsertAsync(new Insurance { InsurerCompany = "Late",  RenewalDate = new DateOnly(2027, 1, 1) });
        await repo.InsertAsync(new Insurance { InsurerCompany = "Soon",  RenewalDate = new DateOnly(2026, 1, 1) });

        var all = await repo.GetAllAsync();
        all.Select(x => x.InsurerCompany).Should().Equal("Z-no-date", "Late", "Soon");
    }

    [TestMethod]
    public async Task DeleteInsurance_CascadesToCredentialsVaultEntry()
    {
        using var f = await TestDbFactory.CreateMigratedAsync();
        var meta = new MetaRepository(f);
        var vault = new Vault(meta);
        await vault.InitialiseAsync("!Arvind@Nivas83!");
        using var _v = vault;

        var entries = new VaultEntryRepository(f);
        var insRepo = new InsuranceRepository(f);
        var creds   = new InsuranceCredentialsService(f, vault, entries, insRepo);

        var i = Sample(); await insRepo.InsertAsync(i);
        await creds.SaveAsync(i.Id, new[]
        {
            new InsuranceCredentialsService.FieldSpec(InsuranceCredentialsService.UsernameLabel, "user1", false),
            new InsuranceCredentialsService.FieldSpec(InsuranceCredentialsService.PasswordLabel, "pass1", true),
        });

        var afterSave = await insRepo.GetAsync(i.Id);
        afterSave!.VaultEntryId.Should().NotBeNull();
        var entryId = afterSave.VaultEntryId!.Value;

        await insRepo.DeleteAsync(i.Id);

        var orphan = await entries.GetAsync(entryId);
        orphan.Should().BeNull("trg_insurance_after_delete must remove the credentials vault_entry");
    }

    [TestMethod]
    public async Task VaultEntryDelete_NullsOutLink_NotCascades()
    {
        using var f = await TestDbFactory.CreateMigratedAsync();
        var meta = new MetaRepository(f);
        var vault = new Vault(meta);
        await vault.InitialiseAsync("!Arvind@Nivas83!");
        using var _v = vault;

        var entries = new VaultEntryRepository(f);
        var insRepo = new InsuranceRepository(f);

        var entryId = await entries.InsertAsync(new VaultEntry { Kind = Insurance.CredentialsEntryKind, Name = "Bupa login" });
        var i = Sample(); i.VaultEntryId = entryId;
        await insRepo.InsertAsync(i);

        await entries.DeleteAsync(entryId);

        var got = await insRepo.GetAsync(i.Id);
        got.Should().NotBeNull("deleting the linked vault_entry must not delete the insurance row");
        got!.VaultEntryId.Should().BeNull();
    }

    [TestMethod]
    public async Task UpdateSortOrder_PersistsOrder_AndLeavesUnlistedRowsAlone()
    {
        using var f = await TestDbFactory.CreateMigratedAsync();
        var repo = new InsuranceRepository(f);

        var a = Sample("Aon",  "A"); await repo.InsertAsync(a);
        var b = Sample("Bupa", "B"); await repo.InsertAsync(b);
        var c = Sample("CGU",  "C"); await repo.InsertAsync(c);
        var dExtra = Sample("Domino", "D"); await repo.InsertAsync(dExtra);
        var dExtraSortBefore = (await repo.GetAsync(dExtra.Id))!.SortOrder;

        // Reorder only a/b/c — d must keep its slot.
        await repo.UpdateSortOrderAsync(new[] { c.Id, a.Id, b.Id });

        var all = await repo.GetAllAsync();
        // Three reordered rows come first (sort_order 1/2/3); d keeps its
        // original sort_order (= its id, set by migration backfill on insert).
        all.Take(3).Select(x => x.Id).Should().Equal(new[] { c.Id, a.Id, b.Id });
        (await repo.GetAsync(dExtra.Id))!.SortOrder.Should().Be(dExtraSortBefore);
    }

    [TestMethod]
    public async Task Migration015_Backfill_PreservesPriorRenewalDateOrdering()
    {
        // Simulates the pre-migration state by inserting rows then resetting
        // sort_order to 0, and re-runs the same backfill statement migration
        // 015 ships. Asserts the order matches the prior contract
        // (renewal_date asc, nulls last). This is the reviewer's concern: we
        // must not bury soon-due policies under id order on first migration.
        using var f = await TestDbFactory.CreateMigratedAsync();
        var repo = new InsuranceRepository(f);

        await repo.InsertAsync(new Insurance { InsurerCompany = "Z-no-date", RenewalDate = null });
        await repo.InsertAsync(new Insurance { InsurerCompany = "Late",  RenewalDate = new DateOnly(2027, 1, 1) });
        await repo.InsertAsync(new Insurance { InsurerCompany = "Soon",  RenewalDate = new DateOnly(2026, 1, 1) });

        await using var conn = f.Open();
        // Reset to the post-ALTER, pre-backfill state.
        await conn.ExecuteAsync("UPDATE insurance SET sort_order = 0;");
        // Re-run the migration's backfill verbatim.
        await conn.ExecuteAsync(@"
            UPDATE insurance
               SET sort_order = ranked.rn
              FROM (
                   SELECT id,
                          ROW_NUMBER() OVER (
                              ORDER BY (renewal_date IS NULL), renewal_date, insurer_company, id
                          ) AS rn
                     FROM insurance
                   ) AS ranked
             WHERE insurance.id = ranked.id
               AND insurance.sort_order = 0;");

        var all = await repo.GetAllAsync();
        all.Select(x => x.InsurerCompany).Should().Equal("Soon", "Late", "Z-no-date");
    }
}

[TestClass]
public class InsuranceCredentialsServiceTests
{
    [TestMethod]
    public async Task Save_Then_Load_RoundTripsFields()
    {
        using var f = await TestDbFactory.CreateMigratedAsync();
        var meta = new MetaRepository(f);
        var vault = new Vault(meta);
        await vault.InitialiseAsync("!Arvind@Nivas83!");
        using var _v = vault;

        var entries = new VaultEntryRepository(f);
        var insRepo = new InsuranceRepository(f);
        var svc     = new InsuranceCredentialsService(f, vault, entries, insRepo);

        var i = new Insurance { InsurerCompany = "NRMA", InsuranceType = "Car" };
        await insRepo.InsertAsync(i);

        await svc.SaveAsync(i.Id, new[]
        {
            new InsuranceCredentialsService.FieldSpec(InsuranceCredentialsService.UsernameLabel, "u", false),
            new InsuranceCredentialsService.FieldSpec(InsuranceCredentialsService.PasswordLabel, "p", true),
            new InsuranceCredentialsService.FieldSpec(InsuranceCredentialsService.NotesLabel,    "n", false),
        });

        var loaded = await svc.LoadAsync(i.Id);
        loaded[InsuranceCredentialsService.UsernameLabel].Should().Be("u");
        loaded[InsuranceCredentialsService.PasswordLabel].Should().Be("p");
        loaded[InsuranceCredentialsService.NotesLabel].Should().Be("n");
    }

    [TestMethod]
    public async Task Save_AllEmpty_OnFreshPolicy_DoesNotCreateVaultEntry()
    {
        using var f = await TestDbFactory.CreateMigratedAsync();
        var meta = new MetaRepository(f);
        var vault = new Vault(meta);
        await vault.InitialiseAsync("!Arvind@Nivas83!");
        using var _v = vault;

        var entries = new VaultEntryRepository(f);
        var insRepo = new InsuranceRepository(f);
        var svc     = new InsuranceCredentialsService(f, vault, entries, insRepo);

        var i = new Insurance { InsurerCompany = "AIA" };
        await insRepo.InsertAsync(i);

        await svc.SaveAsync(i.Id, new[]
        {
            new InsuranceCredentialsService.FieldSpec(InsuranceCredentialsService.UsernameLabel, "", false),
            new InsuranceCredentialsService.FieldSpec(InsuranceCredentialsService.PasswordLabel, null, true),
        });

        var got = await insRepo.GetAsync(i.Id);
        got!.VaultEntryId.Should().BeNull("saving only empty fields must not lazily create a vault_entry");
    }

    [TestMethod]
    public async Task LoadLabels_ReturnsOnlyRequested()
    {
        using var f = await TestDbFactory.CreateMigratedAsync();
        var meta = new MetaRepository(f);
        var vault = new Vault(meta);
        await vault.InitialiseAsync("!Arvind@Nivas83!");
        using var _v = vault;

        var entries = new VaultEntryRepository(f);
        var insRepo = new InsuranceRepository(f);
        var svc     = new InsuranceCredentialsService(f, vault, entries, insRepo);

        var i = new Insurance { InsurerCompany = "Allianz" };
        await insRepo.InsertAsync(i);
        await svc.SaveAsync(i.Id, new[]
        {
            new InsuranceCredentialsService.FieldSpec(InsuranceCredentialsService.UsernameLabel, "u", false),
            new InsuranceCredentialsService.FieldSpec(InsuranceCredentialsService.PasswordLabel, "p", true),
            new InsuranceCredentialsService.FieldSpec(InsuranceCredentialsService.NotesLabel,    "n", false),
        });

        var loaded = await svc.LoadLabelsAsync(i.Id, new[] { InsuranceCredentialsService.NotesLabel });
        loaded.Should().HaveCount(1);
        loaded[InsuranceCredentialsService.NotesLabel].Should().Be("n");
    }
}
