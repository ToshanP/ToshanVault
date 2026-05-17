using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.WinUI.UI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ToshanVault.Core.Models;
using ToshanVault.Data.Repositories;
using ToshanVault_App.Hosting;
using ToshanVault_App.Services;

namespace ToshanVault_App.Pages;

public sealed partial class MintInvestmentPage : Page
{
    private static readonly CultureInfo Aud = CultureInfo.GetCultureInfo("en-AU");
    private readonly MintInvestmentRepository _repo = AppHost.GetService<MintInvestmentRepository>();
    private readonly GoldPriceService _price = AppHost.GetService<GoldPriceService>();
    private IReadOnlyList<MintInvestmentPurchase> _purchases = Array.Empty<MintInvestmentPurchase>();
    private readonly ObservableCollection<YearlyBalanceVm> _yearlyRows = new();
    private MintInvestmentPlan _plan = new();
    private Dictionary<DateOnly, MintFortnightActual> _fortnightActuals = new();
    private readonly List<FortnightVm> _expandedFortnightVms = new();
    private YearlyBalanceVm? _expandedYear;

    public MintInvestmentPage()
    {
        InitializeComponent();
        YearlyBalanceGrid.ItemsSource = _yearlyRows;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _plan = await _repo.GetPlanAsync();
        _purchases = await _repo.GetPurchasesAsync();
        Populate(_plan);
        Render(_plan);
        await LoadYearlyBalanceAsync(_plan);
    }

    private void Populate(MintInvestmentPlan plan)
    {
        EnabledSwitch.IsOn = plan.Enabled;
        AccountStartDateBox.Date = new DateTimeOffset(plan.AccountStartDate.ToDateTime(TimeOnly.MinValue));
        FortnightlyContributionBox.Value = plan.FortnightlyContributionAud;
        WorkingUnitOuncesBox.Value = plan.WorkingUnitOunces;
        PricePerOunceBox.Value = plan.PricePerOunceAud;
        ReminderLeadDaysBox.Value = plan.ReminderLeadDays;
        ConsolidationTargetBox.Value = plan.ConsolidationTargetOunces;
        NotesBox.Text = plan.Notes ?? string.Empty;
    }

    private MintInvestmentPlan ReadInputs() => new()
    {
        Enabled = EnabledSwitch.IsOn,
        AccountStartDate = AccountStartDateBox.Date is { } start
            ? DateOnly.FromDateTime(start.LocalDateTime.Date)
            : DateOnly.FromDateTime(DateTime.Today),
        FortnightlyContributionAud = NumOrZero(FortnightlyContributionBox.Value),
        WorkingUnitOunces = NumOrZero(WorkingUnitOuncesBox.Value),
        PricePerOunceAud = NumOrZero(PricePerOunceBox.Value),
        ReminderLeadDays = (int)Math.Max(0, NumOrZero(ReminderLeadDaysBox.Value)),
        ConsolidationTargetOunces = NumOrZero(ConsolidationTargetBox.Value) <= 0
            ? 10
            : NumOrZero(ConsolidationTargetBox.Value),
        Notes = string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim(),
    };

    private static double NumOrZero(double value) => double.IsNaN(value) ? 0 : value;

    private void Render(MintInvestmentPlan plan)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var summary = MintInvestmentCalculator.Summarise(plan, _purchases, today);
        CashText.Text = summary.MintAccountCash.ToString("C0", Aud);
        OuncesText.Text = $"{summary.PhysicalOunces:N1} oz";
        ValueText.Text = summary.PhysicalValue.ToString("C0", Aud);
        ConsolidationText.Text = summary.ConsolidationBars >= 1
            ? $"{summary.ConsolidationBars:N0} x {plan.ConsolidationTargetOunces:N0} oz target(s) + {summary.LiquidOunces:N1} oz liquid"
            : $"{Math.Max(0, plan.ConsolidationTargetOunces - summary.PhysicalOunces):N1} oz to first {plan.ConsolidationTargetOunces:N0} oz consolidation target";

