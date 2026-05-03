using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.UI;
using ToshanVault.Core.Models;
using ToshanVault.Core.Security;
using ToshanVault.Data.Repositories;
using ToshanVault_App.Hosting;
using ToshanVault_App.Services;

namespace ToshanVault_App.Pages;

/// <summary>
/// Home/landing page that summarises the most useful at-a-glance data from
/// across the app. Each tile is read-only and tapping it routes the user to
/// the relevant feature page via <see cref="NavigationService.NavigateInShell"/>.
/// All loads happen in parallel on <see cref="OnNavigatedTo"/>; failures show
/// a single error line at the bottom rather than blocking the whole page.
/// </summary>
public sealed partial class DashboardPage : Page
{
    private static readonly CultureInfo Aud = CultureInfo.GetCultureInfo("en-AU");

    private readonly NavigationService          _nav      = AppHost.GetService<NavigationService>();
    private readonly InsuranceRepository        _insurance= AppHost.GetService<InsuranceRepository>();
    private readonly BudgetItemRepository       _bItems   = AppHost.GetService<BudgetItemRepository>();
    private readonly BudgetCategoryRepository   _bCats    = AppHost.GetService<BudgetCategoryRepository>();
    private readonly GoldItemRepository         _gold     = AppHost.GetService<GoldItemRepository>();
    private readonly GoldPriceCacheRepository   _goldPx   = AppHost.GetService<GoldPriceCacheRepository>();
    private readonly RetirementPlanRepository   _plan     = AppHost.GetService<RetirementPlanRepository>();
    private readonly VaultEntryRepository       _entries  = AppHost.GetService<VaultEntryRepository>();
    private readonly BankAccountRepository      _banks    = AppHost.GetService<BankAccountRepository>();
    private readonly RecipeRepository           _recipes  = AppHost.GetService<RecipeRepository>();

    public DashboardPage() => InitializeComponent();

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        try
        {
            ErrorText.Visibility = Visibility.Collapsed;
            await Task.WhenAll(
                LoadCashflowAsync(),
                LoadGoldAsync(),
                LoadLoanAsync(),
                LoadInsuranceAsync(),
                LoadCountsAsync(),
                LoadNotesAsync());
            LoadDbInfo();
        }
        catch (VaultLockedException) { _nav.NavigateToLogin(); }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    // ---------------- Weekly cashflow ----------------

    private async Task LoadCashflowAsync()
    {
        var cats  = await _bCats.GetAllAsync();
        var items = await _bItems.GetAllAsync();
        double inc = 0, exp = 0;
        foreach (var it in items)
        {
            var cat = cats.FirstOrDefault(c => c.Id == it.CategoryId);
            if (cat is null) continue;
            var w = ToWeekly(it.Amount, it.Frequency);
            if (cat.Type == BudgetCategoryType.Income) inc += w;
            else exp += w;
        }
        var surplus = inc - exp;
        CashIncomeText.Text  = inc.ToString("C0", Aud);
        CashExpenseText.Text = exp.ToString("C0", Aud);
        CashSurplusText.Text = surplus.ToString("C0", Aud);
        CashSurplusText.Foreground = new SolidColorBrush(surplus >= 0 ? Color.FromArgb(255, 46, 125, 50) : Color.FromArgb(255, 198, 40, 40));
    }

    private static double ToWeekly(double amount, BudgetFrequency f) => f switch
    {
        BudgetFrequency.Weekly      => amount,
        BudgetFrequency.Fortnightly => amount / 2.0,
        BudgetFrequency.Monthly     => amount * 12.0 / 52.0,
        BudgetFrequency.Quarterly   => amount * 4.0 / 52.0,
        BudgetFrequency.Yearly      => amount / 52.0,
        _ => 0,
    };

    // ---------------- Gold holdings ----------------

    private async Task LoadGoldAsync()
    {
        var items = await _gold.GetAllAsync();
        var grams = items.Sum(g => g.GramsCalc * (g.Qty <= 0 ? 1 : g.Qty));
        // GramsCalc is per-piece grams in the existing GoldItem model; multiply
        // by Qty to get total grams. Treat Qty=0 as 1 to keep historical rows
        // (where Qty was never captured) from collapsing to zero.
        GoldGramsText.Text = grams.ToString("N1", Aud) + " g";

        var cache = await _goldPx.GetAsync("AUD");
        if (cache is null || cache.PricePerGram24k <= 0)
        {
            GoldValueText.Text = "Price not cached";
            GoldPriceText.Text = "Open Gold tile to refresh";
        }
        else
        {
            var value = grams * cache.PricePerGram24k;
            GoldValueText.Text = "≈ " + value.ToString("C0", Aud);
            GoldPriceText.Text = $"@ {cache.PricePerGram24k.ToString("C2", Aud)}/g · {Ago(cache.FetchedAt)}";
        }
    }

    // ---------------- Loan payoff progress ----------------

