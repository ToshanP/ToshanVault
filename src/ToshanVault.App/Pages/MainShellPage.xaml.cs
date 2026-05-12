using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ToshanVault.Core.Security;
using ToshanVault_App.Hosting;
using ToshanVault_App.Services;

namespace ToshanVault_App.Pages;

public sealed partial class MainShellPage : Page
{
    private readonly Vault _vault;
    private readonly NavigationService _nav;
    private readonly IdleLockService _idle;
    private readonly MintInvestmentReminderService _mintReminder;
    private DispatcherTimer? _idleTimer;
    private bool _wired;
    private bool _lockRequested;

    public MainShellPage()
    {
        InitializeComponent();
        _vault = AppHost.GetService<Vault>();
        _nav = AppHost.GetService<NavigationService>();
        _idle = AppHost.GetService<IdleLockService>();
        _mintReminder = AppHost.GetService<MintInvestmentReminderService>();
        Unloaded += (_, _) => Teardown();
    }

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        if (_wired) return; // guard against duplicate Loaded events
        _wired = true;

        ContentFrame.Navigate(typeof(DashboardPage));
        _nav.RegisterShellNavigator(NavigateByTag);

        _idle.Reset();
        _idle.IdleThresholdReached += OnIdleThreshold;

        _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _idleTimer.Tick += (_, _) => _idle.Tick();
        _idleTimer.Start();

        DispatcherQueue.TryEnqueue(async () =>
            await _mintReminder.ShowIfDueAsync(XamlRoot, () => _nav.NavigateInShell("mint")));
    }

    /// <summary>
    /// Programmatic NavView selection driver used by Dashboard tile clicks.
    /// Setting <c>SelectedItem</c> raises <c>SelectionChanged</c> which performs
    /// the frame navigation, so we don't duplicate the page-switch logic here.
    /// </summary>
    private void NavigateByTag(string tag)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            foreach (var obj in NavView.MenuItems)
            {
                if (obj is NavigationViewItem nvi && (nvi.Tag as string) == tag)
                {
                    NavView.SelectedItem = nvi;
                    return;
                }
            }
        });
    }

    private void OnIdleThreshold(object? sender, EventArgs e)
        => DispatcherQueue.TryEnqueue(LockAndReturnToLogin);

    private void Teardown()
    {
        if (!_wired) return;
        _wired = false;
        _idleTimer?.Stop();
        _idleTimer = null;
        _idle.IdleThresholdReached -= OnIdleThreshold;
    }

    private void LockAndReturnToLogin()
    {
        if (_lockRequested) return; // re-entrancy guard: idle tick + manual click race
        _lockRequested = true;
        Teardown();
        _vault.Lock();
        _nav.NavigateToLogin();
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected) { ContentFrame.Navigate(typeof(SettingsPage)); return; }
        if (args.SelectedItem is not NavigationViewItem item) return;
        switch (item.Tag)
        {
            case "search":    ContentFrame.Navigate(typeof(SearchPage)); break;
            case "dashboard": ContentFrame.Navigate(typeof(DashboardPage)); break;
            case "budget":    ContentFrame.Navigate(typeof(BudgetPage)); break;
            case "vault":     ContentFrame.Navigate(typeof(VaultPage)); break;
            case "notes":     ContentFrame.Navigate(typeof(GeneralNotesPage)); break;
            case "banks":     ContentFrame.Navigate(typeof(BankAccountsPage)); break;
            case "insurance": ContentFrame.Navigate(typeof(InsurancePage)); break;
            case "retirement": ContentFrame.Navigate(typeof(RetirementPlanningPage)); break;
            case "retincexp":  ContentFrame.Navigate(typeof(RetirementIncomeExpensePage)); break;
            case "gold":      ContentFrame.Navigate(typeof(GoldOrnamentsPage)); break;
            case "mint":      ContentFrame.Navigate(typeof(MintInvestmentPage)); break;
            case "about":     ContentFrame.Navigate(typeof(AboutPage)); break;
            case "lock":      LockAndReturnToLogin(); break;
        }
    }
}
