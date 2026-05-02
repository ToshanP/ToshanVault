using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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
        foreach (var r in _all)
        {
            if (string.IsNullOrEmpty(f)
                || r.Title.Contains(f, StringComparison.OrdinalIgnoreCase)
                || (r.Author     is { Length: > 0 } a && a.Contains(f, StringComparison.OrdinalIgnoreCase))
                || (r.YoutubeUrl is { Length: > 0 } u && u.Contains(f, StringComparison.OrdinalIgnoreCase)))
            {
                _items.Add(r);
            }
        }
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
