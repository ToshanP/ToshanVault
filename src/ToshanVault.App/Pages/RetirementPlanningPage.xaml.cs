using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using ToshanVault.Core.Models;
using ToshanVault.Data.Repositories;
using ToshanVault_App.Hosting;

namespace ToshanVault_App.Pages;

public sealed partial class RetirementPlanningPage : Page
{
    private static readonly CultureInfo Aud = CultureInfo.GetCultureInfo("en-AU");
    private readonly RetirementPlanRepository _repo;
    private readonly ObservableCollection<YearRow> _yearRows = new();
    private readonly ObservableCollection<GoldRow> _goldRows = new();
    private readonly ObservableCollection<CombinedRow> _combinedRows = new();

    public RetirementPlanningPage()
    {
        InitializeComponent();
        _repo = AppHost.GetService<RetirementPlanRepository>();
        YearGrid.ItemsSource = _yearRows;
        GoldGrid.ItemsSource = _goldRows;
        CombinedGrid.ItemsSource = _combinedRows;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var p = await _repo.GetAsync();
        LoanNameBox.Text     = p.LoanName;
        PrincipalBox.Value   = p.Principal;
        RateBox.Value        = p.AnnualRatePct;
        TermBox.Value        = p.TermYears;
        MinimumPaymentBox.Value = p.MinimumPaymentPerPeriod;
        ExtraBox.Value       = p.ExtraPerPeriod;
        NotesBox.Text        = p.Notes ?? string.Empty;
        StartDateBox.Date    = ToDto(p.StartDate);
        FrequencyBox.SelectedIndex = p.Frequency switch
        {
            RepaymentFrequency.Weekly      => 0,
            RepaymentFrequency.Monthly     => 2,
            _                              => 1,
        };
        GoldPerPeriodBox.Value  = p.GoldPerPeriod;
        GoldGrowthBox.Value     = p.GoldGrowthPct;
        GoldStartDateBox.Date   = ToDto(p.GoldStartDate);
        Recalculate();
    }

