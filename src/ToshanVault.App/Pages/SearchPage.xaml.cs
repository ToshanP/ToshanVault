using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ToshanVault.Core.Security;
using ToshanVault.Data.Repositories;
using ToshanVault_App.Hosting;
using ToshanVault_App.Services;

namespace ToshanVault_App.Pages;

public sealed partial class SearchPage : Page
{
    private readonly VaultEntryRepository _entryRepo     = AppHost.GetService<VaultEntryRepository>();
    private readonly BankAccountRepository _bankRepo     = AppHost.GetService<BankAccountRepository>();
    private readonly InsuranceRepository _insuranceRepo  = AppHost.GetService<InsuranceRepository>();
    private readonly NavigationService _nav              = AppHost.GetService<NavigationService>();

    private readonly List<SearchResultVm> _vaultAll     = new();
    private readonly List<SearchResultVm> _bankAll      = new();
    private readonly List<SearchResultVm> _insuranceAll = new();
    private readonly List<SearchResultVm> _notesAll     = new();

    private readonly ObservableCollection<SearchResultVm> _vaultResults     = new();
    private readonly ObservableCollection<SearchResultVm> _bankResults      = new();
    private readonly ObservableCollection<SearchResultVm> _insuranceResults = new();
    private readonly ObservableCollection<SearchResultVm> _notesResults     = new();

    private string _filter = string.Empty;

    public SearchPage()
    {
        InitializeComponent();
        VaultList.ItemsSource     = _vaultResults;
        BankingList.ItemsSource   = _bankResults;
        InsuranceList.ItemsSource = _insuranceResults;
        NotesList.ItemsSource     = _notesResults;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        try { await LoadAllAsync(); }
        catch (VaultLockedException) { _nav.NavigateToLogin(); }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async Task LoadAllAsync()
    {
        _vaultAll.Clear();
        _bankAll.Clear();
        _insuranceAll.Clear();
        _notesAll.Clear();

        // Load each section independently so one failure doesn't block others
        await LoadVaultAsync();
        await LoadBankingAsync();
        await LoadInsuranceAsync();
        await LoadNotesAsync();

        ApplyFilter();
    }

    private async Task LoadVaultAsync()
    {
        try
        {
            var rows = await _entryRepo.GetByKindAsync(WebCredentialsService.EntryKind);
            foreach (var r in rows)
            {
                var subtitle = JoinParts(r.Owner, r.Category);
                _vaultAll.Add(new SearchResultVm("vault", r.Name, subtitle, r.Owner, r.Category));
            }
        }
        catch (Exception ex) { ShowError($"Vault: {ex.Message}"); }
    }

    private async Task LoadBankingAsync()
    {
        try
        {
            var banks = await _bankRepo.GetAllAsync();
            foreach (var b in banks.Where(b => !b.IsClosed))
                _bankAll.Add(new SearchResultVm("banks", b.AccountName, b.Bank, b.Bank, b.Website));
        }
        catch (Exception ex) { ShowError($"Banking: {ex.Message}"); }
    }

    private async Task LoadInsuranceAsync()
    {
        try
        {
            var insurance = await _insuranceRepo.GetAllAsync();
            foreach (var i in insurance)
            {
                var subtitle = JoinParts(i.Owner, i.InsuranceType);
                _insuranceAll.Add(new SearchResultVm("insurance", i.InsurerCompany, subtitle,
                    i.Owner, i.PolicyNumber, i.InsuranceType, i.Website));
            }
        }
        catch (Exception ex) { ShowError($"Insurance: {ex.Message}"); }
    }

    private async Task LoadNotesAsync()
    {
        try
        {
            var notes = await _entryRepo.GetByKindAsync(GeneralNotesService.EntryKind);
            foreach (var r in notes)
                _notesAll.Add(new SearchResultVm("notes", r.Name, r.Owner ?? "", r.Owner));
        }
        catch (Exception ex) { ShowError($"Notes: {ex.Message}"); }
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        _filter = sender.Text.Trim();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (string.IsNullOrEmpty(_filter))
        {
            ClearResults();
            EmptyPrompt.Visibility = Visibility.Visible;
            NoResultsText.Visibility = Visibility.Collapsed;
            return;
        }

        EmptyPrompt.Visibility = Visibility.Collapsed;

        FilterSection(_vaultAll, _vaultResults, VaultSection, VaultHeader, "Vault");
        FilterSection(_bankAll, _bankResults, BankingSection, BankingHeader, "Banking");
        FilterSection(_insuranceAll, _insuranceResults, InsuranceSection, InsuranceHeader, "Insurance");
        FilterSection(_notesAll, _notesResults, NotesSection, NotesHeader, "Notes");

        var total = _vaultResults.Count + _bankResults.Count
                  + _insuranceResults.Count + _notesResults.Count;
        NoResultsText.Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void FilterSection(
        List<SearchResultVm> all,
        ObservableCollection<SearchResultVm> results,
        StackPanel section,
        TextBlock header,
        string label)
    {
        results.Clear();
        foreach (var vm in all)
            if (vm.Matches(_filter))
                results.Add(vm);

        if (results.Count > 0)
        {
            header.Text = $"{label} ({results.Count})";
            section.Visibility = Visibility.Visible;
        }
        else
        {
            section.Visibility = Visibility.Collapsed;
        }
    }

    private void ClearResults()
    {
        _vaultResults.Clear();
        _bankResults.Clear();
        _insuranceResults.Clear();
        _notesResults.Clear();
        VaultSection.Visibility = Visibility.Collapsed;
        BankingSection.Visibility = Visibility.Collapsed;
        InsuranceSection.Visibility = Visibility.Collapsed;
        NotesSection.Visibility = Visibility.Collapsed;
    }

    private void Result_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el && el.DataContext is SearchResultVm vm)
            _nav.NavigateInShell(vm.SectionTag);
    }

    private void ShowError(string message)
    {
        InfoBar.Message = message;
        InfoBar.Severity = InfoBarSeverity.Error;
        InfoBar.IsOpen = true;
    }

    private static string JoinParts(params string?[] parts) =>
        string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
}

internal sealed class SearchResultVm
{
    public string SectionTag { get; }
    public string Name { get; }
    public string Subtitle { get; }
    private readonly string _searchText;

    public SearchResultVm(string sectionTag, string name, string subtitle, params string?[] extraSearchFields)
    {
        SectionTag = sectionTag;
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
