using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ToshanVault.Core.Models;
using ToshanVault.Data.Repositories;

namespace ToshanVault.Tests.Repositories;

[TestClass]
public class BudgetRepositoryTests
{
    [TestMethod]
    public async Task BudgetCategory_CrudRoundTrip()
    {
        using var f = await TestDbFactory.CreateMigratedAsync();
        var repo = new BudgetCategoryRepository(f);

        var c = new BudgetCategory { Name = "Salary", Type = BudgetCategoryType.Income };
        var id = await repo.InsertAsync(c);
        id.Should().BeGreaterThan(0);
        c.Id.Should().Be(id);

        var got = await repo.GetAsync(id);
        got.Should().NotBeNull();
        got!.Name.Should().Be("Salary");
        got.Type.Should().Be(BudgetCategoryType.Income);

        c.Name = "Salary (Renamed)";
        await repo.UpdateAsync(c);
        (await repo.GetAsync(id))!.Name.Should().Be("Salary (Renamed)");

        (await repo.GetAllAsync()).Should().ContainSingle();
        (await repo.DeleteAsync(id)).Should().Be(1);
        (await repo.GetAsync(id)).Should().BeNull();
    }

    [TestMethod]
    public async Task BudgetItem_CrudRoundTrip_PreservesEnumAndNullable()
    {
        using var f = await TestDbFactory.CreateMigratedAsync();
        var catRepo = new BudgetCategoryRepository(f);
        var itemRepo = new BudgetItemRepository(f);

        var catId = await catRepo.InsertAsync(new BudgetCategory { Name = "Groceries", Type = BudgetCategoryType.Variable });

        var item = new BudgetItem
        {
            CategoryId = catId,
            Label = "Coles weekly",
            Amount = 250.50,
            Frequency = BudgetFrequency.Monthly,
            Notes = null,
            SortOrder = 1,
        };
        await itemRepo.InsertAsync(item);

        var got = await itemRepo.GetAsync(item.Id);
        got!.Frequency.Should().Be(BudgetFrequency.Monthly);
        got.Notes.Should().BeNull();
        got.Amount.Should().Be(250.50);
    }

    [TestMethod]
    public async Task BudgetCategory_DeleteWithChildItems_IsRestricted()
    {
        // FK is ON DELETE RESTRICT — caller must remove items before category.
        using var f = await TestDbFactory.CreateMigratedAsync();
        var catRepo = new BudgetCategoryRepository(f);
        var itemRepo = new BudgetItemRepository(f);

        var catId = await catRepo.InsertAsync(new BudgetCategory { Name = "Misc", Type = BudgetCategoryType.Variable });
        await itemRepo.InsertAsync(new BudgetItem
        {
            CategoryId = catId, Label = "x", Amount = 1, Frequency = BudgetFrequency.Monthly,
        });

        var act = async () => await catRepo.DeleteAsync(catId);
        await act.Should().ThrowAsync<Microsoft.Data.Sqlite.SqliteException>();

        // Items survive
        (await itemRepo.GetByCategoryAsync(catId)).Should().HaveCount(1);
    }

    [TestMethod]
    public async Task BudgetItem_Update_NonExistent_Throws()
    {
        using var f = await TestDbFactory.CreateMigratedAsync();
        var itemRepo = new BudgetItemRepository(f);

        var act = async () => await itemRepo.UpdateAsync(new BudgetItem
        {
            Id = 9999, CategoryId = 1, Label = "x", Amount = 1, Frequency = BudgetFrequency.Monthly,
        });
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