    private static DateTimeOffset ToDto(DateOnly d) =>
        new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue));

    private RepaymentFrequency CurrentFrequency() => FrequencyBox.SelectedIndex switch
    {
        0 => RepaymentFrequency.Weekly,
        2 => RepaymentFrequency.Monthly,
        _ => RepaymentFrequency.Fortnightly,
    };

    private static string FreqAdjective(RepaymentFrequency f) => f switch
    {
        RepaymentFrequency.Weekly      => "weekly",
        RepaymentFrequency.Fortnightly => "fortnightly",
        RepaymentFrequency.Monthly     => "monthly",
        _ => "",
    };

    private RetirementPlan ReadInputs() => new RetirementPlan
    {
        LoanName       = string.IsNullOrWhiteSpace(LoanNameBox.Text) ? "Loan" : LoanNameBox.Text.Trim(),
        Principal      = NumOrZero(PrincipalBox.Value),
        AnnualRatePct  = NumOrZero(RateBox.Value),
        TermYears      = (int)Math.Max(1, NumOrZero(TermBox.Value)),
        Frequency      = CurrentFrequency(),
        MinimumPaymentPerPeriod = NumOrZero(MinimumPaymentBox.Value),
        ExtraPerPeriod = NumOrZero(ExtraBox.Value),
        StartDate      = StartDateBox.Date is { } d
            ? DateOnly.FromDateTime(d.LocalDateTime.Date)
            : DateOnly.FromDateTime(DateTime.Today),
        GoldPerPeriod  = NumOrZero(GoldPerPeriodBox.Value),
        GoldGrowthPct  = NumOrZero(GoldGrowthBox.Value),
        GoldStartDate  = GoldStartDateBox.Date is { } gd
            ? DateOnly.FromDateTime(gd.LocalDateTime.Date)
            : new DateOnly(2028, 5, 1),
        Notes          = string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim(),
    };

    private static double NumOrZero(double v) => double.IsNaN(v) ? 0 : v;

    private void Recalculate_Click(object sender, RoutedEventArgs e) => Recalculate();

    private void Recalculate()
    {
        _yearRows.Clear();
        _goldRows.Clear();
        _combinedRows.Clear();
        KeyDatesPanel.Children.Clear();
        CombinedKeyDatesPanel.Children.Clear();
        TimelineGrid.Children.Clear();
        GoldSidebarRows.ItemsSource = null;

        try
        {
            var p = ReadInputs();
            if (p.Principal <= 0)
            {
                ShowInfo("Enter a principal greater than zero.", error: true);
                return;
            }

            if (p.MinimumPaymentPerPeriod <= 0)
            {
                ShowInfo("Enter a minimum repayment greater than zero.", error: true);
                return;
            }

            var withExtra    = MortgageCalculator.AmortizeWithMinimumPayment(
                p.Principal, p.AnnualRatePct, p.Frequency, p.MinimumPaymentPerPeriod, p.ExtraPerPeriod, p.StartDate);
            var withoutExtra = MortgageCalculator.AmortizeWithMinimumPayment(
                p.Principal, p.AnnualRatePct, p.Frequency, p.MinimumPaymentPerPeriod, 0, p.StartDate);
            var gold         = GoldAccumulator.Project(p.GoldPerPeriod, p.GoldGrowthPct, p.Frequency,
                                                       p.StartDate, p.GoldStartDate, withExtra.PeriodsToPayoff);

            var saved = withoutExtra.TotalInterest - withExtra.TotalInterest;
            var yearsActual = withExtra.PeriodsToPayoff / (double)MortgageCalculator.PeriodsPerYear(p.Frequency);

            // Summary cards (top tab)
            ScheduledLabel.Text     = withExtra.ScheduledPayment.ToString("C0", Aud);
            ScheduledFreqLabel.Text = $"per {FreqAdjective(p.Frequency)} period";
            ActualLabel.Text        = withExtra.ActualPayment.ToString("C0", Aud);
            ActualSubLabel.Text     = p.ExtraPerPeriod > 0
                ? $"includes {p.ExtraPerPeriod.ToString("C0", Aud)} extra"
                : "minimum only";
            PayoffDateLabel.Text    = withExtra.PayoffDate.ToString("MMM yyyy", Aud);
            PayoffSubLabel.Text     = $"{yearsActual:F1} yr ({withExtra.PeriodsToPayoff} payments)";
            InterestLabel.Text      = withExtra.TotalInterest.ToString("C0", Aud);
            SavingsLabel.Text       = saved > 1 ? $"Saving {saved.ToString("C0", Aud)} vs no extra" : "";
            GoldFinalLabel.Text     = gold.FinalValue.ToString("C0", Aud);
            GoldContribLabel.Text   = $"contributed {gold.TotalContributed.ToString("C0", Aud)}";
            NetLabel.Text           = gold.FinalValue.ToString("C0", Aud);
            YearsLabel.Text         = $"{yearsActual:F1} years";
            YearsSubLabel.Text      = $"debt-free {withExtra.PayoffDate:MMM yyyy}";

            // Tables
            for (var i = 0; i < withExtra.YearSummaries.Count; i++)
            {
                var y = withExtra.YearSummaries[i];
                _yearRows.Add(new YearRow(y));
                var g = i < gold.YearValues.Count ? gold.YearValues[i] : null;
                if (g is not null) _goldRows.Add(new GoldRow(g));
                _combinedRows.Add(new CombinedRow(y, g));
            }

            // Key dates (left panel)
            AddDate(KeyDatesPanel, "Today", DateOnly.FromDateTime(DateTime.Today),
                "Stay the course — consistency is key", "#4a9af5");
            var halfwayPeriod = withExtra.PeriodsToPayoff / 2;
            var halfwayDate = AddPeriods(p.StartDate, Math.Max(1, halfwayPeriod), p.Frequency);
            AddDate(KeyDatesPanel, "Halfway point", halfwayDate,
                $"Loan ~50% paid down (after {halfwayPeriod} payments)", "#f5a623");
            AddDate(KeyDatesPanel, "Gold accumulation begins", p.GoldStartDate,
                $"{p.GoldPerPeriod.ToString("C0", Aud)} per {FreqAdjective(p.Frequency)} @ {p.GoldGrowthPct:F1}% growth", "#f5a623");
            AddDate(KeyDatesPanel, "Loan cleared", withExtra.PayoffDate,
                $"Debt-free! Saved {saved.ToString("C0", Aud)} in interest.", "#4cd964");
            AddDate(KeyDatesPanel, "Gold balance at payoff", withExtra.PayoffDate,
                $"≈ {gold.FinalValue.ToString("C0", Aud)} ({gold.TotalContributed.ToString("C0", Aud)} contributed)", "#af52de");

            // ---- Combined Plan tab ----
            CombinedSubtitle.Text = $"{p.LoanName} · {p.Principal.ToString("C0", Aud)} @ {p.AnnualRatePct:F2}% · {FreqAdjective(p.Frequency)} · started {p.StartDate:MMM yyyy}";
            CombinedGoalLine1.Text = $"Debt-free by\n{withExtra.PayoffDate:MMM yyyy}";
            CombinedGoalLine2.Text = $"{yearsActual:F1} years · {gold.FinalValue.ToString("C0", Aud)} gold buffer";

            SnapLoanName.Text   = p.LoanName;
            SnapPrincipal.Text  = $"Principal: {p.Principal.ToString("C0", Aud)}";
            SnapPayment.Text    = $"{withExtra.ActualPayment.ToString("C0", Aud)} / {FreqAdjective(p.Frequency)[..3]}";
            SnapPaymentSub.Text = p.ExtraPerPeriod > 0
                ? $"+ {p.ExtraPerPeriod.ToString("C0", Aud)} extra"
                : "base scheduled payment";
            SnapGold.Text       = $"{p.GoldPerPeriod.ToString("C0", Aud)} / {FreqAdjective(p.Frequency)[..3]}";
            SnapGoldSub.Text    = $"from {p.GoldStartDate:MMM yyyy} @ {p.GoldGrowthPct:F1}% / yr";
            SnapInterest.Text   = saved > 0 ? saved.ToString("C0", Aud) : "—";
            SnapInterestSub.Text = saved > 0 ? "vs no extra payment" : "no extra payment yet";

            // Timeline (5 dots)
            var phaseEnd1 = AddPeriods(p.StartDate, Math.Min(withExtra.PeriodsToPayoff, MortgageCalculator.PeriodsPerYear(p.Frequency) * 2), p.Frequency);
            BuildTimelineDot(0, "🏠", "Loan start",        $"{p.StartDate:MMM yyyy}",     "Begin payoff",       "#4a9af5", "#1e3f7a");
            BuildTimelineDot(1, "🎯", "Halfway",           $"{halfwayDate:MMM yyyy}",     "~50% paid down",     "#4cd964", "#1a3a1a");
            BuildTimelineDot(2, "🥇", "Gold begins",       $"{p.GoldStartDate:MMM yyyy}", "Build the buffer",   "#f5a623", "#2a1f0e");
            BuildTimelineDot(3, "🏆", "Loan cleared",      $"{withExtra.PayoffDate:MMM yyyy}", "Debt-free!",     "#af52de", "#251e38");
            BuildTimelineDot(4, "🏖", "Onwards",           "Retirement",                  "Live off buffer",    "#f5a623", "#1a3a6e");

            Phase1Period.Text = $"{p.StartDate:MMM yyyy} → {halfwayDate:MMM yyyy}";
            Phase2Period.Text = $"~{halfwayDate:MMM yyyy}";
            Phase3Period.Text = $"From {p.GoldStartDate:MMM yyyy}";
            Phase4Period.Text = $"From {withExtra.PayoffDate:MMM yyyy}";

            // Gold sidebar (sample every ~2 years)
            GoldSidebarSub.Text = $"{p.GoldPerPeriod.ToString("C0", Aud)} per {FreqAdjective(p.Frequency)} from {p.GoldStartDate:MMM yyyy}";
            var sidebar = new System.Collections.Generic.List<SidebarRow>();
            for (var i = 0; i < gold.YearValues.Count; i++)
            {
                if (i == 0 || (i + 1) % 2 == 0 || i == gold.YearValues.Count - 1)
                {
                    var v = gold.YearValues[i];
                    sidebar.Add(new SidebarRow(
                        $"{v.YearEnd:yyyy}",
                        v.EndingValue.ToString("C0", Aud)));
                }
            }
            GoldSidebarRows.ItemsSource = sidebar;

            // Combined key dates panel (right side, copy of left styled the same way)
            AddDate(CombinedKeyDatesPanel, "Today",           DateOnly.FromDateTime(DateTime.Today), "Start", "#4a9af5");
            AddDate(CombinedKeyDatesPanel, "Halfway",         halfwayDate,         "~50% paid down",     "#f5a623");
            AddDate(CombinedKeyDatesPanel, "Gold begins",     p.GoldStartDate,     $"{p.GoldPerPeriod.ToString("C0", Aud)} / {FreqAdjective(p.Frequency)[..3]}", "#f5a623");
            AddDate(CombinedKeyDatesPanel, "Loan cleared",    withExtra.PayoffDate, $"Saved {saved.ToString("C0", Aud)}", "#4cd964");

            ShowInfo(null);
        }
        catch (Exception ex)
        {
            ShowInfo(ex.Message, error: true);
        }
    }

    public sealed record SidebarRow(string YearLabel, string ValueText);

    private void BuildTimelineDot(int column, string icon, string label, string date, string focus, string accentHex, string bgHex)
    {
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch };
        var dot = new Border
        {
            Width = 38, Height = 38, CornerRadius = new CornerRadius(19),
            Background = new SolidColorBrush(ParseColor(bgHex)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new TextBlock { Text = icon, FontSize = 22, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        var labelTb = new TextBlock { Text = label.ToUpperInvariant(), FontWeight = Microsoft.UI.Text.FontWeights.ExtraBold, FontSize = 13, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
        var dateTb  = new TextBlock { Text = date, FontSize = 12, Foreground = new SolidColorBrush(ParseColor("#a8c4f0")), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) };
        var focusTb = new TextBlock { Text = focus.ToUpperInvariant(), FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(ParseColor(accentHex)), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0) };
        sp.Children.Add(dot);
        sp.Children.Add(labelTb);
        sp.Children.Add(dateTb);
        sp.Children.Add(focusTb);
        Grid.SetColumn(sp, column);
        TimelineGrid.Children.Add(sp);
    }

    private static DateOnly AddPeriods(DateOnly start, int periods, RepaymentFrequency f) => f switch
    {
        RepaymentFrequency.Weekly      => start.AddDays(7 * periods),
        RepaymentFrequency.Fortnightly => start.AddDays(14 * periods),
        RepaymentFrequency.Monthly     => start.AddMonths(periods),
        _ => start,
    };

    private void AddDate(StackPanel panel, string title, DateOnly when, string detail, string hexColor)
    {
        var dot = new Border
        {
            Width = 10, Height = 10, CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(ParseColor(hexColor)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap };
        if (ReferenceEquals(panel, CombinedKeyDatesPanel))
        {
            text.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);
            text.FontSize = 14;
        }
        text.Inlines.Add(new Run { Text = $"{when:dd MMM yyyy}  ·  ", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        text.Inlines.Add(new Run { Text = $"{title} — ", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        text.Inlines.Add(new Run { Text = detail });
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        sp.Children.Add(dot);
        sp.Children.Add(text);
        panel.Children.Add(sp);
    }

    private static Windows.UI.Color ParseColor(string hex)
    {
        var h = hex.TrimStart('#');
        return Windows.UI.Color.FromArgb(
            0xFF,
            Convert.ToByte(h.Substring(0, 2), 16),
            Convert.ToByte(h.Substring(2, 2), 16),
            Convert.ToByte(h.Substring(4, 2), 16));
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _repo.UpsertAsync(ReadInputs());
            ShowInfo("Saved.");
        }
        catch (Exception ex)
        {
            ShowInfo($"Save failed: {ex.Message}", error: true);
        }
    }

    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        await _repo.UpsertAsync(new RetirementPlan());
        await LoadAsync();
        ShowInfo("Reset to defaults.");
    }

    private void ShowInfo(string? message, bool error = false)
    {
        if (string.IsNullOrEmpty(message)) { InfoBar.IsOpen = false; return; }
        InfoBar.Severity = error ? InfoBarSeverity.Error : InfoBarSeverity.Success;
        InfoBar.Message  = message;
        InfoBar.IsOpen   = true;
    }

    public sealed class YearRow
    {
        public int    YearNumber          { get; }
        public string YearEndText         { get; }
        public string InterestPaidText    { get; }
        public string PrincipalPaidText   { get; }
        public string EndingBalanceText   { get; }

        public YearRow(MortgageCalculator.YearSummary y)
        {
            YearNumber        = y.YearNumber;
            YearEndText       = y.YearEnd.ToString("MMM yyyy", Aud);
            InterestPaidText  = y.InterestPaid.ToString("C0", Aud);
            PrincipalPaidText = y.PrincipalPaid.ToString("C0", Aud);
            EndingBalanceText = y.EndingBalance.ToString("C0", Aud);
        }
    }

    public sealed class GoldRow
    {
        public int    YearNumber       { get; }
        public string YearEndText      { get; }
        public string ContributedText  { get; }
        public string EndingValueText  { get; }

        public GoldRow(GoldAccumulator.YearValue v)
        {
            YearNumber      = v.YearNumber;
            YearEndText     = v.YearEnd.ToString("MMM yyyy", Aud);
            ContributedText = v.Contributed.ToString("C0", Aud);
            EndingValueText = v.EndingValue.ToString("C0", Aud);
        }
    }

    public sealed class CombinedRow
    {
        public int    YearNumber       { get; }
        public string YearEndText      { get; }
        public string LoanBalanceText  { get; }
        public string GoldValueText    { get; }
        public string NetText          { get; }

        public CombinedRow(MortgageCalculator.YearSummary y, GoldAccumulator.YearValue? g)
        {
            YearNumber      = y.YearNumber;
            YearEndText     = y.YearEnd.ToString("MMM yyyy", Aud);
            LoanBalanceText = y.EndingBalance.ToString("C0", Aud);
            var goldVal = g?.EndingValue ?? 0;
            GoldValueText   = goldVal.ToString("C0", Aud);
            NetText         = (goldVal - y.EndingBalance).ToString("C0", Aud);
        }
    }
}
