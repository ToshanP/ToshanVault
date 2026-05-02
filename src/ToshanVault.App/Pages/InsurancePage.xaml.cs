using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using ToshanVault.Core.Models;
using ToshanVault.Core.Security;
using ToshanVault.Data.Repositories;
using ToshanVault_App.Hosting;
using ToshanVault_App.Services;

namespace ToshanVault_App.Pages;

public sealed partial class InsurancePage : Page
{
    private readonly InsuranceRepository _repo = AppHost.GetService<InsuranceRepository>();
    private readonly InsuranceCredentialsService _credService = AppHost.GetService<InsuranceCredentialsService>();
    private readonly AttachmentService _attachments = AppHost.GetService<AttachmentService>();
    private readonly NavigationService _nav = AppHost.GetService<NavigationService>();

    private readonly ObservableCollection<InsuranceVm> _items = new();
    private readonly List<InsuranceVm> _all = new();
    private string _filter = string.Empty;
    private bool _busy;

    public InsurancePage()
    {
        InitializeComponent();
        PolicyList.ItemsSource = _items;
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
        _all.Clear();
        foreach (var r in rows) _all.Add(new InsuranceVm(r));
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        _items.Clear();
        var f = _filter;
        foreach (var vm in _all)
        {
            if (string.IsNullOrEmpty(f)
                || vm.InsurerCompany.Contains(f, StringComparison.OrdinalIgnoreCase)
                || (vm.Owner         is { Length: > 0 } o && o.Contains(f, StringComparison.OrdinalIgnoreCase))
                || (vm.PolicyNumber  is { Length: > 0 } p && p.Contains(f, StringComparison.OrdinalIgnoreCase))
                || (vm.InsuranceType is { Length: > 0 } t && t.Contains(f, StringComparison.OrdinalIgnoreCase))
                || (vm.Website       is { Length: > 0 } w && w.Contains(f, StringComparison.OrdinalIgnoreCase)))
            {
                _items.Add(vm);
            }
        }
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        _filter = (sender.Text ?? string.Empty).Trim();
        ApplyFilter();
    }

    // ---- Add ---------------------------------------------------------------
    private async void AddPolicy_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return; _busy = true;
        try
        {
            var dlg = new InsuranceDialog(this.XamlRoot, null, null);
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
            await _repo.InsertAsync(dlg.Result!);
            await ReloadAsync();
        }
        catch (VaultLockedException) { _nav.NavigateToLogin(); }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { _busy = false; }
    }

    // ---- Edit --------------------------------------------------------------
    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return; _busy = true;
        try
        {
            var id = (long)((Button)sender).Tag;
            var existing = await _repo.GetAsync(id);
            if (existing is null) { ShowError("Policy not found."); await ReloadAsync(); return; }

            var dlg = new InsuranceDialog(this.XamlRoot, existing, _attachments);
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
            await _repo.UpdateAsync(dlg.Result!);
            await ReloadAsync();
        }
        catch (VaultLockedException) { _nav.NavigateToLogin(); }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { _busy = false; }
    }

    // ---- Credentials -------------------------------------------------------
    private async void Credentials_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return; _busy = true;
        InsuranceCredentialsModel? creds = null;
        try
        {
            var id = (long)((Button)sender).Tag;
            var ins = await _repo.GetAsync(id);
            if (ins is null) { ShowError("Policy not found."); await ReloadAsync(); return; }

            var loaded = await _credService.LoadAsync(id);
            creds = new InsuranceCredentialsModel
            {
                Username = loaded.GetValueOrDefault(InsuranceCredentialsService.UsernameLabel, ""),
                Password = loaded.GetValueOrDefault(InsuranceCredentialsService.PasswordLabel, ""),
                Notes    = loaded.GetValueOrDefault(InsuranceCredentialsService.NotesLabel, ""),
            };

            var dlg = new InsuranceCredentialsDialog(this.XamlRoot, ins.InsurerCompany, creds);
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

            var specs = new List<InsuranceCredentialsService.FieldSpec>
            {
                new(InsuranceCredentialsService.UsernameLabel, creds.Username, false),
                new(InsuranceCredentialsService.PasswordLabel, creds.Password, true),
                new(InsuranceCredentialsService.NotesLabel,    creds.Notes,    false),
            };
            await _credService.SaveAsync(id, specs);
            ShowInfo("Credentials saved (encrypted in vault).");
        }
        catch (VaultLockedException) { _nav.NavigateToLogin(); }
        catch (Exception ex) { ShowError(ex.Message); }
        finally
        {
            if (creds is not null) { creds.Username = creds.Password = creds.Notes = string.Empty; }
            _busy = false;
        }
    }

    // ---- Delete ------------------------------------------------------------
    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return; _busy = true;
        try
        {
            var id = (long)((Button)sender).Tag;
            var ins = await _repo.GetAsync(id);
            if (ins is null) { await ReloadAsync(); return; }

            var confirm = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = $"Delete {ins.InsurerCompany}?",
                Content = new TextBlock
                {
                    Text = "This permanently removes the policy, its encrypted credentials and any attachments. This cannot be undone.",
                    TextWrapping = TextWrapping.Wrap,
                },
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

            await _repo.DeleteAsync(id);
            await ReloadAsync();
            ShowInfo($"Deleted {ins.InsurerCompany}.");
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

    private void ShowInfo(string msg)
    {
        InfoBar.Severity = InfoBarSeverity.Success;
        InfoBar.Title = "Done";
        InfoBar.Message = msg;
        InfoBar.IsOpen = true;
    }
}

