using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ToshanVault.Core.Models;
using ToshanVault.Core.Security;
using ToshanVault.Data.Repositories;
using ToshanVault_App.Hosting;
using ToshanVault_App.Services;

namespace ToshanVault_App.Pages;

public sealed partial class BankAccountsPage : Page
{
    private readonly BankAccountRepository _bankRepo = AppHost.GetService<BankAccountRepository>();
    private readonly BankAccountCredentialRepository _credRepo = AppHost.GetService<BankAccountCredentialRepository>();
    private readonly BankCredentialsService _credService = AppHost.GetService<BankCredentialsService>();
    private readonly AttachmentService _attachments = AppHost.GetService<AttachmentService>();
    private readonly NavigationService _nav = AppHost.GetService<NavigationService>();

    private readonly ObservableCollection<BankAccountVm> _open = new();
    private readonly ObservableCollection<ClosedBankAccountVm> _closed = new();
    private readonly List<BankAccountVm> _allOpen = new();
    private readonly List<ClosedBankAccountVm> _allClosed = new();
    private string _filter = string.Empty;
    private bool _busy;

    public BankAccountsPage()
    {
        InitializeComponent();
        OpenList.ItemsSource = _open;
        ClosedList.ItemsSource = _closed;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        try { await ReloadAsync(); }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async Task ReloadAsync()
    {
        var rows = await _bankRepo.GetAllAsync();
        _allOpen.Clear();
        _allClosed.Clear();
        foreach (var r in rows)
        {
            // One credential lookup per account is fine for the tile-grid scale we
            // expect (tens of accounts). Switch to a single grouped query if the
            // count grows by an order of magnitude.
            var creds = await _credRepo.GetByAccountAsync(r.Id);
            if (r.IsClosed) _allClosed.Add(new ClosedBankAccountVm(r, creds));
            else _allOpen.Add(new BankAccountVm(r, creds));
        }
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        _open.Clear();
        _closed.Clear();
        var f = _filter;
        bool Match(string bank, string accountName) =>
            string.IsNullOrEmpty(f) ||
            bank.Contains(f, StringComparison.OrdinalIgnoreCase) ||
            accountName.Contains(f, StringComparison.OrdinalIgnoreCase);

        foreach (var vm in _allOpen)
            if (Match(vm.Bank, vm.AccountName)) _open.Add(vm);
        foreach (var vm in _allClosed)
            if (Match(vm.Bank, vm.AccountName)) _closed.Add(vm);

        ClosedHeader.Text = $"Closed accounts ({_closed.Count}{(string.IsNullOrEmpty(f) ? string.Empty : $" / {_allClosed.Count}")})";
        ClosedExpander.Visibility = _allClosed.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        _filter = (sender.Text ?? string.Empty).Trim();
        ApplyFilter();
    }

    // ---- Add ---------------------------------------------------------------
    private async void AddAccount_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return; _busy = true;
        try
        {
            var dlg = new BankAccountDialog(this.XamlRoot, null);
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
            await _bankRepo.InsertAsync(dlg.Result!);
            await ReloadAsync();
        }
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
            var existing = await _bankRepo.GetAsync(id);
            if (existing is null) { ShowError("Account not found."); await ReloadAsync(); return; }
            // Snapshot original close state BEFORE the dialog (it mutates `existing` in place).
            var wasClosed = existing.IsClosed;

            var dlg = new BankAccountDialog(this.XamlRoot, existing, _attachments);
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
            var result = dlg.Result!;

            await _bankRepo.UpdateAsync(result);

            if (!wasClosed && result.IsClosed)
            {
                await _bankRepo.CloseAsync(id, result.CloseReason, result.ClosedDate);
                ShowInfo($"Closed {result.Bank} · {result.AccountName}. Linked credentials kept.");
            }
            else if (wasClosed && !result.IsClosed)
            {
                await _bankRepo.ReopenAsync(id);
                ShowInfo($"Reopened {result.Bank} · {result.AccountName}.");
            }

            await ReloadAsync();
        }
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
            var existing = await _bankRepo.GetAsync(id);
            if (existing is null) { ShowError("Account not found."); await ReloadAsync(); return; }

