using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ToshanVault.Tests;

/// <summary>
/// Tests the search-matching logic used by the global SearchPage.
/// SearchResultVm lives in the App project (can't reference due to WinUI),
/// so we replicate its matching algorithm here to verify correctness.
/// </summary>
[TestClass]
public class SearchMatchingTests
{
    /// <summary>
    /// Replicates SearchResultVm's matching logic from SearchPage.xaml.cs.
    /// </summary>
    private sealed class SearchResult
    {
        public string Name { get; }
        public string Subtitle { get; }
        private readonly string _searchText;

        public SearchResult(string name, string subtitle, params string?[] extraSearchFields)
        {
            Name = name;
            Subtitle = subtitle;
            _searchText = string.Join('\n',
                new[] { name, subtitle }
                    .Concat(extraSearchFields.Where(s => !string.IsNullOrEmpty(s)))
                    .Distinct(StringComparer.OrdinalIgnoreCase));
        }

        public bool Matches(string filter) =>
            _searchText.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void Matches_ByName_CaseInsensitive()
    {
        var vm = new SearchResult("Edco Wonder Cloth", "Toshan", "Toshan");
        vm.Matches("edco").Should().BeTrue();
        vm.Matches("EDCO").Should().BeTrue();
        vm.Matches("Wonder").Should().BeTrue();
    }

    [TestMethod]
    public void Matches_BySubtitle()
    {
        var vm = new SearchResult("Edco Wonder Cloth", "Toshan", "Toshan");
        vm.Matches("toshan").Should().BeTrue();
    }

    [TestMethod]
    public void Matches_ByExtraField()
    {
        // Insurance example with extra search fields
        var vm = new SearchResult("AIA Health", "Toshan · Health",
            "Toshan", "POL123", "Health", "aia.com.au");
        vm.Matches("POL123").Should().BeTrue();
        vm.Matches("aia.com").Should().BeTrue();
        vm.Matches("health").Should().BeTrue();
    }

    [TestMethod]
    public void Matches_ReturnsFalse_WhenNoFieldContainsFilter()
    {
        var vm = new SearchResult("Edco Wonder Cloth", "Toshan", "Toshan");
        vm.Matches("xyz123").Should().BeFalse();
    }

    [TestMethod]
    public void Matches_NullExtraFieldsIgnored()
    {
        var vm = new SearchResult("My Bank", "Commonwealth", null, "", null);
        vm.Matches("Commonwealth").Should().BeTrue();
        vm.Matches("My Bank").Should().BeTrue();
    }

    [TestMethod]
    public void Matches_VaultEntry_ByCategory()
    {
        // Vault: SearchResultVm("vault", r.Name, subtitle, r.Owner, r.Category)
        var vm = new SearchResult("Edco Wonder Cloth", "Toshan · Shopping", "Toshan", "Shopping");
        vm.Matches("Shopping").Should().BeTrue();
        vm.Matches("Edco").Should().BeTrue();
    }

    [TestMethod]
    public void Matches_NoteEntry_ByNameAndOwner()
    {
        // Notes: SearchResultVm("notes", r.Name, r.Owner ?? "", r.Owner)
        var vm = new SearchResult("Edco Wonder Cloth", "Toshan", "Toshan");
        vm.Matches("Edco").Should().BeTrue("note title should match");
        vm.Matches("Toshan").Should().BeTrue("note owner should match");
    }

    [TestMethod]
    public void Matches_BankEntry_ByWebsite()
    {
        // Banks: SearchResultVm("banks", b.AccountName, b.Bank, b.Bank, b.Website)
        var vm = new SearchResult("Savings Account", "Commonwealth Bank",
            "Commonwealth Bank", "commbank.com.au");
        vm.Matches("commbank").Should().BeTrue();
        vm.Matches("Savings").Should().BeTrue();
    }

    [TestMethod]
    public void Matches_PartialWord()
    {
        var vm = new SearchResult("Edco Wonder Cloth", "Toshan", "Toshan");
        vm.Matches("Ed").Should().BeTrue();
        vm.Matches("Cloth").Should().BeTrue();
        vm.Matches("der Cl").Should().BeTrue();
    }
}