public sealed class InsuranceVm
{
    public InsuranceVm(Insurance i)
    {
        Id = i.Id;
        InsurerCompany = i.InsurerCompany;
        PolicyNumber   = i.PolicyNumber;
        InsuranceType  = i.InsuranceType;
        Website        = i.Website;
        Owner          = i.Owner;

        // Subtitle: "Toshan · Health · POL12345" with sensible fallbacks so
        // empties don't produce stray dots.
        var parts = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(i.Owner))         parts.Add(i.Owner!);
        if (!string.IsNullOrWhiteSpace(i.InsuranceType)) parts.Add(i.InsuranceType!);
        if (!string.IsNullOrWhiteSpace(i.PolicyNumber))  parts.Add(i.PolicyNumber!);
        Subtitle = parts.Count == 0 ? "(no details)" : string.Join(" · ", parts);

        if (i.RenewalDate is { } d)
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var days  = d.DayNumber - today.DayNumber;
            RenewalText = days switch
            {
                < 0  => $"Renewed/expired {-days}d ago ({d:yyyy-MM-dd})",
                0    => $"Renews today ({d:yyyy-MM-dd})",
                1    => $"Renews tomorrow ({d:yyyy-MM-dd})",
                _    => $"Renews in {days}d ({d:yyyy-MM-dd})",
            };
            RenewalVisibility = Visibility.Visible;
            // Red within 30 days (incl. overdue), amber 31-60, default beyond.
            RenewalBrush = days <= 30
                ? new SolidColorBrush(Microsoft.UI.Colors.IndianRed)
                : days <= 60
                    ? new SolidColorBrush(Microsoft.UI.Colors.DarkOrange)
                    : (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        }
        else
        {
            RenewalText = string.Empty;
            RenewalVisibility = Visibility.Collapsed;
            RenewalBrush = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        }

        WebsiteVisibility = string.IsNullOrWhiteSpace(Website) ? Visibility.Collapsed : Visibility.Visible;
    }

    public long Id { get; }
    public string InsurerCompany { get; }
    public string? PolicyNumber { get; }
    public string? InsuranceType { get; }
    public string? Website { get; }
    public string? Owner { get; }
    public string Subtitle { get; }
    public string RenewalText { get; }
    public Visibility RenewalVisibility { get; }
    public Brush RenewalBrush { get; }
    public Visibility WebsiteVisibility { get; }
}