            var (saved, value) = await NotesWindow.ShowAsync(
                $"{existing.Bank} — {existing.AccountName} Notes", existing.Notes);
            if (!saved) return;

            existing.Notes = value;
            await _bankRepo.UpdateAsync(existing);
            ShowInfo("Notes saved.");
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { _busy = false; }
    }

    // ---- Existing credential clicked (avatar) ------------------------------
    private async void Credential_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return; _busy = true;
        try
        {
            var avatar = (CredentialAvatarVm)((Button)sender).Tag;
            await OpenCredentialDialogAsync(avatar.AccountId, avatar.Owner, avatar.VaultEntryId, avatar.AccountTitle);
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { _busy = false; }
    }

    // ---- Add credential (+) — owner picker first ---------------------------
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

    private async void AddCredential_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return; _busy = true;
        try
        {
            var id = (long)((Button)sender).Tag;
            var account = await _bankRepo.GetAsync(id);
            if (account is null) { ShowError("Account not found."); await ReloadAsync(); return; }

            var existingOwners = (await _credRepo.GetByAccountAsync(id))
                .Select(c => c.Owner)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var available = BankCredentialsService.KnownOwners
                .Where(o => !existingOwners.Contains(o))
                .ToList();
            if (available.Count == 0) { ShowInfo("All known owners already have a credential. Edit an existing one or extend the dropdown."); return; }

            var picker = new OwnerPickerDialog(this.XamlRoot, available);
            if (await picker.ShowAsync() != ContentDialogResult.Primary || picker.SelectedOwner is null) return;

            await OpenCredentialDialogAsync(id, picker.SelectedOwner, vaultEntryId: null,
                                            accountTitle: account.Bank + " · " + account.AccountName);
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { _busy = false; }
    }

    private async Task OpenCredentialDialogAsync(long accountId, string owner, long? vaultEntryId, string accountTitle)
    {
        CredentialsModel? creds = null;
        try
        {
            var loaded = await _credService.LoadAsync(vaultEntryId);
            creds = new CredentialsModel
            {
                Username = loaded.GetValueOrDefault(BankCredentialsService.UsernameLabel, ""),
                ClientId = loaded.GetValueOrDefault(BankCredentialsService.ClientIdLabel, ""),
                Password = loaded.GetValueOrDefault(BankCredentialsService.PasswordLabel, ""),
                CardPin  = loaded.GetValueOrDefault(BankCredentialsService.CardPinLabel, ""),
                PhonePin = loaded.GetValueOrDefault(BankCredentialsService.PhonePinLabel, ""),
            };
            for (var i = 0; i < BankCredentialsService.MaxQa; i++)
            {
                creds.Qa[i] = new QaPair(
                    loaded.GetValueOrDefault($"{BankCredentialsService.QuestionLabelPrefix}{i + 1}", ""),
                    loaded.GetValueOrDefault($"{BankCredentialsService.AnswerLabelPrefix}{i + 1}", ""));
            }

            var dlg = new CredentialsDialog(this.XamlRoot, accountTitle, owner, creds, allowDelete: vaultEntryId is not null);
            var result = await dlg.ShowAsync();

            if (dlg.DeleteRequested)
            {
                // Confirm AFTER the credentials dialog has closed (WinUI only
                // allows one open ContentDialog per XAML root, so the confirm
                // can't be shown from inside the cred dialog's button handler).
                var confirm = new ContentDialog
                {
                    XamlRoot = this.XamlRoot,
                    Title = $"Delete {owner} credential?",
                    Content = new TextBlock
                    {
                        Text = $"All encrypted username/password/Q&A for {owner} on {accountTitle} will be permanently removed. This cannot be undone.",
                        TextWrapping = TextWrapping.Wrap,
                    },
                    PrimaryButtonText = "Delete",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close,
                };
                if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

                // The credential row + cascade trigger will remove the linked
                // vault_entry and all its vault_field rows in one statement.
                var rowToDelete = (await _credRepo.GetByAccountAsync(accountId))
                    .FirstOrDefault(c => string.Equals(c.Owner, owner, StringComparison.OrdinalIgnoreCase));
                if (rowToDelete is not null)
                {
                    await _credRepo.DeleteAsync(rowToDelete.Id);
                    ShowInfo($"Deleted {owner} credential for {accountTitle}.");
                    await ReloadAsync();
                }
                return;
            }

            if (result != ContentDialogResult.Primary) return;

            var specs = new List<BankCredentialsService.FieldSpec>(6 + BankCredentialsService.MaxQa * 2)
            {
                new(BankCredentialsService.UsernameLabel, creds.Username, false),
                new(BankCredentialsService.ClientIdLabel, creds.ClientId, false),
                new(BankCredentialsService.PasswordLabel, creds.Password, true),
                new(BankCredentialsService.CardPinLabel,  creds.CardPin,  true),
                new(BankCredentialsService.PhonePinLabel, creds.PhonePin, true),
                // Preserve existing credential notes (now edited via notes popup)
                new(BankCredentialsService.NotesLabel,    loaded.GetValueOrDefault(BankCredentialsService.NotesLabel, ""), false),
            };
            for (var i = 0; i < BankCredentialsService.MaxQa; i++)
            {
                specs.Add(new($"{BankCredentialsService.QuestionLabelPrefix}{i + 1}", creds.Qa[i].Question, false));
                specs.Add(new($"{BankCredentialsService.AnswerLabelPrefix}{i + 1}", creds.Qa[i].Answer, true));
            }

            await _credService.SaveAsync(accountId, owner, $"{accountTitle} ({owner})", specs);
            ShowInfo($"Saved {owner} credential (encrypted in vault).");
            await ReloadAsync();
        }
        catch (VaultLockedException)
        {
            _nav.NavigateToLogin();
        }
        finally
        {
            if (creds is not null)
            {
                creds.Username = creds.ClientId = creds.Password = string.Empty;
                creds.CardPin = creds.PhonePin = string.Empty;
                for (var i = 0; i < creds.Qa.Length; i++) creds.Qa[i] = new QaPair("", "");
            }
        }
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
    // Persists the new order back to the DB after the user drops a tile.
    // Reordering is disabled while a search filter is active because the
    // visible subset is not the full list and overwriting sort_order with
    // partial-list indices would scramble hidden rows. Whole body wrapped in
    // try/catch — `async void` exceptions tear down the WinUI app.
    private async void OpenList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        try
        {
            if (!string.IsNullOrEmpty(_filter))
            {
                ShowError("Clear the search box before reordering.");
                await ReloadAsync();
                return;
            }
            _allOpen.Clear();
            _allOpen.AddRange(_open);
            await _bankRepo.UpdateSortOrderAsync(_open.Select(v => v.Id).ToList());
        }
        catch (Exception ex)
        {
            ShowError($"Could not save order: {ex.Message}");
            try { await ReloadAsync(); } catch { /* best-effort */ }
        }
    }

    private async void ClosedList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        try
        {
            if (!string.IsNullOrEmpty(_filter))
            {
                ShowError("Clear the search box before reordering.");
                await ReloadAsync();
                return;
            }
            _allClosed.Clear();
            _allClosed.AddRange(_closed);
            await _bankRepo.UpdateSortOrderAsync(_closed.Select(v => v.Id).ToList());
        }
        catch (Exception ex)
        {
            ShowError($"Could not save order: {ex.Message}");
            try { await ReloadAsync(); } catch { /* best-effort */ }
        }
    }
}

