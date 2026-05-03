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

public sealed partial class VaultPage : Page
{
    private readonly VaultEntryRepository _entryRepo = AppHost.GetService<VaultEntryRepository>();
    private readonly WebCredentialsService _credService = AppHost.GetService<WebCredentialsService>();
    private readonly AttachmentService _attachments = AppHost.GetService<AttachmentService>();
    private readonly NavigationService _nav = AppHost.GetService<NavigationService>();

    private readonly ObservableCollection<WebEntryVm> _entries = new();
    private readonly List<WebEntryVm> _allEntries = new();
    private string _filter = string.Empty;
    private bool _busy;

    public VaultPage()
    {
        InitializeComponent();
        EntryList.ItemsSource = _entries;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        try { await ReloadAsync(); }
        catch (VaultLockedException) { _nav.NavigateToLogin(); }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async Task ReloadAsync()
    {
        var rows = await _entryRepo.GetByKindAsync(WebCredentialsService.EntryKind);
        _allEntries.Clear();
        // Only the two non-secret display fields — keep password + security
        // answers out of memory until the user actually opens an entry.
        var previewLabels = new[]
        {
            WebCredentialsService.NumberLabel,
            WebCredentialsService.WebsiteLabel,
        };
        foreach (var r in rows)
        {
            string? number = null, website = null;
            try
            {
                var loaded = await _credService.LoadLabelsAsync(r.Id, previewLabels);
                number  = loaded.GetValueOrDefault(WebCredentialsService.NumberLabel);
                website = loaded.GetValueOrDefault(WebCredentialsService.WebsiteLabel);
            }
            catch (VaultLockedException)
            {
                // Vault was locked between page nav and reload — propagate so
                // the navigation host can route back to login. There is no
                // useful tile to show without the DEK.
                throw;
            }
            catch (Exception ex)
            {
                // One bad row (e.g. crypto tag mismatch on a tampered field)
                // shouldn't blank the whole list. Surface via InfoBar so the
                // user knows something is off, but keep going.
                ShowError($"Could not preview entry #{r.Id}: {ex.Message}");
            }
            _allEntries.Add(new WebEntryVm(r, number, website));
        }
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        _entries.Clear();
        var f = _filter;
        foreach (var vm in _allEntries)
        {
            if (string.IsNullOrEmpty(f)
                || vm.Name.Contains(f, StringComparison.OrdinalIgnoreCase)
                || vm.Subtitle.Contains(f, StringComparison.OrdinalIgnoreCase)
                || (vm.Number  is { Length: > 0 } n && n.Contains(f, StringComparison.OrdinalIgnoreCase))
                || (vm.Website is { Length: > 0 } w && w.Contains(f, StringComparison.OrdinalIgnoreCase)))
            {
                _entries.Add(vm);
            }
        }
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        _filter = (sender.Text ?? string.Empty).Trim();
        ApplyFilter();
    }

    // ---- Add ---------------------------------------------------------------
    private async void AddEntry_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return; _busy = true;
        try
        {
            var dlg = new VaultEntryDialog(this.XamlRoot, null, null, null, null);
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

            var entry = dlg.Result!;
            var entryId = await _entryRepo.InsertAsync(entry);

            // Persist the encrypted-but-displayed-plain fields tied to this new
            // entry. Empty values are skipped by the service (no field row).
            await _credService.SaveAsync(entryId, BuildEntryFieldSpecs(
                dlg.NumberValue, dlg.WebsiteValue, dlg.AdditionalDetailsValue));

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
            var existing = await _entryRepo.GetAsync(id);
            if (existing is null) { ShowError("Entry not found."); await ReloadAsync(); return; }

            var loaded = await _credService.LoadAsync(id);
            var dlg = new VaultEntryDialog(this.XamlRoot, existing,
                loaded.GetValueOrDefault(WebCredentialsService.NumberLabel),
                loaded.GetValueOrDefault(WebCredentialsService.WebsiteLabel),
                loaded.GetValueOrDefault(WebCredentialsService.AdditionalDetailsLabel),
                _attachments);
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

            await _entryRepo.UpdateAsync(dlg.Result!);
            await _credService.SaveAsync(id, BuildEntryFieldSpecs(
                dlg.NumberValue, dlg.WebsiteValue, dlg.AdditionalDetailsValue));

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
        WebCredentialsModel? creds = null;
        try
        {
            var id = (long)((Button)sender).Tag;
            var entry = await _entryRepo.GetAsync(id);
            if (entry is null) { ShowError("Entry not found."); await ReloadAsync(); return; }

            var loaded = await _credService.LoadAsync(id);
            creds = new WebCredentialsModel
            {
                Username = loaded.GetValueOrDefault(WebCredentialsService.UsernameLabel, ""),
                Password = loaded.GetValueOrDefault(WebCredentialsService.PasswordLabel, ""),
            };
            for (var i = 0; i < WebCredentialsService.MaxQa; i++)
            {
                creds.Qa[i] = new QaPair(
                    loaded.GetValueOrDefault($"{WebCredentialsService.QuestionLabelPrefix}{i + 1}", ""),
                    loaded.GetValueOrDefault($"{WebCredentialsService.AnswerLabelPrefix}{i + 1}", ""));
            }

            var dlg = new VaultCredentialsDialog(this.XamlRoot, entry.Name, creds);
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

            var specs = new List<WebCredentialsService.FieldSpec>(2 + WebCredentialsService.MaxQa * 2)
            {
                new(WebCredentialsService.UsernameLabel, creds.Username, false),
                new(WebCredentialsService.PasswordLabel, creds.Password, true),
            };
            for (var i = 0; i < WebCredentialsService.MaxQa; i++)
            {
                specs.Add(new($"{WebCredentialsService.QuestionLabelPrefix}{i + 1}", creds.Qa[i].Question, false));
                specs.Add(new($"{WebCredentialsService.AnswerLabelPrefix}{i + 1}",   creds.Qa[i].Answer,   true));
            }

            await _credService.SaveAsync(id, specs);
            ShowInfo("Credentials saved (encrypted in vault).");
        }
        catch (VaultLockedException) { _nav.NavigateToLogin(); }
        catch (Exception ex) { ShowError(ex.Message); }
        finally
        {
            // Best-effort plaintext lifetime reduction; PasswordBox.Password is a
            // managed string we can't truly zero, but null the references so GC
            // can reclaim them sooner.
            if (creds is not null)
            {
                creds.Username = creds.Password = string.Empty;
                for (var i = 0; i < creds.Qa.Length; i++) creds.Qa[i] = new QaPair("", "");
            }
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
            var entry = await _entryRepo.GetAsync(id);
            if (entry is null) { await ReloadAsync(); return; }

            var confirm = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = $"Delete {entry.Name}?",
                Content = new TextBlock
                {
                    Text = "This permanently removes the entry and all its encrypted fields. This cannot be undone.",
                    TextWrapping = TextWrapping.Wrap,
                },
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

            await _entryRepo.DeleteAsync(id);
            await ReloadAsync();
            ShowInfo($"Deleted {entry.Name}.");
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { _busy = false; }
    }

    private static IReadOnlyList<WebCredentialsService.FieldSpec> BuildEntryFieldSpecs(
        string? number, string? website, string? additionalDetails) =>
        new WebCredentialsService.FieldSpec[]
        {
            new(WebCredentialsService.NumberLabel,            number,            false),
            new(WebCredentialsService.WebsiteLabel,           website,           false),
            new(WebCredentialsService.AdditionalDetailsLabel, additionalDetails, false),
        };

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
    private async void EntryList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        try
        {
            if (!string.IsNullOrEmpty(_filter))
            {
                ShowError("Clear the search box before reordering.");
                await ReloadAsync();
                return;
            }
            _allEntries.Clear();
            _allEntries.AddRange(_entries);
            await _entryRepo.UpdateSortOrderAsync(_entries.Select(v => v.Id).ToList());
        }
        catch (Exception ex)
        {
            ShowError($"Could not save order: {ex.Message}");
            try { await ReloadAsync(); } catch { /* best-effort */ }
        }
    }
}

public sealed class WebEntryVm
{
    public WebEntryVm(VaultEntry e, string? number = null, string? website = null)
    {
        Id = e.Id;
        Name = e.Name;
        Subtitle = string.IsNullOrWhiteSpace(e.Owner) ? "(no owner)" : e.Owner!;
        Number = number;
        Website = website;
        // Visibility helpers — XAML can't easily collapse on null/empty without
        // a converter, and we'd rather not pull in a converter for two fields.
        HasNumber  = !string.IsNullOrWhiteSpace(number);
        HasWebsite = !string.IsNullOrWhiteSpace(website);
        NumberVisibility  = HasNumber  ? Visibility.Visible : Visibility.Collapsed;
        WebsiteVisibility = HasWebsite ? Visibility.Visible : Visibility.Collapsed;
    }
    public long Id { get; }
    public string Name { get; }
    public string Subtitle { get; }
    public string? Number { get; }
    public string? Website { get; }
    public bool HasNumber { get; }
    public bool HasWebsite { get; }
    public Visibility NumberVisibility { get; }
    public Visibility WebsiteVisibility { get; }
}
