using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ToshanVault.Core.Models;
using ToshanVault.Importer;

namespace ToshanVault_App.Pages;

/// <summary>
/// Add/Edit dialog for a <see cref="GoldItem"/>. Description is required.
/// Tola drives Grams (read-only display, recalculated on every keystroke).
/// Purity defaults to 22K for Indian jewellery; "Diamond" is selectable for
/// stones (estimated value formula treats "Diamond" as zero gold content).
/// </summary>
internal sealed class GoldOrnamentDialog : ContentDialog
{
    private static readonly string[] Purities = { "24K", "22K", "18K", "14K", "10K", "Diamond" };

    public GoldItem? Result { get; private set; }

    private readonly GoldItem? _existing;
    private readonly TextBox _name, _notes;
    private readonly NumberBox _qty, _tola;
    private readonly ComboBox _purity;
    private readonly TextBlock _grams, _err;

    public GoldOrnamentDialog(XamlRoot root, GoldItem? existing)
    {
        XamlRoot = root;
        _existing = existing;
        Title = existing is null ? "Add gold ornament" : $"Edit · {existing.ItemName}";
        PrimaryButtonText = "Save";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;

        this.Resources["ContentDialogMaxWidth"] = 720d;
        this.Resources["ContentDialogMinWidth"] = 560d;

        _name = new TextBox
        {
            Header = "Description",
            Text = existing?.ItemName ?? string.Empty,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        _qty = new NumberBox
        {
            Header = "Quantity",
            Value = existing?.Qty ?? 1,
            Minimum = 0,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        };

        _tola = new NumberBox
        {
            Header = "Tola (1 tola = 11.6638 g)",
            Value = existing?.Tola ?? 0,
            Minimum = 0,
            SmallChange = 0.1,
            LargeChange = 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        };
        _tola.ValueChanged += (_, _) => RefreshGrams();

        _purity = new ComboBox
        {
            Header = "Purity",
            ItemsSource = Purities,
            SelectedItem = NormalisePurity(existing?.Purity) ?? "22K",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        _grams = new TextBlock();
        _notes = new TextBox
        {
            Header = "Notes",
            Text = existing?.Notes ?? string.Empty,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 80,
        };

        _err = new TextBlock
        {
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
        };

        var qtyTolaRow = new Grid { ColumnSpacing = 8 };
        qtyTolaRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        qtyTolaRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        qtyTolaRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(_qty, 0);
        Grid.SetColumn(_tola, 1);
        Grid.SetColumn(_purity, 2);
        qtyTolaRow.Children.Add(_qty);
        qtyTolaRow.Children.Add(_tola);
        qtyTolaRow.Children.Add(_purity);

        var panel = new StackPanel { Spacing = 10, Width = 560 };
        panel.Children.Add(_name);
        panel.Children.Add(qtyTolaRow);
        panel.Children.Add(_grams);
        panel.Children.Add(_notes);
        panel.Children.Add(_err);
        Content = panel;

        RefreshGrams();
        PrimaryButtonClick += OnSave;
    }

    private void RefreshGrams()
    {
        var grams = GoldImporter.TolaToGrams(_tola.Value);
        _grams.Text = $"≈ {grams:0.000} g";
    }

    private static string? NormalisePurity(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var t = raw.Trim();
        return Array.Exists(Purities, p => string.Equals(p, t, StringComparison.OrdinalIgnoreCase))
            ? Array.Find(Purities, p => string.Equals(p, t, StringComparison.OrdinalIgnoreCase))
            : t; // unknown value: keep as-is so it appears as combobox text (won't match list)
    }

    private void OnSave(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var name = _name.Text.Trim();
        if (name.Length == 0) { _err.Text = "Description is required."; args.Cancel = true; return; }
        var purity = (_purity.SelectedItem as string) ?? "22K";

        Result = _existing ?? new GoldItem();
        Result.ItemName  = name;
        Result.Purity    = purity;
        Result.Qty       = _qty.Value;
        Result.Tola      = _tola.Value;
        Result.GramsCalc = GoldImporter.TolaToGrams(_tola.Value);
        var notes = _notes.Text.Trim();
        Result.Notes = notes.Length == 0 ? null : notes;
    }
}