    private async Task LoadLoanAsync()
    {
        var p = await _plan.GetAsync();
        LoanNameText.Text = p.LoanName + " — Loan Payoff";
        var totalDays = Math.Max(1, p.TermYears * 365.25);
        var elapsed   = (DateTime.Today - p.StartDate.ToDateTime(TimeOnly.MinValue)).TotalDays;
        var pct       = Math.Clamp(elapsed / totalDays * 100.0, 0, 100);
        LoanProgressBar.Value = pct;
        LoanProgressText.Text = pct.ToString("F1", Aud) + "% elapsed";
        var payoff = p.StartDate.ToDateTime(TimeOnly.MinValue).AddYears(p.TermYears);
        LoanPayoffText.Text = $"Term {p.TermYears}y · ends {payoff:MMM yyyy}";
    }

    // ---------------- Insurance renewals ----------------

    private async Task LoadInsuranceAsync()
    {
        var all = await _insurance.GetAllAsync();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var upcoming = all
            .Where(i => i.RenewalDate.HasValue)
            .OrderBy(i => i.RenewalDate!.Value.DayNumber < today.DayNumber
                          ? today.DayNumber + 365 + (today.DayNumber - i.RenewalDate!.Value.DayNumber)
                          : i.RenewalDate!.Value.DayNumber)
            .Take(5)
            .ToList();
        // Past-due dates sort to the bottom (treated as if they were a year out)
        // so the user sees genuinely upcoming renewals first; if they want past
        // due they can open the Insurance tile.

        if (upcoming.Count == 0)
        {
            InsuranceEmptyText.Visibility = Visibility.Visible;
            InsuranceRepeater.ItemsSource = Array.Empty<object>();
            return;
        }
        InsuranceEmptyText.Visibility = Visibility.Collapsed;
        InsuranceRepeater.ItemsSource = upcoming.Select(i => BuildInsuranceRow(i, today)).ToList();
    }

    private FrameworkElement BuildInsuranceRow(Insurance ins, DateOnly today)
    {
        var days = ins.RenewalDate!.Value.DayNumber - today.DayNumber;
        Color bg;
        string badge;
        if (days < 0) { bg = Color.FromArgb(40, 198, 40, 40); badge = $"{-days}d overdue"; }
        else if (days <= 14) { bg = Color.FromArgb(40, 198, 40, 40); badge = $"in {days}d"; }
        else if (days <= 30) { bg = Color.FromArgb(40, 245, 124, 0); badge = $"in {days}d"; }
        else { bg = Color.FromArgb(20, 46, 125, 50); badge = $"in {days}d"; }

        var grid = new Grid { Padding = new Thickness(10), CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(bg) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var name = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(ins.PolicyNumber)
                ? ins.InsurerCompany
                : $"{ins.InsurerCompany}  ·  {ins.PolicyNumber}",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        Grid.SetColumn(name, 0);

        var meta = new TextBlock
        {
            Text = (ins.InsuranceType ?? "") + (string.IsNullOrWhiteSpace(ins.Owner) ? "" : "  ·  " + ins.Owner),
            Opacity = 0.7
        };
        Grid.SetColumn(meta, 1);

        var dueWrap = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        dueWrap.Children.Add(new TextBlock { Text = ins.RenewalDate!.Value.ToString("dd MMM yyyy"), VerticalAlignment = VerticalAlignment.Center });
        dueWrap.Children.Add(new Border
        {
            Padding = new Thickness(8, 2, 8, 2),
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(255, 32, 32, 32)),
            Child = new TextBlock { Text = badge, Foreground = new SolidColorBrush(Colors.White), FontSize = 11 }
        });
        Grid.SetColumn(dueWrap, 2);

        grid.Children.Add(name);
        grid.Children.Add(meta);
        grid.Children.Add(dueWrap);
        return grid;
    }

    // ---------------- Counts ----------------

    private async Task LoadCountsAsync()
    {
        var entries  = await _entries.GetAllAsync();
        var banks    = await _banks.GetAllAsync();
        var ins      = await _insurance.GetAllAsync();
        var notes    = await _entries.GetByKindAsync(GeneralNotesService.EntryKind);
        var recipes  = await _recipes.GetAllAsync();
        // Vault count excludes the synthetic kinds that back other features
        // (bank logins, insurance logins, general notes) so the figure matches
        // what the user actually sees on the Vault tile.
        var vaultCount = entries.Count(e =>
            e.Kind != "bank_login" &&
            e.Kind != Insurance.CredentialsEntryKind &&
            e.Kind != GeneralNotesService.EntryKind);
        CountVault.Text     = vaultCount.ToString();
        CountBanks.Text     = banks.Count(b => !b.IsClosed).ToString();
        CountInsurance.Text = ins.Count.ToString();
        CountNotes.Text     = notes.Count.ToString();
        var tried = recipes.Count(r => r.IsTried);
        CountRecipes.Text   = $"{tried}/{recipes.Count}";
    }

