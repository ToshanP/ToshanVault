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
/// Two-grid view of retirement-era recurring Income and Expense items, with
/// auto-calculated weekly equivalents and a surplus row. Reuses the existing
/// <see cref="RetirementItem"/> table; <c>MonthlyAmountJan2025 × 12</c> is the
/// annual figure shown to the user. Other RetirementItem fields (inflation,
/// indexing, ages) are accepted at default values from this page and remain
/// available for the projection logic on the Retirement Planning page.
/// </summary>
public sealed partial class RetirementIncomeExpensePage : Page
{
    private readonly RetirementItemRepository _repo = AppHost.GetService<RetirementItemRepository>();
    private readonly NavigationService _nav = AppHost.GetService<NavigationService>();

    private readonly ObservableCollection<ItemVm> _expenses = new();
    private readonly ObservableCollection<ItemVm> _income   = new();
    private readonly List<ItemVm> _expenseAll = new();
    private readonly List<ItemVm> _incomeAll  = new();

    private string? _expenseSortKey, _incomeSortKey;
    private DataGridSortDirection? _expenseSortDir, _incomeSortDir;
    private bool _busy;

    public RetirementIncomeExpensePage()
    {
        InitializeComponent();
        ExpenseGrid.ItemsSource = _expenses;
        IncomeGrid.ItemsSource  = _income;

        // Sync the totals/surplus column widths to the DataGrid's actual
        // column widths after every layout pass. This compensates for the
        // vertical scrollbar gutter (and any future auto-sized DataGrid
        // column changes) so the Total / Surplus values line up exactly
        // under the grid columns above them. The 4th "spacer" column on
        // each totals grid absorbs the residual width so columns 1-3 keep
        // matching the DataGrid widths instead of being stretched by *.
        ExpenseGrid.LayoutUpdated += (_, __) => SyncWidths(ExpenseGrid, ExpenseTotalsGrid);
        IncomeGrid .LayoutUpdated += (_, __) =>
        {
            SyncWidths(IncomeGrid, IncomeTotalsGrid);
            // Surplus has no DataGrid above it; mirror the Income grid so it
            // aligns with the same set of columns.
            SyncWidths(IncomeGrid, SurplusGrid);
        };
    }

