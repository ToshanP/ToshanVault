using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.WinUI.UI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using ToshanVault.Core.Models;
using ToshanVault.Data.Repositories;
using ToshanVault.Importer;
using ToshanVault_App.Hosting;
using ToshanVault_App.Services;
using Windows.Storage.Pickers;

namespace ToshanVault_App.Pages;

public sealed partial class RecipesPage : Page
{
    private readonly RecipeRepository _repo = AppHost.GetService<RecipeRepository>();
    private readonly NavigationService _nav = AppHost.GetService<NavigationService>();

    private readonly ObservableCollection<Recipe> _items = new();
    private readonly List<Recipe> _all = new();
    private string _filter = string.Empty;
    private bool _busy;
    private string? _sortKey;
    private DataGridSortDirection? _sortDir;

    public RecipesPage()
    {
        InitializeComponent();
        Grid.ItemsSource = _items;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        try { await ReloadAsync(); }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async Task ReloadAsync()
    {
        var rows = await _repo.GetAllAsync();
        _all.Clear();
        _all.AddRange(rows);
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        _items.Clear();
        var f = _filter;
        IEnumerable<Recipe> filtered = _all.Where(r =>
            string.IsNullOrEmpty(f)
            || r.Title.Contains(f, StringComparison.OrdinalIgnoreCase)
            || r.Category.Contains(f, StringComparison.OrdinalIgnoreCase)
            || (r.Author     is { Length: > 0 } a && a.Contains(f, StringComparison.OrdinalIgnoreCase))
            || (r.YoutubeUrl is { Length: > 0 } u && u.Contains(f, StringComparison.OrdinalIgnoreCase)));

        filtered = ApplySort(filtered);

        foreach (var r in filtered) _items.Add(r);
    }

    private IEnumerable<Recipe> ApplySort(IEnumerable<Recipe> rows)
    {
        // Tried recipes always pinned to the top regardless of any column
        // sort the user picks; the chosen column (if any) becomes the
        // secondary key within each tried/untried group.
        var pinned = rows.OrderByDescending(r => r.IsTried);
        if (_sortKey is null || _sortDir is null) return pinned;
        var asc = _sortDir == DataGridSortDirection.Ascending;
        Func<Recipe, IComparable?> key = _sortKey switch
        {
            "IsTried"     => r => r.IsTried,
            "Category"    => r => r.Category ?? string.Empty,
            "Title"       => r => r.Title    ?? string.Empty,
            "Author"      => r => r.Author   ?? string.Empty,
            "YoutubeUrl"  => r => r.YoutubeUrl ?? string.Empty,
            "IsFavourite" => r => r.IsFavourite,
            _             => _ => 0,
        };
        return asc ? pinned.ThenBy(key) : pinned.ThenByDescending(key);
    }

    private void Grid_Sorting(object? sender, DataGridColumnEventArgs e)
    {
        var key = e.Column.Tag as string;
        if (string.IsNullOrEmpty(key)) return;

        // Toggle: same column flips direction; new column starts ascending.
        var newDir = (_sortKey == key && _sortDir == DataGridSortDirection.Ascending)
            ? DataGridSortDirection.Descending
            : DataGridSortDirection.Ascending;

        _sortKey = key;
        _sortDir = newDir;

        foreach (var col in Grid.Columns)
            col.SortDirection = ReferenceEquals(col, e.Column) ? newDir : null;

        ApplyFilter();
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        _filter = (sender.Text ?? string.Empty).Trim();
        ApplyFilter();
    }

    private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var has = Grid.SelectedItem is Recipe;
        EditButton.IsEnabled = has;
        DeleteButton.IsEnabled = has;
    }

    private async void Grid_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (Grid.SelectedItem is Recipe r) await EditAsync(r);
    }

    private void Grid_LoadingRow(object? sender, DataGridRowEventArgs e)
    {
        // Highlight rows the user has marked tried — mirrors the yellow
        // background they used in the source spreadsheet. Resetting on
        // every LoadingRow is required because rows are recycled.
        if (e.Row.DataContext is Recipe r && r.IsTried)
        {
            e.Row.Background = (Brush)Application.Current.Resources["SystemFillColorSuccessBackgroundBrush"];
        }
        else
        {
            e.Row.Background = null;
        }
    }

    // ---- Add ---------------------------------------------------------------
    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return; _busy = true;
        try
        {
            var dlg = new RecipeDialog(this.XamlRoot, null);
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
            await _repo.InsertAsync(dlg.Result!);
            await ReloadAsync();
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { _busy = false; }
    }

    // ---- Edit --------------------------------------------------------------
    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is Recipe r) await EditAsync(r);
    }

    private async Task EditAsync(Recipe r)
    {
        if (_busy) return; _busy = true;
        try
        {
            var fresh = await _repo.GetAsync(r.Id);
            if (fresh is null) { ShowError("Recipe not found."); await ReloadAsync(); return; }

            var dlg = new RecipeDialog(this.XamlRoot, fresh);
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
            await _repo.UpdateAsync(dlg.Result!);
            await ReloadAsync();
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { _busy = false; }
    }

    // ---- Delete ------------------------------------------------------------
    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || Grid.SelectedItem is not Recipe r) return;
        _busy = true;
        try
        {
            var confirm = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = $"Delete '{r.Title}'?",
                Content = new TextBlock
                {
                    Text = "This permanently removes the recipe row. This cannot be undone.",
                    TextWrapping = TextWrapping.Wrap,
                },
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

            await _repo.DeleteAsync(r.Id);
            await ReloadAsync();
            ShowInfo($"Deleted '{r.Title}'.");
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { _busy = false; }
    }

    // ---- Import xlsx -------------------------------------------------------
    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return; _busy = true;
        try
        {
            var picker = new FileOpenPicker();
            // WinUI 3 desktop requires the picker to be initialised with the
            // owning window's HWND.
            WinRT.Interop.InitializeWithWindow.Initialize(picker, MainWindow.Hwnd);
            picker.FileTypeFilter.Add(".xlsx");
            picker.SuggestedStartLocation = PickerLocationId.Desktop;

            // Default to the project's Toshan.xlsx if it sits next to the data
            // dir (common dev layout) — saves a click. Use ContinuationFile or
            // SettingsIdentifier to remember last pick across runs.
            var defaultGuess = Path.Combine(@"C:\Toshan\Retirement Plan", "Toshan.xlsx");

            var file = await picker.PickSingleFileAsync();
            if (file is null && File.Exists(defaultGuess))
            {
                // User cancelled but we have a known-good fallback: ask once.
                var ask = new ContentDialog
                {
                    XamlRoot = this.XamlRoot,
                    Title = "Use Toshan.xlsx?",
                    Content = $"Import recipes from:\n{defaultGuess}",
                    PrimaryButtonText = "Import",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Primary,
                };
                if (await ask.ShowAsync() != ContentDialogResult.Primary) return;
                await DoImportAsync(defaultGuess);
                return;
            }
            if (file is null) return;
            await DoImportAsync(file.Path);
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { _busy = false; }
    }

    private async Task DoImportAsync(string path)
    {
        var importer = new RecipesImporter(_repo);
        var report = await importer.ImportAsync(path);
        await ReloadAsync();
        ShowInfo($"Read {report.RowsRead} rows · inserted {report.Inserted} · skipped {report.Skipped} duplicates.");
    }

    // ---- InfoBar helpers ---------------------------------------------------
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
