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
    private readonly InsuranceCredentialRepository _credRepo = AppHost.GetService<InsuranceCredentialRepository>();
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
        try
        {
            await _credService.MigrateNotesToColumnAsync();
            await ReloadAsync();
        }
        catch (VaultLockedException) { _nav.NavigateToLogin(); }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async Task ReloadAsync()
    {
        var rows = await _repo.GetAllAsync();
        _all.Clear();
        foreach (var r in rows)
        {
            var creds = await _credRepo.GetByInsuranceAsync(r.Id);
            _all.Add(new InsuranceVm(r, creds));
        }
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

    // ---- Notes popup --------------------------------------------------------
    private async void Notes_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return; _busy = true;
        try
        {
            var id = (long)((Button)sender).Tag;
            var existing = await _repo.GetAsync(id);
            if (existing is null) { ShowError("Policy not found."); await ReloadAsync(); return; }

            var (saved, value) = await NotesWindow.ShowAsync(
                $"{existing.InsurerCompany} Notes", existing.Notes);
            if (!saved) return;

            existing.Notes = value;
            await _repo.UpdateAsync(existing);
            ShowInfo("Notes saved.");
        }
        catch (VaultLockedException) { _nav.NavigateToLogin(); }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { _busy = false; }
    }

    // ---- Credentials (multi-owner) -----------------------------------------
    private async void Credential_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return; _busy = true;
        try
        {
            var avatar = (InsuranceCredentialAvatarVm)((Button)sender).Tag;
            await OpenCredentialDialogAsync(avatar.InsuranceId, avatar.Owner, avatar.VaultEntryId, avatar.InsuranceTitle);
        }
        catch (VaultLockedException) { _nav.NavigateToLogin(); }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { _busy = false; }
    }

    private async void AddCredential_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return; _busy = true;
        try
        {
            var id = (long)((Button)sender).Tag;
            var ins = await _repo.GetAsync(id);
            if (ins is null) { ShowError("Policy not found."); await ReloadAsync(); return; }

            var existingOwners = (await _credRepo.GetByInsuranceAsync(id))
                .Select(c => c.Owner)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var available = InsuranceCredentialsService.KnownOwners
                .Where(o => !existingOwners.Contains(o))
                .ToList();
            if (available.Count == 0) { ShowInfo("All known owners already have a credential."); return; }

            var picker = new OwnerPickerDialog(this.XamlRoot, available);
            if (await picker.ShowAsync() != ContentDialogResult.Primary || picker.SelectedOwner is null) return;

            await OpenCredentialDialogAsync(id, picker.SelectedOwner, vaultEntryId: null,
                                            insuranceTitle: ins.InsurerCompany);
        }
        catch (VaultLockedException) { _nav.NavigateToLogin(); }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { _busy = false; }
    }

    private async Task OpenCredentialDialogAsync(long insuranceId, string owner, long? vaultEntryId, string insuranceTitle)
    {
        InsuranceCredentialsModel? creds = null;
        try
        {
            var loaded = await _credService.LoadAsync(vaultEntryId);
            creds = new InsuranceCredentialsModel
            {
                Username = loaded.GetValueOrDefault(InsuranceCredentialsService.UsernameLabel, ""),
                Password = loaded.GetValueOrDefault(InsuranceCredentialsService.PasswordLabel, ""),
            };
            for (var i = 0; i < InsuranceCredentialsService.MaxQa; i++)
            {
                creds.Qa[i] = new QaPair(
                    loaded.GetValueOrDefault($"{InsuranceCredentialsService.QuestionLabelPrefix}{i + 1}", ""),
                    loaded.GetValueOrDefault($"{InsuranceCredentialsService.AnswerLabelPrefix}{i + 1}", ""));
            }

            var dlg = new InsuranceCredentialsDialog(this.XamlRoot, insuranceTitle, owner, creds, allowDelete: vaultEntryId is not null);
            var result = await dlg.ShowAsync();

            if (dlg.DeleteRequested)
            {
                var confirm = new ContentDialog
                {
                    XamlRoot = this.XamlRoot,
                    Title = $"Delete {owner} credential?",
                    Content = new TextBlock
                    {
                        Text = $"All encrypted username/password/Q&A for {owner} on {insuranceTitle} will be permanently removed.",
                        TextWrapping = TextWrapping.Wrap,
                    },
                    PrimaryButtonText = "Delete",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close,
                };
                if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

                var rowToDelete = (await _credRepo.GetByInsuranceAsync(insuranceId))
                    .FirstOrDefault(c => string.Equals(c.Owner, owner, StringComparison.OrdinalIgnoreCase));
                if (rowToDelete is not null)
                {
                    await _credRepo.DeleteAsync(rowToDelete.Id);
                    ShowInfo($"Deleted {owner} credential for {insuranceTitle}.");
                    await ReloadAsync();
                }
                return;
            }

            if (result != ContentDialogResult.Primary) return;

            var specs = new List<InsuranceCredentialsService.FieldSpec>(2 + InsuranceCredentialsService.MaxQa * 2)
            {
                new(InsuranceCredentialsService.UsernameLabel, creds.Username, false),
                new(InsuranceCredentialsService.PasswordLabel, creds.Password, true),
            };
            for (var i = 0; i < InsuranceCredentialsService.MaxQa; i++)
            {
                specs.Add(new($"{InsuranceCredentialsService.QuestionLabelPrefix}{i + 1}", creds.Qa[i].Question, false));
                specs.Add(new($"{InsuranceCredentialsService.AnswerLabelPrefix}{i + 1}", creds.Qa[i].Answer, true));
            }

            await _credService.SaveAsync(insuranceId, owner, $"{insuranceTitle} ({owner})", specs);
            ShowInfo($"Saved {owner} credential (encrypted in vault).");
            await ReloadAsync();
        }
        catch (VaultLockedException) { _nav.NavigateToLogin(); }
        finally
        {
            if (creds is not null)
            {
                creds.Username = creds.Password = string.Empty;
                for (var i = 0; i < creds.Qa.Length; i++) creds.Qa[i] = new QaPair("", "");
            }
        }
    }

    // ---- Delete ------------------------------------------------------------
    private async void LaunchWebsite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el && el.Tag is string url && !string.IsNullOrWhiteSpace(url))
        {
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                url = "https://" + url;
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                (uri.Scheme == "http" || uri.Scheme == "https"))
                await Windows.System.Launcher.LaunchUriAsync(uri);
        }
    }

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

    // ---- Drag & drop reordering -------------------------------------------
    // Persists the new tile order to insurance.sort_order. Disabled while a
    // search filter is active so we don't overwrite hidden rows' positions
    // with partial-list indices. Whole body wrapped in try/catch because this
    // is `async void` — an unhandled exception would tear down the app.
    private async void PolicyList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        try
        {
            if (!string.IsNullOrEmpty(_filter))
            {
                ShowError("Clear the search box before reordering.");
                await ReloadAsync();
                return;
            }
            _all.Clear();
            _all.AddRange(_items);
            await _repo.UpdateSortOrderAsync(_items.Select(v => v.Id).ToList());
        }
        catch (Exception ex)
        {
            ShowError($"Could not save order: {ex.Message}");
            try { await ReloadAsync(); } catch { /* swallowed: reload-on-failure is best effort */ }
        }
    }
}

