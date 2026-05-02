using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
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

public sealed partial class GoldOrnamentsPage : Page
{
    private readonly GoldItemRepository _repo  = AppHost.GetService<GoldItemRepository>();
    private readonly GoldPriceService   _price = AppHost.GetService<GoldPriceService>();

    private readonly ObservableCollection<GoldRowVm> _items = new();
    private readonly List<GoldItem> _all = new();
    private string _filter = string.Empty;
    private bool _busy;
    private GoldPriceCache? _currentPrice;

    public GoldOrnamentsPage()
    {
        InitializeComponent();
        Grid.ItemsSource = _items;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        try
        {
            // Use any cached price (no network) on first load.
            _currentPrice = await _price.GetAsync(forceRefresh: false);
            UpdatePriceBanner();
            await ReloadAsync();
        }
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
        var ppg = _currentPrice?.PricePerGram24k ?? 0;
        double totalGrams = 0, totalAud = 0;
        foreach (var g in _all)
        {
            if (!string.IsNullOrEmpty(f)
                && !g.ItemName.Contains(f, StringComparison.OrdinalIgnoreCase)
                && !((g.Purity ?? "").Contains(f, StringComparison.OrdinalIgnoreCase))
                && !((g.Notes  ?? "").Contains(f, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            var value = GoldValueCalculator.EstimateValue(g.GramsCalc, g.Purity, ppg);
            totalGrams += g.GramsCalc;
            totalAud += value;
            _items.Add(new GoldRowVm(g, value));
        }
        TotalsBanner.Text = ppg > 0
            ? $"{_items.Count} items · {totalGrams:0.0} g total · est. AUD {totalAud:N0}"
            : $"{_items.Count} items · {totalGrams:0.0} g total · price unavailable";
    }

    private void UpdatePriceBanner()
    {
        if (_currentPrice is null || _currentPrice.PricePerGram24k <= 0)
        {
            PriceBanner.Text = "Price not loaded yet — click ‘Refresh price’ to fetch live AUD/g (24K).";
            return;
        }
        var ageMin = (DateTimeOffset.UtcNow - _currentPrice.FetchedAt).TotalMinutes;
        var ageText = ageMin < 1 ? "just now"
                     : ageMin < 60 ? $"{(int)ageMin} min ago"
                     : $"{(int)(ageMin / 60)} h ago";
        PriceBanner.Text =
            $"Live gold (24K): AUD {_currentPrice.PricePerGram24k:N2} / g · fetched {ageText}.";
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        _filter = (sender.Text ?? string.Empty).Trim();
        ApplyFilter();
    }

    private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var has = Grid.SelectedItem is GoldRowVm;
        EditButton.IsEnabled = has;
        DeleteButton.IsEnabled = has;
    }

    private async void Grid_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (Grid.SelectedItem is GoldRowVm vm) await EditAsync(vm.Source);
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return; _busy = true;
        try
        {
            var dlg = new GoldOrnamentDialog(this.XamlRoot, null);
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
            await _repo.InsertAsync(dlg.Result!);
            await ReloadAsync();
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { _busy = false; }
    }

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is GoldRowVm vm) await EditAsync(vm.Source);
    }

    private async Task EditAsync(GoldItem g)
    {
        if (_busy) return; _busy = true;
        try
        {
            var fresh = await _repo.GetAsync(g.Id);
            if (fresh is null) { ShowError("Item not found."); await ReloadAsync(); return; }
            var dlg = new GoldOrnamentDialog(this.XamlRoot, fresh);
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
            await _repo.UpdateAsync(dlg.Result!);
            await ReloadAsync();
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { _busy = false; }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || Grid.SelectedItem is not GoldRowVm vm) return;
        _busy = true;
        try
        {
            var confirm = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = $"Delete '{vm.Description}'?",
                Content = new TextBlock
                {
                    Text = "This permanently removes the item. This cannot be undone.",
                    TextWrapping = TextWrapping.Wrap,
                },
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
            await _repo.DeleteAsync(vm.Source.Id);
            await ReloadAsync();
            ShowInfo($"Deleted '{vm.Description}'.");
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { _busy = false; }
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return; _busy = true;
        try
        {
            var picker = new FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, MainWindow.Hwnd);
            picker.FileTypeFilter.Add(".xlsx");
            picker.SuggestedStartLocation = PickerLocationId.Desktop;

            var defaultGuess = Path.Combine(@"C:\Toshan\Retirement Plan", "Toshan.xlsx");

            var file = await picker.PickSingleFileAsync();
            if (file is null && File.Exists(defaultGuess))
            {
                var ask = new ContentDialog
                {
                    XamlRoot = this.XamlRoot,
                    Title = "Use Toshan.xlsx?",
                    Content = $"Import gold ornaments from:\n{defaultGuess}",
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
        var importer = new GoldImporter(_repo);
        var report = await importer.ImportAsync(path);
        await ReloadAsync();
        ShowInfo($"Read {report.RowsRead} rows · inserted {report.Inserted} · skipped {report.Skipped} duplicates.");
    }

    private async void RefreshPrice_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return; _busy = true;
        try
        {
            RefreshPriceButton.IsEnabled = false;
            _currentPrice = await _price.GetAsync(forceRefresh: true);
            UpdatePriceBanner();
            ApplyFilter();
            if (_currentPrice is null)
                ShowError("Could not fetch live gold price (network unavailable).");
            else
                ShowInfo($"Updated · AUD {_currentPrice.PricePerGram24k:N2} per gram (24K).");
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { _busy = false; RefreshPriceButton.IsEnabled = true; }
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

    /// <summary>
    /// View-model wrapper around a <see cref="GoldItem"/> that pre-formats the
    /// numeric columns and folds in the live estimated value. Kept private so
    /// the page binding doesn't have to reach into the domain model directly.
    /// </summary>
    public sealed class GoldRowVm
    {
        public GoldItem Source { get; }
        public string Description => Source.ItemName;
        public string Purity      => Source.Purity ?? string.Empty;
        public string? Notes      => Source.Notes;
        public string QtyText     => Source.Qty.ToString("0.##", CultureInfo.CurrentCulture);
        public string TolaText    => Source.Tola.ToString("0.###", CultureInfo.CurrentCulture);
        public string GramsText   => Source.GramsCalc.ToString("0.000", CultureInfo.CurrentCulture);
        public string ValueText   { get; }

        public GoldRowVm(GoldItem source, double estimatedAud)
        {
            Source = source;
            ValueText = estimatedAud > 0
                ? $"AUD {estimatedAud:N0}"
                : "—";
        }
    }
}