    // ---------------- Recent notes ----------------

    private async Task LoadNotesAsync()
    {
        var notes = await _entries.GetByKindAsync(GeneralNotesService.EntryKind);
        var recent = notes.OrderByDescending(n => n.UpdatedAt).Take(5).ToList();
        if (recent.Count == 0)
        {
            NotesEmptyText.Visibility = Visibility.Visible;
            NotesRepeater.ItemsSource = Array.Empty<object>();
            return;
        }
        NotesEmptyText.Visibility = Visibility.Collapsed;
        NotesRepeater.ItemsSource = recent.Select(BuildNoteRow).ToList();
    }

    private FrameworkElement BuildNoteRow(VaultEntry n)
    {
        var grid = new Grid { Padding = new Thickness(8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new TextBlock { Text = string.IsNullOrWhiteSpace(n.Name) ? "(untitled)" : n.Name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        Grid.SetColumn(title, 0);
        var when = new TextBlock { Text = Ago(n.UpdatedAt), Opacity = 0.6 };
        Grid.SetColumn(when, 1);
        grid.Children.Add(title);
        grid.Children.Add(when);
        return grid;
    }

    private static string Ago(DateTimeOffset when)
    {
        var span = DateTimeOffset.Now - when;
        if (span.TotalSeconds < 60) return "just now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalHours   < 24) return $"{(int)span.TotalHours}h ago";
        if (span.TotalDays    < 30) return $"{(int)span.TotalDays}d ago";
        return when.LocalDateTime.ToString("dd MMM yyyy");
    }

    // ---------------- Database / backup ----------------

    private void LoadDbInfo()
    {
        var path = AppPaths.DatabasePath;
        DbPathText.Text = path;
        try
        {
            var fi = new FileInfo(path);
            DbSizeText.Text = fi.Exists ? FormatBytes(fi.Length) : "—";
        }
        catch { DbSizeText.Text = "—"; }
        LastBackupText.Text = FindLatestBackup() is { } latest ? Ago(latest) : "never";
    }

    private static string FormatBytes(long b)
    {
        if (b < 1024) return $"{b} B";
        if (b < 1024 * 1024) return $"{b / 1024.0:F1} KB";
        return $"{b / (1024.0 * 1024.0):F1} MB";
    }

    private static DateTimeOffset? FindLatestBackup()
    {
        try
        {
            var dir = Path.Combine(AppPaths.DataDirectory, "backups");
            if (!Directory.Exists(dir)) return null;
            var newest = new DirectoryInfo(dir)
                .EnumerateFiles("vault-*.db")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();
            return newest is null ? (DateTimeOffset?)null : new DateTimeOffset(newest.LastWriteTimeUtc, TimeSpan.Zero);
        }
        catch { return null; }
    }

    private async void BackupNow_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BackupBtn.IsEnabled = false;
            BackupStatusText.Text = "Backing up...";
            var src = AppPaths.DatabasePath;
            var dir = Path.Combine(AppPaths.DataDirectory, "backups");
            Directory.CreateDirectory(dir);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var dst = Path.Combine(dir, $"vault-{stamp}.db");
            // Plain file copy is safe here because the DB is opened with
            // Pooling=False and Cache=Private and SQLite uses WAL — a copy
            // captures the committed pages. If the user is mid-write we may
            // miss the tail, which is acceptable for a manual-on-demand backup.
            await Task.Run(() => File.Copy(src, dst, overwrite: false));
            LastBackupText.Text = "just now";
            BackupStatusText.Text = "Saved to " + dst;
        }
        catch (Exception ex)
        {
            BackupStatusText.Text = "Backup failed: " + ex.Message;
        }
        finally
        {
            BackupBtn.IsEnabled = true;
        }
    }

    // ---------------- Tile navigation ----------------

    private void OpenBudget_Tapped(object s, TappedRoutedEventArgs e)      => _nav.NavigateInShell("budget");
    private void OpenGold_Tapped(object s, TappedRoutedEventArgs e)        => _nav.NavigateInShell("gold");
    private void OpenRetirement_Tapped(object s, TappedRoutedEventArgs e)  => _nav.NavigateInShell("retirement");
    private void OpenInsurance_Tapped(object s, TappedRoutedEventArgs e)   => _nav.NavigateInShell("insurance");
    private void OpenVault_Tapped(object s, TappedRoutedEventArgs e)       => _nav.NavigateInShell("vault");
    private void OpenBanks_Tapped(object s, TappedRoutedEventArgs e)       => _nav.NavigateInShell("banks");
    private void OpenNotes_Tapped(object s, TappedRoutedEventArgs e)       => _nav.NavigateInShell("notes");
    private void OpenRecipes_Tapped(object s, TappedRoutedEventArgs e)     => _nav.NavigateInShell("recipes");

    private void ShowError(string msg)
    {
        ErrorText.Text = msg;
        ErrorText.Visibility = Visibility.Visible;
    }
}
