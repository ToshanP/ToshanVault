using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ToshanVault.Core.Models;
using ToshanVault.Core.Security;
using ToshanVault.Data.Repositories;

namespace ToshanVault.Tests.Repositories;

[TestClass]
public class VaultRepositoryTests
{
    private const string Pwd = "!Arvind@Nivas83!";

    private static async Task<(TestDbFactory f, Vault vault)> SetupAsync()
    {
        var f = await TestDbFactory.CreateMigratedAsync();
        var meta = new MetaRepository(f);
        var vault = new Vault(meta);
        await vault.InitialiseAsync(Pwd);
        return (f, vault);
    }

    [TestMethod]
    public async Task VaultEntry_CrudRoundTrip_StampsTimestamps()
    {
        var (f, vault) = await SetupAsync();
        using var _ = f;
        using var __ = vault;
        var repo = new VaultEntryRepository(f);

        var e = new VaultEntry { Kind = "Login", Name = "Gmail" };
        await repo.InsertAsync(e);
        e.CreatedAt.Should().NotBe(default);
        e.UpdatedAt.Should().NotBe(default);

        var got = await repo.GetAsync(e.Id);
        got!.Name.Should().Be("Gmail");

        var firstUpdate = got.UpdatedAt;
        await Task.Delay(10);
        e.Name = "Gmail (work)";
        await repo.UpdateAsync(e);
        var got2 = await repo.GetAsync(e.Id);
        got2!.UpdatedAt.Should().BeAfter(firstUpdate);
    }

    [TestMethod]
    public async Task VaultField_RoundTrip_EncryptsAtRestDecryptsOnRead()
    {
        var (f, vault) = await SetupAsync();
        using var _ = f;
        using var __ = vault;

        var entryRepo = new VaultEntryRepository(f);
        var fieldRepo = new VaultFieldRepository(f, vault);

        var entryId = await entryRepo.InsertAsync(new VaultEntry { Kind = "Login", Name = "Bank" });
        var field = new VaultField
        {
            EntryId = entryId, Label = "Password", Value = "p@ssw0rd-with-₹", IsSecret = true,
        };
        await fieldRepo.InsertAsync(field);

        var got = await fieldRepo.GetAsync(field.Id);
        got!.Value.Should().Be("p@ssw0rd-with-₹");
        got.IsSecret.Should().BeTrue();

        // Read raw row from DB to confirm value is NOT plaintext on disk.
        await using var conn = f.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value_enc FROM vault_field WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id", field.Id);
        var raw = (byte[])(await cmd.ExecuteScalarAsync())!;
        var rawAsUtf8 = System.Text.Encoding.UTF8.GetString(raw);
        rawAsUtf8.Should().NotContain("p@ssw0rd");
    }

    [TestMethod]
    public async Task VaultField_UpdatedValue_RotatesIv()
    {
        var (f, vault) = await SetupAsync();
        using var _ = f;
        using var __ = vault;
        var entryRepo = new VaultEntryRepository(f);
        var fieldRepo = new VaultFieldRepository(f, vault);

        var entryId = await entryRepo.InsertAsync(new VaultEntry { Kind = "Login", Name = "X" });
        var field = new VaultField { EntryId = entryId, Label = "v", Value = "first" };
        await fieldRepo.InsertAsync(field);

        var iv1 = await ReadIvAsync(f, field.Id);
        field.Value = "second";
        await fieldRepo.UpdateAsync(field);
        var iv2 = await ReadIvAsync(f, field.Id);

        iv2.Should().NotBeEquivalentTo(iv1);
        (await fieldRepo.GetAsync(field.Id))!.Value.Should().Be("second");
    }

    [TestMethod]
    public async Task VaultEntry_Delete_CascadesToFields()
    {
        var (f, vault) = await SetupAsync();
        using var _ = f;
        using var __ = vault;
        var entryRepo = new VaultEntryRepository(f);
        var fieldRepo = new VaultFieldRepository(f, vault);

        var entryId = await entryRepo.InsertAsync(new VaultEntry { Kind = "Login", Name = "X" });
        await fieldRepo.InsertAsync(new VaultField { EntryId = entryId, Label = "a", Value = "1" });
        await fieldRepo.InsertAsync(new VaultField { EntryId = entryId, Label = "b", Value = "2" });
        (await fieldRepo.GetByEntryAsync(entryId)).Should().HaveCount(2);

        await entryRepo.DeleteAsync(entryId);
        (await fieldRepo.GetByEntryAsync(entryId)).Should().BeEmpty();
    }

    [TestMethod]
    public async Task VaultField_Insert_WhenVaultLocked_Throws()
    {
        var (f, vault) = await SetupAsync();
        using var _ = f;
        var entryRepo = new VaultEntryRepository(f);
        var fieldRepo = new VaultFieldRepository(f, vault);
        var entryId = await entryRepo.InsertAsync(new VaultEntry { Kind = "Login", Name = "X" });

        vault.Lock();

        var act = async () => await fieldRepo.InsertAsync(new VaultField
        {
            EntryId = entryId, Label = "x", Value = "y",
        });
        await act.Should().ThrowAsync<VaultLockedException>();
    }

    private static async Task<byte[]> ReadIvAsync(TestDbFactory f, long fieldId)
    {
        await using var conn = f.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT iv FROM vault_field WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id", fieldId);
        return (byte[])(await cmd.ExecuteScalarAsync())!;
    }

    [TestMethod]
    public async Task VaultEntry_UpdateSortOrder_PersistsOrder_AndIsKindAware()
    {
        var (f, vault) = await SetupAsync();
        using var _ = f;
        using var __ = vault;
        var repo = new VaultEntryRepository(f);

        var a = new VaultEntry { Kind = "Web", Name = "A" }; await repo.InsertAsync(a);
        var b = new VaultEntry { Kind = "Web", Name = "B" }; await repo.InsertAsync(b);
        var c = new VaultEntry { Kind = "Web", Name = "C" }; await repo.InsertAsync(c);
        var other = new VaultEntry { Kind = "Login", Name = "Z" }; await repo.InsertAsync(other);
        var otherSortBefore = (await repo.GetAsync(other.Id))!.SortOrder;

        await repo.UpdateSortOrderAsync(new[] { c.Id, a.Id, b.Id });

        var web = await repo.GetByKindAsync("Web");
        web.Select(x => x.Id).Should().Equal(new[] { c.Id, a.Id, b.Id });

        // Untouched entry of a different kind keeps its sort_order.
        (await repo.GetAsync(other.Id))!.SortOrder.Should().Be(otherSortBefore);
    }
}
