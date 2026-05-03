using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
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

public sealed partial class GeneralNotesPage : Page
{
    private readonly VaultEntryRepository _entryRepo = AppHost.GetService<VaultEntryRepository>();
    private readonly GeneralNotesService  _notes     = AppHost.GetService<GeneralNotesService>();
    private readonly AttachmentService    _attachments = AppHost.GetService<AttachmentService>();
    private readonly NavigationService    _nav       = AppHost.GetService<NavigationService>();

    private readonly List<NoteVm> _all = new();
    private readonly ObservableCollection<NoteVm> _items = new();
    private string _filter = string.Empty;
    private bool _busy;

    public GeneralNotesPage()
    {
        InitializeComponent();
        TileGrid.ItemsSource = _items;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        try { await ReloadAsync(); }
        catch (VaultLockedException) { _nav.NavigateToLogin(); }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async Task ReloadAsync()
    {
        var rows = await _entryRepo.GetByKindAsync(GeneralNotesService.EntryKind);
        _all.Clear();
        foreach (var r in rows.OrderBy(r => r.SortOrder).ThenBy(r => r.Id))
        {
            // Load body to derive a 3-line preview. General notes are typically
            // short; if this becomes a perf issue we can store a separate
            // plaintext-excerpt column or cap reads.
            string? body = null;
            try { body = await _notes.LoadBodyAsync(r.Id); }
            catch { /* best-effort preview */ }
            _all.Add(new NoteVm(r, RtfToPlainText(body)));
        }
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        _items.Clear();
        var f = _filter;
        IEnumerable<NoteVm> rows = string.IsNullOrEmpty(f)
            ? _all
            : _all.Where(n =>
                  n.Name.Contains(f, StringComparison.OrdinalIgnoreCase)
                  || (n.Owner is { Length: > 0 } o && o.Contains(f, StringComparison.OrdinalIgnoreCase))
                  || (n.Excerpt is { Length: > 0 } x && x.Contains(f, StringComparison.OrdinalIgnoreCase)));
        foreach (var n in rows) _items.Add(n);
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        _filter = (sender.Text ?? string.Empty).Trim();
        ApplyFilter();
    }

    // ---- Add ---------------------------------------------------------------
    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return; _busy = true;
        try
        {
            // Insert empty entry first so attachments can be wired on edit.
            var entry = new VaultEntry
            {
                Kind = GeneralNotesService.EntryKind,
                Name = "Untitled note",
                Owner = Enum.GetName(VaultOwner.Toshan),
            };
            // Ask the user for the basics before persisting — but we have to
            // round-trip through a dialog that allows attachments only after
            // the row exists. So show dialog first in "new" mode (no
            // attachments), then insert + reload + immediately reopen for
            // attachments only if the user wants them. For simplicity we
            // skip the reopen step: attachments become available the next
            // time the user edits the note.
            var dlg = new GeneralNoteDialog(this.XamlRoot, existing: null,
                                            initialBody: null, attachments: null);
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

            entry.Name  = dlg.NameValue ?? "Untitled note";
            entry.Owner = dlg.OwnerValue;
            var id = await _entryRepo.InsertAsync(entry);
            await _notes.SaveBodyAsync(id, dlg.BodyValue);
            await ReloadAsync();
            ShowInfo($"Added '{entry.Name}'.");
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { _busy = false; }
    }

    // ---- Edit (tile click opens notes popup) --------------------------------
    private async void Tile_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is NoteVm vm) await OpenNotesAsync(vm.Id);
    }

    private async Task OpenNotesAsync(long id)
    {
        if (_busy) return; _busy = true;
        try
        {
            var entry = await _entryRepo.GetAsync(id);
            if (entry is null) { ShowError("Note not found."); await ReloadAsync(); return; }
            var body = await _notes.LoadBodyAsync(id);

            var (saved, value) = await NotesWindow.ShowAsync(entry.Name, body);
            if (!saved) return;

            await _notes.SaveBodyAsync(id, value);
            await ReloadAsync();
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { _busy = false; }
    }

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not long id) return;
        if (_busy) return; _busy = true;
        try
        {
            var entry = await _entryRepo.GetAsync(id);
            if (entry is null) { ShowError("Note not found."); await ReloadAsync(); return; }
            var body = await _notes.LoadBodyAsync(id);
            var dlg = new GeneralNoteDialog(this.XamlRoot, entry, body, _attachments);
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
            entry.Name  = dlg.NameValue ?? entry.Name;
            entry.Owner = dlg.OwnerValue;
            await _entryRepo.UpdateAsync(entry);
            await _notes.SaveBodyAsync(id, dlg.BodyValue);
            await ReloadAsync();
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { _busy = false; }
    }

    // ---- Delete ------------------------------------------------------------
    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || sender is not Button b || b.Tag is not long id) return;
        _busy = true;
        try
        {
            var vm = _all.FirstOrDefault(n => n.Id == id);
            var name = vm?.Name ?? "this note";
            var confirm = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = $"Delete '{name}'?",
                Content = new TextBlock
                {
                    Text = "This permanently removes the note and any attached files. This cannot be undone.",
                    TextWrapping = TextWrapping.Wrap,
                },
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
            await _entryRepo.DeleteAsync(id);
            await ReloadAsync();
            ShowInfo($"Deleted '{name}'.");
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { _busy = false; }
    }

    // ---- Drag reorder -----------------------------------------------------
    private async void TileGrid_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        try
        {
            if (!string.IsNullOrEmpty(_filter))
            {
                ShowError("Clear the search box before reordering.");
                await ReloadAsync();
                return;
            }
            var orderedIds = _items.Select(n => n.Id).ToList();
            await _entryRepo.UpdateSortOrderAsync(orderedIds);
            // Mirror to _all so the next ApplyFilter preserves order.
            _all.Clear();
            _all.AddRange(_items);
        }
        catch (Exception ex)
        {
            ShowError($"Could not save order: {ex.Message}");
            try { await ReloadAsync(); } catch { /* best-effort */ }
        }
    }

    // ---- Helpers ----------------------------------------------------------
    /// <summary>
    /// Best-effort RTF → plaintext for tile preview only. Strips control
    /// words (\word), control symbols (\\), groups ({...}) and the rtf
    /// header. Not a true RTF parser; good enough to extract a few lines
    /// of text from typical user notes for tile display.
    /// </summary>
    private static string? RtfToPlainText(string? rtf)
    {
        if (string.IsNullOrEmpty(rtf)) return null;
        if (!rtf.StartsWith(@"{\rtf", StringComparison.Ordinal))
            return rtf.Length > 200 ? rtf[..200] : rtf;

        // Drop font/colour/stylesheet groups completely.
        var s = Regex.Replace(rtf, @"\{\\(?:fonttbl|colortbl|stylesheet|info|\*\\[A-Za-z]+)[^{}]*(\{[^{}]*\}[^{}]*)*\}", " ");
        // Replace \par / \line / \tab with whitespace.
        s = Regex.Replace(s, @"\\par[d]?\b|\\line\b|\\tab\b", " ");
        // Strip remaining control words (\word123 with optional numeric arg).
        s = Regex.Replace(s, @"\\[A-Za-z]+-?\d* ?", string.Empty);
        // Strip control symbols (\* \\ \{ \}).
        s = Regex.Replace(s, @"\\[^A-Za-z]", string.Empty);
        // Drop braces.
        s = s.Replace("{", string.Empty).Replace("}", string.Empty);
        // Collapse whitespace.
        s = Regex.Replace(s, @"\s+", " ").Trim();
        if (s.Length == 0) return null;
        return s.Length > 200 ? s[..200] : s;
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

internal sealed class NoteVm
{
    public long Id { get; }
    public string Name { get; }
    public string? Owner { get; }
    public string? Excerpt { get; }
    public int SortOrder { get; set; }
    public NoteVm(VaultEntry e, string? excerpt)
    {
        Id = e.Id;
        Name = e.Name;
        Owner = e.Owner;
        Excerpt = excerpt;
        SortOrder = e.SortOrder;
    }
}