        ScheduleHintText.Text =
            $"{plan.FortnightlyContributionAud.ToString("C0", Aud)}/fortnight starts {plan.AccountStartDate:dd MMM yyyy}. " +
            $"The next due date is the first funding date where available Mint cash covers {plan.WorkingUnitOunces:N1} oz.";

        ScheduleList.Items.Clear();
        foreach (var row in MintInvestmentCalculator.GenerateSchedule(plan, _purchases, today, 10))
        {
            ScheduleList.Items.Add(BuildScheduleRow(row));
        }
    }

    // ---- Yearly Balance Tab ------------------------------------------------

    private async Task LoadYearlyBalanceAsync(MintInvestmentPlan plan)
    {
        var yearEnds = GenerateFinancialYearEnds(plan.AccountStartDate, 10);
        var projections = MintInvestmentCalculator.ProjectYearValues(plan, _purchases, yearEnds);
        var actuals = await _repo.GetYearlyBalancesAsync();
        var actualsByYear = actuals.ToDictionary(a => a.YearEnd);

        // Seed targets into DB for years that don't have records yet
        var toSeed = new List<MintYearlyBalance>();
        foreach (var proj in projections)
        {
            if (!actualsByYear.ContainsKey(proj.YearEnd))
            {
                var seed = new MintYearlyBalance
                {
                    YearEnd = proj.YearEnd,
                    ActualOz = proj.PhysicalOunces,
                    ActualInvested = proj.TotalContributed,
                };
                toSeed.Add(seed);
                actualsByYear[proj.YearEnd] = seed;
            }
        }
        if (toSeed.Count > 0)
            await _repo.BulkUpsertYearlyBalancesAsync(toSeed);

        // Load fortnight actuals
        var fnActuals = await _repo.GetFortnightActualsAsync();
        _fortnightActuals = fnActuals.ToDictionary(a => a.FortnightDate);

        _yearlyRows.Clear();
        foreach (var proj in projections)
        {
            actualsByYear.TryGetValue(proj.YearEnd, out var actual);
            _yearlyRows.Add(new YearlyBalanceVm(proj, actual, plan.PricePerOunceAud));
        }
    }

    private static IReadOnlyList<DateOnly> GenerateFinancialYearEnds(DateOnly startDate, int count)
    {
        // Australian FY ends 30 June. Find the first 30 June after the start date.
        var firstYearEnd = new DateOnly(startDate.Year, 6, 30);
        if (firstYearEnd < startDate) firstYearEnd = firstYearEnd.AddYears(1);

        var ends = new List<DateOnly>(count);
        for (var i = 0; i < count; i++)
            ends.Add(firstYearEnd.AddYears(i));
        return ends;
    }

    private async void YearlyBalanceGrid_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        if (YearlyBalanceGrid.SelectedItem is YearlyBalanceVm vm) await EditYearlyAsync(vm);
    }

    private void YearlyBalanceGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var hasSelection = YearlyBalanceGrid.SelectedItem is YearlyBalanceVm;
        EditYearlyBtn.IsEnabled = hasSelection;
        ExpandYearlyBtn.IsEnabled = hasSelection;

        // Auto-refresh the detail panel when a different row is selected while expanded
        if (FortnightDetailBorder.Visibility == Visibility.Visible
            && YearlyBalanceGrid.SelectedItem is YearlyBalanceVm selected
            && selected != _expandedYear)
        {
            ExpandYearly_Click(sender, new RoutedEventArgs());
        }
    }

    private async void EditYearly_Click(object sender, RoutedEventArgs e)
    {
        if (YearlyBalanceGrid.SelectedItem is YearlyBalanceVm vm) await EditYearlyAsync(vm);
    }

    private async Task EditYearlyAsync(YearlyBalanceVm vm)
    {
        try
        {
            var dlg = new MintYearlyBalanceDialog(this.XamlRoot, vm);
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

            await _repo.UpsertYearlyBalanceAsync(new MintYearlyBalance
            {
                YearEnd = vm.YearEnd,
                ActualOz = dlg.ResultActualOz,
                ActualInvested = dlg.ResultActualInvested,
            });

            vm.ActualOz = dlg.ResultActualOz;
            vm.ActualInvested = dlg.ResultActualInvested;
            vm.RefreshComputed();
            ShowInfo($"Saved {vm.YearLabel}.");
        }
        catch (Exception ex)
        {
            ShowInfo($"Save failed: {ex.Message}", error: true);
        }
    }

    // ---- Fortnight drill-down ------------------------------------------------

    private void ExpandYearly_Click(object sender, RoutedEventArgs e)
    {
        if (YearlyBalanceGrid.SelectedItem is not YearlyBalanceVm vm) return;

        // Toggle collapse if same year
        if (_expandedYear == vm && FortnightDetailBorder.Visibility == Visibility.Visible)
        {
            FortnightDetailBorder.Visibility = Visibility.Collapsed;
            _expandedYear = null;
            ExpandYearlyBtn.Content = "▶ Expand";
            return;
        }

        _expandedYear = vm;
        ExpandYearlyBtn.Content = "▼ Collapse";
        FortnightDetailTitle.Text = $"Fortnightly Detail — {vm.YearLabel}";

        var fyStart = vm.YearEnd.AddYears(-1); // 30 June previous year
        var snapshots = MintInvestmentCalculator.GenerateFortnightDetails(
            _plan, _fortnightActuals, fyStart, vm.YearEnd);

        _expandedFortnightVms.Clear();
        FortnightList.Items.Clear();
        foreach (var snap in snapshots)
        {
            var fnVm = new FortnightVm(snap);
            _expandedFortnightVms.Add(fnVm);
            FortnightList.Items.Add(BuildFortnightRow(fnVm));
        }

        EditFortnightBtn.IsEnabled = false;
        FortnightDetailBorder.Visibility = Visibility.Visible;
    }

    private void CloseFortnightDetail_Click(object sender, RoutedEventArgs e)
    {
        FortnightDetailBorder.Visibility = Visibility.Collapsed;
        _expandedYear = null;
        ExpandYearlyBtn.Content = "▶ Expand";
    }

    private void FortnightList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        EditFortnightBtn.IsEnabled = FortnightList.SelectedIndex >= 0;
    }

    private async void FortnightList_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        if (FortnightList.SelectedIndex >= 0)
            await EditFortnightAsync(FortnightList.SelectedIndex);
    }

    private async void EditFortnight_Click(object sender, RoutedEventArgs e)
    {
        if (FortnightList.SelectedIndex >= 0)
            await EditFortnightAsync(FortnightList.SelectedIndex);
    }

    private async Task EditFortnightAsync(int index)
    {
        if (index < 0 || index >= _expandedFortnightVms.Count || _expandedYear == null) return;

        var vm = _expandedFortnightVms[index];
        try
        {
            var dlg = new MintFortnightEditDialog(this.XamlRoot, vm);
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

            // Persist edited fortnight
            await _repo.UpsertFortnightActualAsync(new MintFortnightActual
            {
                FortnightDate = vm.Date,
                ActualOz = dlg.ResultPurchaseOz,
                ActualContribution = dlg.ResultContribution,
            });

            // Update local cache
            _fortnightActuals[vm.Date] = new MintFortnightActual
            {
                FortnightDate = vm.Date,
                ActualOz = dlg.ResultPurchaseOz,
                ActualContribution = dlg.ResultContribution,
            };

            // Forward propagation: recalculate from edited index onward
            var price = _plan.PricePerOunceAud;
            double prevCash = index > 0 ? _expandedFortnightVms[index - 1].CashBalance : 0;
            double prevOz = index > 0 ? _expandedFortnightVms[index - 1].RunningOz : 0;

            // First fortnight needs cash from before the FY — get from snapshot generation
            if (index == 0)
            {
                // Cash at start of FY = cash up to fyStart from the generator.
                // Simplification: use the snapshot values minus this row's contribution.
                prevCash = 0;
                prevOz = 0;
                // Re-run full snapshot to get correct opening balances
                var fyStart = _expandedYear.YearEnd.AddYears(-1);
                var freshSnaps = MintInvestmentCalculator.GenerateFortnightDetails(
                    _plan, _fortnightActuals, fyStart, _expandedYear.YearEnd);

                // Refresh all VMs from fresh snapshots
                for (var i = 0; i < _expandedFortnightVms.Count && i < freshSnaps.Count; i++)
                {
                    var s = freshSnaps[i];
                    var fv = _expandedFortnightVms[i];
                    fv.Contribution = s.Contribution;
                    fv.PurchaseOz = s.PurchaseOz;
                    fv.CashBalance = s.CashBalance;
                    fv.RunningOz = s.RunningOz;
                    fv.RunningValue = s.RunningValue;
                }
            }
            else
            {
                // Propagate from edited index onward
                for (var i = index; i < _expandedFortnightVms.Count; i++)
                {
                    var fv = _expandedFortnightVms[i];
                    double contrib, purchOz;

                    if (i == index)
                    {
                        contrib = dlg.ResultContribution;
                        purchOz = dlg.ResultPurchaseOz;
                    }
                    else if (_fortnightActuals.TryGetValue(fv.Date, out var act))
                    {
                        contrib = act.ActualContribution;
                        purchOz = act.ActualOz;
                    }
                    else
                    {
                        contrib = _plan.FortnightlyContributionAud;
                        var unitOz = _plan.WorkingUnitOunces;
                        var unitCost = unitOz * price;
                        purchOz = (unitCost > 0 && prevCash + contrib + 0.0001 >= unitCost) ? unitOz : 0;
                    }

                    var cash = prevCash + contrib - (purchOz * price);
                    var oz = prevOz + purchOz;

                    fv.Contribution = contrib;
                    fv.PurchaseOz = purchOz;
                    fv.CashBalance = cash;
                    fv.RunningOz = oz;
                    fv.RunningValue = oz * price;

                    prevCash = cash;
                    prevOz = oz;
                }
            }

            // Rebuild ListView items
            FortnightList.Items.Clear();
            foreach (var fv in _expandedFortnightVms)
                FortnightList.Items.Add(BuildFortnightRow(fv));

            // Update parent annual row
            if (_expandedFortnightVms.Count > 0)
            {
                var lastFn = _expandedFortnightVms[^1];
                _expandedYear.ActualOz = lastFn.RunningOz;
                _expandedYear.ActualInvested = _expandedFortnightVms.Sum(f => f.PurchaseOz * price);
                _expandedYear.RefreshComputed();

                await _repo.UpsertYearlyBalanceAsync(new MintYearlyBalance
                {
                    YearEnd = _expandedYear.YearEnd,
                    ActualOz = _expandedYear.ActualOz,
                    ActualInvested = _expandedYear.ActualInvested,
                });
            }

            ShowInfo($"Saved fortnight {vm.DateDisplay} and propagated forward.");
        }
        catch (Exception ex)
        {
            ShowInfo($"Save failed: {ex.Message}", error: true);
        }
    }

    private static FrameworkElement BuildFortnightRow(FortnightVm vm)
    {
        var grid = new Grid { Padding = new Thickness(4, 2, 4, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var date = new TextBlock { Text = vm.DateDisplay };
        var contrib = new TextBlock { Text = vm.ContributionDisplay, TextAlignment = TextAlignment.Right, HorizontalAlignment = HorizontalAlignment.Stretch };
        var cash = new TextBlock { Text = vm.CashBalanceDisplay, TextAlignment = TextAlignment.Right, HorizontalAlignment = HorizontalAlignment.Stretch };
        var purch = new TextBlock { Text = vm.PurchaseOzDisplay, TextAlignment = TextAlignment.Right, HorizontalAlignment = HorizontalAlignment.Stretch };
        var runOz = new TextBlock { Text = vm.RunningOzDisplay, TextAlignment = TextAlignment.Right, HorizontalAlignment = HorizontalAlignment.Stretch };
        var runVal = new TextBlock { Text = vm.RunningValueDisplay, TextAlignment = TextAlignment.Right, HorizontalAlignment = HorizontalAlignment.Stretch };

        Grid.SetColumn(date, 0);
        Grid.SetColumn(contrib, 1);
        Grid.SetColumn(cash, 2);
        Grid.SetColumn(purch, 3);
        Grid.SetColumn(runOz, 4);
        Grid.SetColumn(runVal, 5);

        grid.Children.Add(date);
        grid.Children.Add(contrib);
        grid.Children.Add(cash);
        grid.Children.Add(purch);
        grid.Children.Add(runOz);
        grid.Children.Add(runVal);

        return grid;
    }

    // ---- Schedule Tab helpers -----------------------------------------------

    private FrameworkElement BuildScheduleRow(MintInvestmentCalculator.ScheduleRow row)
    {
        var grid = new Grid { Padding = new Thickness(8), ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(105) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        AddCell(grid, row.DueDate.ToString("dd MMM yyyy", Aud), 0, bold: true);
        AddCell(grid, $"{row.Ounces:N1} oz", 1);
        AddCell(grid, row.EstimatedCost.ToString("C0", Aud), 2);
        AddCell(grid, row.CashAfterPurchase.ToString("C0", Aud), 3);
        AddCell(grid, StatusText(row), 4);

        var button = new Button
        {
            Content = row.CompletedDate is null ? "Tick purchased" : "Clear tick",
            Tag = row,
            MinWidth = 118,
        };
        button.Click += row.CompletedDate is null ? TickPurchased_Click : ClearTick_Click;
        Grid.SetColumn(button, 5);
        grid.Children.Add(button);
        return grid;
    }

    private static void AddCell(Grid grid, string text, int column, bool bold = false)
    {
        var tb = new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            FontWeight = bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
        };
        Grid.SetColumn(tb, column);
        grid.Children.Add(tb);
    }

    private static string StatusText(MintInvestmentCalculator.ScheduleRow row)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var status = row.CompletedDate is { } completed
            ? $"Purchased {completed:dd MMM yyyy}"
            : row.DueDate < today
                ? $"{today.DayNumber - row.DueDate.DayNumber} day(s) overdue"
                : row.DueDate == today
                    ? "Due today"
                    : $"Due in {row.DueDate.DayNumber - today.DayNumber} day(s)";
        return row.IsConsolidationCheckpoint
            ? $"{status} · consolidation checkpoint"
            : status;
    }

    // ---- Save / Buttons ----------------------------------------------------

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var plan = ReadInputs();
            await _repo.UpsertPlanAsync(plan);
            await LoadAsync();
            ShowInfo("Mint Investment plan saved.");
        }
        catch (Exception ex)
        {
            ShowInfo($"Save failed: {ex.Message}", error: true);
        }
    }

    private async void UseCurrentPrice_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var cache = await _price.GetAsync(forceRefresh: false);
            if (cache is null || cache.PricePerGram24k <= 0)
            {
                cache = await _price.GetAsync(forceRefresh: true);
            }
            if (cache is null || cache.PricePerGram24k <= 0)
            {
                ShowInfo("Gold price is not available yet.", error: true);
                return;
            }

            PricePerOunceBox.Value = cache.PricePerGram24k * GoldValueCalculator.GramsPerTroyOunce;
            Render(ReadInputs());
            ShowInfo("Updated 1 oz price from cached/live gold price.");
        }
        catch (Exception ex)
        {
            ShowInfo($"Price update failed: {ex.Message}", error: true);
        }
    }

    private async void TickPurchased_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MintInvestmentCalculator.ScheduleRow row }) return;
        try
        {
            await _repo.UpsertCompletedPurchaseAsync(new MintInvestmentPurchase
            {
                DueDate = row.DueDate,
                CompletedDate = DateOnly.FromDateTime(DateTime.Today),
                Ounces = row.Ounces,
                PricePerOunceAud = row.PricePerOunceAud,
            });
            await LoadAsync();
            ShowInfo($"Recorded {row.Ounces:N1} oz purchase for {row.DueDate:dd MMM yyyy}.");
        }
        catch (Exception ex)
        {
            ShowInfo($"Could not record purchase: {ex.Message}", error: true);
        }
    }

    private async void ClearTick_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MintInvestmentCalculator.ScheduleRow row }) return;
        try
        {
            await _repo.DeletePurchaseAsync(row.DueDate);
            await LoadAsync();
            ShowInfo($"Cleared purchase for {row.DueDate:dd MMM yyyy}.");
        }
        catch (Exception ex)
        {
            ShowInfo($"Could not clear purchase: {ex.Message}", error: true);
        }
    }

    private void ShowInfo(string message, bool error = false)
    {
        InfoBar.Severity = error ? InfoBarSeverity.Error : InfoBarSeverity.Success;
        InfoBar.Message = message;
        InfoBar.IsOpen = true;
    }
}