// ---------------------------------------------------------------------------
// Per-credential avatar VM. Kept in BankAccountVm.Credentials and used as the
// Tag on each tile avatar button so click handlers don't have to re-query the
// DB just to know which (account, owner) to load.
// ---------------------------------------------------------------------------
public sealed class CredentialAvatarVm
{
    public CredentialAvatarVm(long accountId, string accountTitle, BankAccountCredential c)
    {
        AccountId = accountId;
        AccountTitle = accountTitle;
        CredentialId = c.Id;
        Owner = c.Owner;
        VaultEntryId = c.VaultEntryId;
        Initial = string.IsNullOrEmpty(c.Owner) ? "?" : char.ToUpperInvariant(c.Owner[0]).ToString();
    }
    public long AccountId { get; }
    public string AccountTitle { get; }
    public long CredentialId { get; }
    public string Owner { get; }
    public long VaultEntryId { get; }
    public string Initial { get; }
}

// ---------------------------------------------------------------------------
// Row VMs for the GridViews. Added Credentials list + visibility flag for the
// "+" pill (hidden when every known owner already has a credential).
// ---------------------------------------------------------------------------
public sealed class BankAccountVm
{
    public BankAccountVm(BankAccount a, IReadOnlyList<BankAccountCredential> creds)
    {
        Source = a;
        Id = a.Id;
        Bank = a.Bank;
        AccountName = a.AccountName;
        AccountType = a.AccountType.ToString();
        MaskedBsb = Mask(a.Bsb, keepLast: 0);
        MaskedAccountNumber = Mask(a.AccountNumber, keepLast: 4);
        var title = a.Bank + " · " + a.AccountName;
        Credentials = new ObservableCollection<CredentialAvatarVm>(
            creds.Select(c => new CredentialAvatarVm(a.Id, title, c)));
        var existing = creds.Select(c => c.Owner).ToHashSet(StringComparer.OrdinalIgnoreCase);
        CanAddCredentialVisibility = BankCredentialsService.KnownOwners.Any(o => !existing.Contains(o))
            ? Visibility.Visible : Visibility.Collapsed;
        Website = a.Website;
        WebsiteVisibility = string.IsNullOrWhiteSpace(a.Website) ? Visibility.Collapsed : Visibility.Visible;
    }
    public BankAccount Source { get; }
    public long Id { get; }
    public string Bank { get; }
    public string AccountName { get; }
    public string AccountType { get; }
    public string MaskedBsb { get; }
    public string MaskedAccountNumber { get; }
    public string? Website { get; }
    public Visibility WebsiteVisibility { get; }
    public ObservableCollection<CredentialAvatarVm> Credentials { get; }
    public Visibility CanAddCredentialVisibility { get; }