public sealed class InsuranceVm
{
    public InsuranceVm(Insurance i, IReadOnlyList<InsuranceCredential> creds)
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

        Credentials = new ObservableCollection<InsuranceCredentialAvatarVm>(
            creds.Select(c => new InsuranceCredentialAvatarVm(i.Id, i.InsurerCompany, c)));
        var existing = creds.Select(c => c.Owner).ToHashSet(StringComparer.OrdinalIgnoreCase);
        CanAddCredentialVisibility = InsuranceCredentialsService.KnownOwners.Any(o => !existing.Contains(o))
            ? Visibility.Visible : Visibility.Collapsed;
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
    public ObservableCollection<InsuranceCredentialAvatarVm> Credentials { get; }
    public Visibility CanAddCredentialVisibility { get; }
}

public sealed class InsuranceCredentialAvatarVm
{
    public InsuranceCredentialAvatarVm(long insuranceId, string insuranceTitle, InsuranceCredential c)
    {
        InsuranceId = insuranceId;
        InsuranceTitle = insuranceTitle;
        CredentialId = c.Id;
        Owner = c.Owner;
        VaultEntryId = c.VaultEntryId;
        Initial = string.IsNullOrEmpty(c.Owner) ? "?" : char.ToUpperInvariant(c.Owner[0]).ToString();
    }
    public long InsuranceId { get; }
    public string InsuranceTitle { get; }
    public long CredentialId { get; }
    public string Owner { get; }
    public long VaultEntryId { get; }
    public string Initial { get; }
}