// ---- ViewModel for Yearly Balance grid row ---------------------------------

internal sealed class YearlyBalanceVm : INotifyPropertyChanged
{
    private static readonly CultureInfo Aud = CultureInfo.GetCultureInfo("en-AU");
    private readonly double _pricePerOz;
    private double _actualOz;
    private double _actualInvested;

    public YearlyBalanceVm(
        MintInvestmentCalculator.YearProjection projection,
        MintYearlyBalance? actual,
        double pricePerOz)
    {
        YearEnd = projection.YearEnd;
        YearLabel = $"FY {projection.YearEnd.Year - 1}-{projection.YearEnd.Year:D2}".Replace($"-{projection.YearEnd.Year:D4}", $"-{projection.YearEnd.Year % 100:D2}");
        TargetOz = projection.PhysicalOunces;
        TargetValue = projection.PhysicalValue;
        TargetInvested = projection.TotalContributed;
        _pricePerOz = pricePerOz;
        _actualOz = actual?.ActualOz ?? 0;
        _actualInvested = actual?.ActualInvested ?? 0;
    }

    public DateOnly YearEnd { get; }
    public string YearLabel { get; }
    public double TargetOz { get; }
    public double TargetValue { get; }
    public double TargetInvested { get; }

    public string TargetOzDisplay => $"{TargetOz:N1}";
    public string TargetValueDisplay => TargetValue.ToString("C0", Aud);
    public string TargetInvestedDisplay => TargetInvested.ToString("C0", Aud);