    private static string Mask(string? value, int keepLast)
    {
        if (string.IsNullOrEmpty(value)) return "—";
        if (keepLast <= 0 || value.Length <= keepLast) return new string('•', Math.Min(value.Length, 6));
        return new string('•', Math.Max(0, value.Length - keepLast)) + value[^keepLast..];
    }
}

public sealed class ClosedBankAccountVm
{
    public ClosedBankAccountVm(BankAccount a, IReadOnlyList<BankAccountCredential> creds)
    {
        Id = a.Id;
        Bank = a.Bank;
        AccountName = a.AccountName;
        ClosedDateDisplay = a.ClosedDate?.ToLocalTime().ToString("yyyy-MM-dd") ?? "(unknown)";
        ReasonDisplay = string.IsNullOrWhiteSpace(a.CloseReason) ? "no reason given" : a.CloseReason!;
        var title = a.Bank + " · " + a.AccountName;
        Credentials = new ObservableCollection<CredentialAvatarVm>(
            creds.Select(c => new CredentialAvatarVm(a.Id, title, c)));
    }
    public long Id { get; }
    public string Bank { get; }
    public string AccountName { get; }
    public string ClosedDateDisplay { get; }
    public string ReasonDisplay { get; }
    public ObservableCollection<CredentialAvatarVm> Credentials { get; }
}
