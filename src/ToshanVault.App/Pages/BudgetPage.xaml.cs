using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.WinUI.UI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using ToshanVault.Core.Models;
using ToshanVault.Core.Security;
using ToshanVault.Data.Repositories;
using ToshanVault_App.Hosting;
using ToshanVault_App.Services;

namespace ToshanVault_App.Pages;

/// <summary>
/// Three-grid weekly budget view: Income, Fixed expenses, Variable expenses.
/// Each item is captured at its natural cadence (weekly/fortnightly/monthly/
/// quarterly/yearly) and converted to a weekly equivalent on a 52-week year
/// for the totals row. Surplus = Income − (Fixed + Variable) in both columns.
///
/// Categories use the existing budget_category table; on first run the page
/// auto-creates one default category per type so the user can start adding
/// items immediately. Items always live under their type's default category;
/// if the user wants finer per-type grouping later we can layer that on top
/// without a migration.
/// </summary>
public sealed partial class BudgetPage : Page
{
    private readonly BudgetItemRepository _items = AppHost.GetService<BudgetItemRepository>();
    private readonly BudgetCategoryRepository _cats = AppHost.GetService<BudgetCategoryRepository>();
    private readonly NavigationService _nav = AppHost.GetService<NavigationService>();

    private readonly ObservableCollection<BudgetRowVm> _income   = new();
    private readonly ObservableCollection<BudgetRowVm> _fixedEx  = new();
    private readonly ObservableCollection<BudgetRowVm> _variable = new();
    private readonly List<BudgetRowVm> _incomeAll   = new();
    private readonly List<BudgetRowVm> _fixedAll    = new();
    private readonly List<BudgetRowVm> _variableAll = new();

    private long _incomeCatId, _fixedCatId, _variableCatId;
    private string? _incSortKey, _fixSortKey, _varSortKey;
    private DataGridSortDirection? _incSortDir, _fixSortDir, _varSortDir;
    private bool _busy;

