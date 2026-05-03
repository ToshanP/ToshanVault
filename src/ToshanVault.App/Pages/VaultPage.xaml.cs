using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
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

public sealed partial class VaultPage : Page
{
    private readonly VaultEntryRepository _entryRepo = AppHost.GetService<VaultEntryRepository>();
    private readonly WebCredentialsService _credService = AppHost.GetService<WebCredentialsService>();
    private readonly AttachmentService _attachments = AppHost.GetService<AttachmentService>();
    private readonly NavigationService _nav = AppHost.GetService<NavigationService>();

    // Source of truth (unfiltered, all entries from DB) — used to recompute
    // groups whenever the search filter changes and to power the Category
    // autocomplete in the entry dialog.
    private readonly List<WebEntryVm> _allEntries = new();

    // Bound to the page's ItemsControl — one VM per visible category, in
    // display order (alphabetical with "(Uncategorised)" pinned last).
    private readonly ObservableCollection<VaultGroupVm> _groups = new();

    private readonly VaultUiStatePersister _uiState = new();
    private HashSet<string> _collapsed = new(StringComparer.OrdinalIgnoreCase);

    private string _filter = string.Empty;
    private bool _busy;

    private const string UncategorisedDisplay = "(Uncategorised)";
    private const string UncategorisedKey = "";

    public VaultPage()
    {
        InitializeComponent();
        GroupList.ItemsSource = _groups;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        try
        {
            _collapsed = _uiState.LoadCollapsedGroups();
            await ReloadAsync();
        }
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
                throw;
            }
            catch (Exception ex)
            {
                ShowError($"Could not preview entry #{r.Id}: {ex.Message}");
            }
            _allEntries.Add(new WebEntryVm(r, number, website));
        }
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var f = _filter;
        bool Matches(WebEntryVm vm) =>
            string.IsNullOrEmpty(f)
            || vm.Name.Contains(f, StringComparison.OrdinalIgnoreCase)
            || vm.Subtitle.Contains(f, StringComparison.OrdinalIgnoreCase)
            || (vm.Number  is { Length: > 0 } n && n.Contains(f, StringComparison.OrdinalIgnoreCase))
            || (vm.Website is { Length: > 0 } w && w.Contains(f, StringComparison.OrdinalIgnoreCase));

        // Group by trimmed-lowercase Category. Display name = the most common
        // casing actually used (or first-seen) so renames mid-stream don't
        // surprise the user with a different banner label.
        var groupSpecs = _allEntries
            .Where(Matches)
            .GroupBy(vm => NormalizeKey(vm.Category))
            .Select(g =>
            {
                var key = g.Key;
                var display = key == UncategorisedKey
                    ? UncategorisedDisplay
                    : (g.GroupBy(vm => vm.Category!.Trim())
                        .OrderByDescending(c => c.Count())
                        .First().Key);
                // Order entries within a group by their persisted SortOrder
                // so drag-drop reorder is honoured. SortOrder is global, but
                // since we only allow within-group drag, the relative order
                // within any one group is what the user set.
                var ordered = g.OrderBy(vm => vm.SortOrder).ThenBy(vm => vm.Id).ToList();
                return (Key: key, Display: display, Entries: ordered);
            })
            // Alphabetical groups, "(Uncategorised)" pinned last so the user
            // sees their organised buckets first and the catch-all at bottom.
            .OrderBy(g => g.Key == UncategorisedKey)
            .ThenBy(g => g.Display, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Reconcile against existing _groups so we preserve VM identity (and
        // hence Expander state) when only entry contents change. Full clear
        // would also work but causes the ItemsControl to rebuild every tile
        // visual on every keystroke in the search box.
        var existingByKey = _groups.ToDictionary(g => g.NormalizedKey, StringComparer.OrdinalIgnoreCase);
        _groups.Clear();
        foreach (var spec in groupSpecs)
        {
            if (existingByKey.TryGetValue(spec.Key, out var existing))
            {
                existing.UpdateEntries(spec.Entries);
                existing.DisplayName = spec.Display;
                _groups.Add(existing);
            }
            else
            {
                var vm = new VaultGroupVm(
                    normalizedKey: spec.Key,
                    displayName: spec.Display,
                    headerBrush: CategoryColorPalette.BrushFor(spec.Key == UncategorisedKey ? null : spec.Display),
                    isExpanded: !_collapsed.Contains(spec.Key));
                vm.UpdateEntries(spec.Entries);
                vm.ExpansionChanged += OnGroupExpansionChanged;
                _groups.Add(vm);
            }
        }
    }

    private static string NormalizeKey(string? category) =>
        string.IsNullOrWhiteSpace(category) ? UncategorisedKey : category.Trim().ToLowerInvariant();

    private void OnGroupExpansionChanged(VaultGroupVm vm)
    {
        if (vm.IsExpanded) _collapsed.Remove(vm.NormalizedKey);
        else _collapsed.Add(vm.NormalizedKey);
        _uiState.SaveCollapsedGroups(_collapsed);
    }

    private IReadOnlyList<string> KnownCategories() =>
        _allEntries
            .Select(e => e.Category)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        _filter = (sender.Text ?? string.Empty).Trim();
        ApplyFilter();
    }

    // ---- Header tap / add-in-group ----------------------------------------
    private void GroupHeader_Tapped(object sender, TappedRoutedEventArgs e)
    {
        // The header Border contains a "+ Add" Button. WinUI routed Tapped
        // bubbles from the Button up to the Border, which would otherwise
        // collapse the very group the user just clicked Add on (and the
        // dialog opens against the wrong category). Suppress the toggle in
        // that case - Click on the Button still fires.
        if (e.OriginalSource is DependencyObject src && IsInsideButton(src)) return;

        if (sender is FrameworkElement fe && fe.DataContext is VaultGroupVm vm)
        {
            vm.IsExpanded = !vm.IsExpanded;
            e.Handled = true;
        }
    }

    private static bool IsInsideButton(DependencyObject d)
    {
        for (var node = d; node is not null; node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
        {
            if (node is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase) return true;
        }
        return false;
    }

    private async void AddInGroup_Click(object sender, RoutedEventArgs e)
    {
        // Sender Tag carries the display name so we can prefill the dialog
        // with the group's category. Empty/Uncategorised → no prefill.
        var display = (sender as Button)?.Tag as string;
        var prefill = string.Equals(display, UncategorisedDisplay, StringComparison.Ordinal) ? null : display;
        await AddEntryCoreAsync(prefill);
    }

    // ---- Add ---------------------------------------------------------------
    private async void AddEntry_Click(object sender, RoutedEventArgs e) => await AddEntryCoreAsync(null);

    private async Task AddEntryCoreAsync(string? prefillCategory)
    {
        if (_busy) return; _busy = true;
        try
        {
            var dlg = new VaultEntryDialog(this.XamlRoot, null, null, null, null, null, KnownCategories());
            if (prefillCategory is not null) dlg.PrefillCategory(prefillCategory);
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

            var entry = dlg.Result!;
            var entryId = await _entryRepo.InsertAsync(entry);

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
                _attachments,
                KnownCategories());
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

    // ---- Drag & drop reordering (within-group only) -----------------------
    //
    // Each group's inner GridView calls back here when its items have been
    // reordered. We rebuild a global SortOrder by walking *all* groups in
    // their current display order and concatenating each group's current
    // entry IDs. This keeps the global ordering coherent (no cross-group
    // ID interleaving), which matters when filter is removed and SortOrder
    // is re-read on next load.
    private async void GroupGrid_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        try
        {
            if (!string.IsNullOrEmpty(_filter))
            {
                ShowError("Clear the search box before reordering.");
                await ReloadAsync();
                return;
            }

            var orderedIds = _groups.SelectMany(g => g.Entries.Select(e => e.Id)).ToList();
            await _entryRepo.UpdateSortOrderAsync(orderedIds);

            // Mirror the new order into _allEntries so the next ApplyFilter()
            // (e.g. user starts typing in search) picks up the new sort
            // without re-hitting the DB. SortOrder on the VM is updated
            // implicitly by writing through the same numeric order.
            var rank = 0;
            foreach (var g in _groups)
                foreach (var entry in g.Entries)
                    entry.SortOrder = rank++;
            // Rebuild _allEntries in the new order.
            _allEntries.Clear();
            _allEntries.AddRange(_groups.SelectMany(g => g.Entries));
        }
        catch (Exception ex)
        {
            ShowError($"Could not save order: {ex.Message}");
            try { await ReloadAsync(); } catch { /* best-effort */ }
        }
    }
}