    private static void SyncWidths(CommunityToolkit.WinUI.UI.Controls.DataGrid src, Grid totals)
    {
        if (src.Columns.Count == 0 || totals.ColumnDefinitions.Count < src.Columns.Count + 1) return;
        double used = 0;
        for (int i = 0; i < src.Columns.Count; i++)
        {
            var w = src.Columns[i].ActualWidth;
            if (w <= 0) return; // not laid out yet
            totals.ColumnDefinitions[i].Width = new GridLength(w);
            used += w;
        }
        // Spacer column = whatever the DataGrid lost to its vertical
        // scrollbar / row-header gutter, so the right edge of the last
        // value column aligns with the right edge of the last data column.
        var spacer = Math.Max(0, src.ActualWidth - used);
        totals.ColumnDefinitions[^1].Width = new GridLength(spacer);
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        try { await ReloadAsync(); }
        catch (VaultLockedException) { _nav.NavigateToLogin(); }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async Task ReloadAsync()
    {
        var rows = await _repo.GetAllAsync();
        _expenseAll.Clear();
        _incomeAll.Clear();
        foreach (var r in rows)
        {
            var vm = new ItemVm(r);
            (r.Kind == RetirementKind.Expense ? _expenseAll : _incomeAll).Add(vm);
        }
        ApplySortAndBind();
        UpdateTotals();
    }

    private void ApplySortAndBind()
    {
        Bind(_expenses, _expenseAll, _expenseSortKey, _expenseSortDir);
        Bind(_income,   _incomeAll,  _incomeSortKey,  _incomeSortDir);
    }

    private static void Bind(ObservableCollection<ItemVm> target, IEnumerable<ItemVm> source, string? key, DataGridSortDirection? dir)
    {
        target.Clear();
        IEnumerable<ItemVm> rows = source;
        if (key is not null && dir is not null)
        {
            var asc = dir == DataGridSortDirection.Ascending;
            Func<ItemVm, IComparable> sel = key switch
            {
                "Annual" => v => v.Annual,
                "Weekly" => v => v.Weekly,
                _        => v => v.Label ?? string.Empty,
            };
            rows = asc ? source.OrderBy(sel) : source.OrderByDescending(sel);
        }
        foreach (var r in rows) target.Add(r);
    }

    private void UpdateTotals()
    {
        var expA = _expenseAll.Sum(v => v.Annual);
        var expW = _expenseAll.Sum(v => v.Weekly);
        var incA = _incomeAll.Sum(v => v.Annual);
        var incW = _incomeAll.Sum(v => v.Weekly);

        ExpenseAnnualTotal.Text = Money(expA);
        ExpenseWeeklyTotal.Text = Money(expW);
        IncomeAnnualTotal.Text  = Money(incA);
        IncomeWeeklyTotal.Text  = Money(incW);

        var surplusA = incA - expA;
        var surplusW = incW - expW;
        SurplusAnnual.Text = Money(surplusA);
        SurplusWeekly.Text = Money(surplusW);
        // Red-tint the surplus row when negative so a deficit is obvious.
        var critical = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
        var success  = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
        var brush = surplusA < 0 ? critical : success;
        SurplusAnnual.Foreground = brush;
        SurplusWeekly.Foreground = brush;
    }

    private static string Money(double v) => v.ToString("C", CultureInfo.GetCultureInfo("en-AU"));

    // ---- Sort handlers ----------------------------------------------------
    private void ExpenseGrid_Sorting(object? sender, DataGridColumnEventArgs e)
        => HandleSort(e, ExpenseGrid, ref _expenseSortKey, ref _expenseSortDir, () => Bind(_expenses, _expenseAll, _expenseSortKey, _expenseSortDir));

    private void IncomeGrid_Sorting(object? sender, DataGridColumnEventArgs e)
        => HandleSort(e, IncomeGrid, ref _incomeSortKey, ref _incomeSortDir, () => Bind(_income, _incomeAll, _incomeSortKey, _incomeSortDir));

    private static void HandleSort(DataGridColumnEventArgs e, DataGrid grid, ref string? key, ref DataGridSortDirection? dir, Action rebind)
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
    private void ExpenseGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var has = ExpenseGrid.SelectedItem is ItemVm;
        EditExpenseBtn.IsEnabled = has;
        DeleteExpenseBtn.IsEnabled = has;
    }
    private void IncomeGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var has = IncomeGrid.SelectedItem is ItemVm;
        EditIncomeBtn.IsEnabled = has;
        DeleteIncomeBtn.IsEnabled = has;
    }

    // ---- Add / Edit / Delete ---------------------------------------------
    private async void AddExpense_Click(object _, RoutedEventArgs __) => await AddOrEditAsync(RetirementKind.Expense, null);
    private async void AddIncome_Click (object _, RoutedEventArgs __) => await AddOrEditAsync(RetirementKind.Income,  null);

    private async void EditExpense_Click(object _, RoutedEventArgs __)
    {
        if (ExpenseGrid.SelectedItem is ItemVm vm) await AddOrEditAsync(RetirementKind.Expense, vm);
    }
    private async void EditIncome_Click(object _, RoutedEventArgs __)
    {
        if (IncomeGrid.SelectedItem is ItemVm vm) await AddOrEditAsync(RetirementKind.Income, vm);
    }

    private async void ExpenseGrid_DoubleTapped(object _, DoubleTappedRoutedEventArgs __)
    {
        if (ExpenseGrid.SelectedItem is ItemVm vm) await AddOrEditAsync(RetirementKind.Expense, vm);
    }
    private async void IncomeGrid_DoubleTapped(object _, DoubleTappedRoutedEventArgs __)
    {
        if (IncomeGrid.SelectedItem is ItemVm vm) await AddOrEditAsync(RetirementKind.Income, vm);
    }

    private async Task AddOrEditAsync(RetirementKind kind, ItemVm? existing)
    {
        if (_busy) return; _busy = true;
        try
        {
            var dlg = new RetirementIncExpDialog(this.XamlRoot, kind, existing?.Source);
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

            var item = dlg.Result!;
            if (existing is null) await _repo.InsertAsync(item);
            else                  await _repo.UpdateAsync(item);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            // The dialog mutates the underlying RetirementItem in place, so a
            // failed UpdateAsync would leave a stale in-memory row diverging
            // from the database. Reload to restore authoritative state.
            try { await ReloadAsync(); } catch { /* best-effort */ }
        }
        finally { _busy = false; }
    }

    private async void DeleteExpense_Click(object _, RoutedEventArgs __)
    {
        if (ExpenseGrid.SelectedItem is ItemVm vm) await DeleteAsync(vm);
    }
    private async void DeleteIncome_Click(object _, RoutedEventArgs __)
    {
        if (IncomeGrid.SelectedItem is ItemVm vm) await DeleteAsync(vm);
    }

    private async Task DeleteAsync(ItemVm vm)
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
                    Text = "This permanently removes the row from the retirement income/expense list.",
                    TextWrapping = TextWrapping.Wrap,
                },
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
            await _repo.DeleteAsync(vm.Id);
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
/// Display model for a single retirement income or expense row. Holds the
/// underlying <see cref="RetirementItem"/> so edits can round-trip without
/// losing fields the page doesn't show (inflation, ages, notes).
/// </summary>
internal sealed class ItemVm
{
    public RetirementItem Source { get; }
    public long Id => Source.Id;
    public string Label => Source.Label;
    public double Annual => Source.MonthlyAmountJan2025 * 12.0;
    // 52 weeks/year matches the user's reference figures (e.g. $43,680 / $840).
    public double Weekly => Annual / 52.0;
    public string AnnualDisplay => Annual.ToString("C", CultureInfo.GetCultureInfo("en-AU"));
    public string WeeklyDisplay => Weekly.ToString("C", CultureInfo.GetCultureInfo("en-AU"));
    public ItemVm(RetirementItem r) { Source = r; }
}