    public BudgetPage()
    {
        InitializeComponent();
        IncomeGrid.ItemsSource   = _income;
        FixedGrid.ItemsSource    = _fixedEx;
        VariableGrid.ItemsSource = _variable;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        try
        {
            await EnsureDefaultCategoriesAsync();
            await ReloadAsync();
        }
        catch (VaultLockedException) { _nav.NavigateToLogin(); }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    /// <summary>
    /// Make sure exactly one category per type exists and cache its id. If a
    /// category of the right type already exists (e.g. user edited the DB
    /// directly) the first one wins and we don't create a duplicate.
    /// </summary>
    private async Task EnsureDefaultCategoriesAsync()
    {
        var all = await _cats.GetAllAsync();
        async Task<long> EnsureAsync(BudgetCategoryType t, string defaultName)
        {
            var existing = all.FirstOrDefault(c => c.Type == t);
            if (existing is not null) return existing.Id;
            var c = new BudgetCategory { Name = defaultName, Type = t };
            return await _cats.InsertAsync(c);
        }
        _incomeCatId   = await EnsureAsync(BudgetCategoryType.Income,   "Income");
        _fixedCatId    = await EnsureAsync(BudgetCategoryType.Fixed,    "Fixed expenses");
        _variableCatId = await EnsureAsync(BudgetCategoryType.Variable, "Variable expenses");
    }

    private async Task ReloadAsync()
    {
        var rows = await _items.GetAllAsync();
        _incomeAll.Clear();
        _fixedAll.Clear();
        _variableAll.Clear();
        foreach (var r in rows)
        {
            var vm = new BudgetRowVm(r);
            if (r.CategoryId == _incomeCatId)        _incomeAll.Add(vm);
            else if (r.CategoryId == _fixedCatId)    _fixedAll.Add(vm);
            else if (r.CategoryId == _variableCatId) _variableAll.Add(vm);
            // Items in unknown categories are ignored — they belong to a
            // future per-type subgrouping that this page doesn't expose yet.
        }
        ApplySortAndBind();
        UpdateTotals();
    }

    private void ApplySortAndBind()
    {
        Bind(_income,   _incomeAll,   _incSortKey, _incSortDir);
        Bind(_fixedEx,  _fixedAll,    _fixSortKey, _fixSortDir);
        Bind(_variable, _variableAll, _varSortKey, _varSortDir);
    }

    private static void Bind(ObservableCollection<BudgetRowVm> target, IEnumerable<BudgetRowVm> source,
                             string? key, DataGridSortDirection? dir)
    {
        target.Clear();
        IEnumerable<BudgetRowVm> rows = source;
        if (key is not null && dir is not null)
        {
            var asc = dir == DataGridSortDirection.Ascending;
            Func<BudgetRowVm, IComparable> sel = key switch
            {
                "Amount"    => v => v.Amount,
                "Frequency" => v => (int)v.FrequencyEnum,
                "Weekly"    => v => v.Weekly,
                "Annual"    => v => v.Annual,
                _           => v => v.Label ?? string.Empty,
            };
            rows = asc ? source.OrderBy(sel) : source.OrderByDescending(sel);
        }
        foreach (var r in rows) target.Add(r);
    }

    private void UpdateTotals()
    {
        var incW = _incomeAll.Sum(v => v.Weekly);
        var incA = _incomeAll.Sum(v => v.Annual);
        var fixW = _fixedAll.Sum(v => v.Weekly);
        var fixA = _fixedAll.Sum(v => v.Annual);
        var varW = _variableAll.Sum(v => v.Weekly);
        var varA = _variableAll.Sum(v => v.Annual);

        IncomeWeeklyTotal.Text   = Money(incW);
        IncomeAnnualTotal.Text   = Money(incA);
        FixedWeeklyTotal.Text    = Money(fixW);
        FixedAnnualTotal.Text    = Money(fixA);
        VariableWeeklyTotal.Text = Money(varW);
        VariableAnnualTotal.Text = Money(varA);

        var surW = incW - fixW - varW;
        var surA = incA - fixA - varA;
        SurplusWeekly.Text = Money(surW);
        SurplusAnnual.Text = Money(surA);

        var critical = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
        var success  = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
        var brush = surA < 0 ? critical : success;
        SurplusWeekly.Foreground = brush;
        SurplusAnnual.Foreground = brush;
    }

    private static string Money(double v) => v.ToString("C", CultureInfo.GetCultureInfo("en-AU"));

    // ---- Sort handlers ----------------------------------------------------
    private void IncomeGrid_Sorting(object? _, DataGridColumnEventArgs e)
        => HandleSort(e, IncomeGrid, ref _incSortKey, ref _incSortDir, () => Bind(_income, _incomeAll, _incSortKey, _incSortDir));
    private void FixedGrid_Sorting(object? _, DataGridColumnEventArgs e)
        => HandleSort(e, FixedGrid, ref _fixSortKey, ref _fixSortDir, () => Bind(_fixedEx, _fixedAll, _fixSortKey, _fixSortDir));
    private void VariableGrid_Sorting(object? _, DataGridColumnEventArgs e)
        => HandleSort(e, VariableGrid, ref _varSortKey, ref _varSortDir, () => Bind(_variable, _variableAll, _varSortKey, _varSortDir));

    private static void HandleSort(DataGridColumnEventArgs e, DataGrid grid,
                                   ref string? key, ref DataGridSortDirection? dir, Action rebind)
    {
        var newKey = e.Column.Tag as string;
        if (string.IsNullOrEmpty(newKey)) return;
        var newDir = (key == newKey && dir == DataGridSortDirection.Ascending)
            ? DataGridSortDirection.Descending
            : DataGridSortDirection.Ascending;
        key = newKey; dir = newDir;
        foreach (var col in grid.Columns)
            col.SortDirection = ReferenceEquals(col, e.Column) ? newDir : null;
        rebind();
    }

    // ---- Selection --------------------------------------------------------
    private void IncomeGrid_SelectionChanged(object _, SelectionChangedEventArgs __)
    {
        var has = IncomeGrid.SelectedItem is BudgetRowVm;
        EditIncomeBtn.IsEnabled = has; DeleteIncomeBtn.IsEnabled = has;
    }
    private void FixedGrid_SelectionChanged(object _, SelectionChangedEventArgs __)
    {
        var has = FixedGrid.SelectedItem is BudgetRowVm;
        EditFixedBtn.IsEnabled = has; DeleteFixedBtn.IsEnabled = has;
    }
    private void VariableGrid_SelectionChanged(object _, SelectionChangedEventArgs __)
    {
        var has = VariableGrid.SelectedItem is BudgetRowVm;
        EditVariableBtn.IsEnabled = has; DeleteVariableBtn.IsEnabled = has;
    }

    // ---- Add / Edit / Delete ---------------------------------------------
    private async void AddIncome_Click   (object _, RoutedEventArgs __) => await AddOrEditAsync(BudgetCategoryType.Income,   _incomeCatId,   null);
    private async void AddFixed_Click    (object _, RoutedEventArgs __) => await AddOrEditAsync(BudgetCategoryType.Fixed,    _fixedCatId,    null);
    private async void AddVariable_Click (object _, RoutedEventArgs __) => await AddOrEditAsync(BudgetCategoryType.Variable, _variableCatId, null);

    private async void EditIncome_Click(object _, RoutedEventArgs __)
    {
        if (IncomeGrid.SelectedItem is BudgetRowVm vm) await AddOrEditAsync(BudgetCategoryType.Income, _incomeCatId, vm);
    }
    private async void EditFixed_Click(object _, RoutedEventArgs __)
    {
        if (FixedGrid.SelectedItem is BudgetRowVm vm) await AddOrEditAsync(BudgetCategoryType.Fixed, _fixedCatId, vm);
    }
    private async void EditVariable_Click(object _, RoutedEventArgs __)
    {
        if (VariableGrid.SelectedItem is BudgetRowVm vm) await AddOrEditAsync(BudgetCategoryType.Variable, _variableCatId, vm);
    }

    private async void IncomeGrid_DoubleTapped(object _, DoubleTappedRoutedEventArgs __)
    {
        if (IncomeGrid.SelectedItem is BudgetRowVm vm) await AddOrEditAsync(BudgetCategoryType.Income, _incomeCatId, vm);
    }
    private async void FixedGrid_DoubleTapped(object _, DoubleTappedRoutedEventArgs __)
    {
        if (FixedGrid.SelectedItem is BudgetRowVm vm) await AddOrEditAsync(BudgetCategoryType.Fixed, _fixedCatId, vm);
    }
    private async void VariableGrid_DoubleTapped(object _, DoubleTappedRoutedEventArgs __)
    {
        if (VariableGrid.SelectedItem is BudgetRowVm vm) await AddOrEditAsync(BudgetCategoryType.Variable, _variableCatId, vm);
    }

    private async Task AddOrEditAsync(BudgetCategoryType type, long categoryId, BudgetRowVm? existing)
    {
        if (_busy) return; _busy = true;
        try
        {
            var dlg = new BudgetItemDialog(this.XamlRoot, type, categoryId, existing?.Source);
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

            var item = dlg.Result!;
            if (existing is null) await _items.InsertAsync(item);
            else                  await _items.UpdateAsync(item);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            // Dialog mutates the BudgetItem in place; reload restores
            // authoritative state if persist failed mid-edit.
            try { await ReloadAsync(); } catch { /* best-effort */ }
        }
        finally { _busy = false; }
    }

    private async void DeleteIncome_Click(object _, RoutedEventArgs __)
    {
        if (IncomeGrid.SelectedItem is BudgetRowVm vm) await DeleteAsync(vm);
    }
    private async void DeleteFixed_Click(object _, RoutedEventArgs __)
    {
        if (FixedGrid.SelectedItem is BudgetRowVm vm) await DeleteAsync(vm);
    }
    private async void DeleteVariable_Click(object _, RoutedEventArgs __)
    {
        if (VariableGrid.SelectedItem is BudgetRowVm vm) await DeleteAsync(vm);
    }

    private async Task DeleteAsync(BudgetRowVm vm)
    {
        if (_busy) return; _busy = true;
        try
        {
            var confirm = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = $"Delete '{vm.Label}'?",
                Content = new TextBlock
                {
                    Text = "This permanently removes the budget item.",
                    TextWrapping = TextWrapping.Wrap,
                },
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
            await _items.DeleteAsync(vm.Id);
            await ReloadAsync();
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { _busy = false; }
    }

    private void ShowError(string msg)
    {
        InfoBar.Severity = InfoBarSeverity.Error;
        InfoBar.Title = "Error";
        InfoBar.Message = msg;
        InfoBar.IsOpen = true;
    }
}

/// <summary>
/// Display model for one budget row. Holds the underlying
/// <see cref="BudgetItem"/> so edits round-trip without losing the
/// CategoryId or any future fields. Weekly/Annual are derived from
/// (Amount, Frequency) using a 52-week year.
/// </summary>
internal sealed class BudgetRowVm
{
    public BudgetItem Source { get; }
    public long Id => Source.Id;
    public string Label => Source.Label;
    public double Amount => Source.Amount;
    public BudgetFrequency FrequencyEnum => Source.Frequency;
    public string Frequency => Source.Frequency.ToString();
    public double Weekly => ToWeekly(Source.Amount, Source.Frequency);
    public double Annual => Weekly * 52.0;

    public string AmountDisplay => Amount.ToString("C", CultureInfo.GetCultureInfo("en-AU"));
    public string WeeklyDisplay => Weekly.ToString("C", CultureInfo.GetCultureInfo("en-AU"));
    public string AnnualDisplay => Annual.ToString("C", CultureInfo.GetCultureInfo("en-AU"));

    public BudgetRowVm(BudgetItem r) { Source = r; }

    /// <summary>Convert (amount, frequency) → weekly equivalent on a 52-week
    /// year. OneOff contributes nothing because the page totals are recurring
    /// cashflow; including a one-off in a running weekly average would be
    /// misleading. Months/quarters use 12/4 periods per year respectively.</summary>
    private static double ToWeekly(double amount, BudgetFrequency f) => f switch
    {
        BudgetFrequency.Weekly      => amount,
        BudgetFrequency.Fortnightly => amount / 2.0,
        BudgetFrequency.Monthly     => amount * 12.0 / 52.0,
        BudgetFrequency.Quarterly   => amount * 4.0  / 52.0,
        BudgetFrequency.Yearly      => amount / 52.0,
        BudgetFrequency.OneOff      => 0.0,
        _                            => 0.0,
    };
}