    public double ActualOz
    {
        get => _actualOz;
        set { _actualOz = value; OnPropertyChanged(nameof(ActualOz)); OnPropertyChanged(nameof(ActualOzDisplay)); RefreshComputed(); }
    }

    public double ActualInvested
    {
        get => _actualInvested;
        set { _actualInvested = value; OnPropertyChanged(nameof(ActualInvested)); OnPropertyChanged(nameof(ActualInvestedDisplay)); }
    }

    public string ActualOzDisplay => $"{_actualOz:N1}";
    public string ActualInvestedDisplay => _actualInvested.ToString("C0", Aud);
    public string ActualValueDisplay => (_actualOz * _pricePerOz).ToString("C0", Aud);

    public void RefreshComputed()
    {
        OnPropertyChanged(nameof(ActualValueDisplay));
        OnPropertyChanged(nameof(ActualOzDisplay));
        OnPropertyChanged(nameof(ActualInvestedDisplay));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

// ---- ViewModel for Fortnightly drill-down row --------------------------------

internal sealed class FortnightVm
{
    private static readonly CultureInfo Aud = CultureInfo.GetCultureInfo("en-AU");

    public FortnightVm(MintInvestmentCalculator.FortnightSnapshot snap)
    {
        Date = snap.Date;
        Contribution = snap.Contribution;
        CashBalance = snap.CashBalance;
        PurchaseOz = snap.PurchaseOz;
        RunningOz = snap.RunningOz;
        RunningValue = snap.RunningValue;
    }

    public DateOnly Date { get; }
    public double Contribution { get; set; }
    public double CashBalance { get; set; }
    public double PurchaseOz { get; set; }
    public double RunningOz { get; set; }
    public double RunningValue { get; set; }

    public string DateDisplay => Date.ToString("dd MMM yyyy");
    public string ContributionDisplay => Contribution.ToString("C0", Aud);
    public string CashBalanceDisplay => CashBalance.ToString("C0", Aud);
    public string PurchaseOzDisplay => PurchaseOz > 0 ? $"{PurchaseOz:N1}" : "—";
    public string RunningOzDisplay => $"{RunningOz:N1}";
    public string RunningValueDisplay => RunningValue.ToString("C0", Aud);
}
