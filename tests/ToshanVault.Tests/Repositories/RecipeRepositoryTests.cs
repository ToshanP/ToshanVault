using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ToshanVault.Core.Models;
using ToshanVault.Data.Repositories;

namespace ToshanVault.Tests.Repositories;

[TestClass]
public class RecipeRepositoryTests
{
    [TestMethod]
    public async Task Recipe_CrudRoundTrip_StampsAddedAt()
    {
        using var f = await TestDbFactory.CreateMigratedAsync();
        var repo = new RecipeRepository(f);

        var r = new Recipe { Title = "Dal Tadka", Cuisine = "Indian", Rating = 5, IsFavourite = true };
        await repo.InsertAsync(r);
        r.AddedAt.Should().NotBe(default);

        var got = await repo.GetAsync(r.Id);
        got!.IsFavourite.Should().BeTrue();
        got.Rating.Should().Be(5);
    }

    [TestMethod]
    public async Task Recipe_Tags_SetGetReplaceDedupe()
    {
        using var f = await TestDbFactory.CreateMigratedAsync();
        var repo = new RecipeRepository(f);

        var r = new Recipe { Title = "Pasta" };
        await repo.InsertAsync(r);

        await repo.SetTagsAsync(r.Id, new[] { "italian", "quick", "QUICK", "  vegetarian  ", "" });
        var tags = await repo.GetTagsAsync(r.Id);
        tags.Should().BeEquivalentTo(new[] { "italian", "quick", "vegetarian" });

        // Re-set should replace, not append.
        await repo.SetTagsAsync(r.Id, new[] { "noodles" });
        (await repo.GetTagsAsync(r.Id)).Should().BeEquivalentTo(new[] { "noodles" });
    }

    [TestMethod]
    public async Task Recipe_Delete_CascadesToTags()
    {
        using var f = await TestDbFactory.CreateMigratedAsync();
        var repo = new RecipeRepository(f);
        var r = new Recipe { Title = "X" };
        await repo.InsertAsync(r);
        await repo.SetTagsAsync(r.Id, new[] { "a", "b" });
        await repo.DeleteAsync(r.Id);
        (await repo.GetTagsAsync(r.Id)).Should().BeEmpty();
    }
}