// ---------------------------------------------------------------------------
// VM for one Category banner. INotifyPropertyChanged so the chevron glyph
// and body Visibility update in-place when the user toggles the header
// without forcing the whole ItemsControl to rebuild.
// ---------------------------------------------------------------------------
internal sealed class VaultGroupVm : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<VaultGroupVm>? ExpansionChanged;

    public string NormalizedKey { get; }
    public string DisplayName { get; set; }
    public Brush HeaderBrush { get; }
    public ObservableCollection<WebEntryVm> Entries { get; } = new();

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            Raise(nameof(IsExpanded));
            Raise(nameof(BodyVisibility));
            Raise(nameof(ChevronGlyph));
            ExpansionChanged?.Invoke(this);
        }
    }

    public Visibility BodyVisibility => _isExpanded ? Visibility.Visible : Visibility.Collapsed;
    // Segoe Fluent Icons: ChevronDown (E70D) when open, ChevronRight (E76C) when closed.
    public string ChevronGlyph => _isExpanded ? "\uE70D" : "\uE76C";
    public string CountLabel => Entries.Count == 1 ? "1 entry" : $"{Entries.Count} entries";

    public VaultGroupVm(string normalizedKey, string displayName, Brush headerBrush, bool isExpanded)
    {
        NormalizedKey = normalizedKey;
        DisplayName = displayName;
        HeaderBrush = headerBrush;
        _isExpanded = isExpanded;
    }

    public void UpdateEntries(IEnumerable<WebEntryVm> entries)
    {
        Entries.Clear();
        foreach (var e in entries) Entries.Add(e);
        Raise(nameof(CountLabel));
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class WebEntryVm
{
    public WebEntryVm(VaultEntry e, string? number = null, string? website = null)
    {
        Id = e.Id;
        Name = e.Name;
        Subtitle = string.IsNullOrWhiteSpace(e.Owner) ? "(no owner)" : e.Owner!;
        Category = e.Category;
        SortOrder = e.SortOrder;
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
    public string? Category { get; }
    public int SortOrder { get; set; }
    public string? Number { get; }
    public string? Website { get; }
    public bool HasNumber { get; }
    public bool HasWebsite { get; }
    public Visibility NumberVisibility { get; }
    public Visibility WebsiteVisibility { get; }
}
