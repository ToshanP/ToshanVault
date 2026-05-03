using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ToshanVault.Core.Models;

namespace ToshanVault_App.Pages;

/// <summary>
/// Add/edit dialog for a single retirement income or expense row. Captures
/// only Description and Annual amount; Annual is divided by 12 to populate
/// <see cref="RetirementItem.MonthlyAmountJan2025"/> so the value round-trips
/// through the existing schema without changing it. Other RetirementItem
/// fields (inflation, indexing, ages, notes) are preserved on edit and left
/// at defaults on insert.
/// </summary>
internal sealed class RetirementIncExpDialog : ContentDialog
{
    public RetirementItem? Result { get; private set; }

    private readonly TextBox _label;
    private readonly NumberBox _annual;
    private readonly TextBlock _err;
    private readonly RetirementItem _editing;
    private readonly bool _isNew;

    public RetirementIncExpDialog(XamlRoot root, RetirementKind kind, RetirementItem? existing)
    {
        XamlRoot = root;
        _isNew = existing is null;
        _editing = existing ?? new RetirementItem { Kind = kind };
        // Force kind to whatever the calling grid is (prevents an Income row
        // accidentally being moved to the Expense table on edit).
        _editing.Kind = kind;

        Title = _isNew
            ? $"Add {kind.ToString().ToLowerInvariant()} item"
            : $"Edit · {existing!.Label}";
        PrimaryButtonText = "Save";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;

        _label = new TextBox
        {
            Header = "Description",
            PlaceholderText = "e.g. Council, Redbank Plains rent",
            Text = _editing.Label,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        _annual = new NumberBox
        {
            Header = "Annual amount (AUD)",
            Minimum = 0,
            SmallChange = 100,
            LargeChange = 1000,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            // Editing pre-fills from existing monthly × 12; new dialog starts
            // at 0 so accidental Save with no input creates an obviously-wrong
            // row rather than a silently-plausible one.
            Value = _isNew ? 0 : _editing.MonthlyAmountJan2025 * 12.0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        _err = new TextBlock
        {
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
        };

        var panel = new StackPanel { Spacing = 8, Width = 420 };
        panel.Children.Add(_label);
        panel.Children.Add(_annual);
        panel.Children.Add(_err);
        Content = panel;

        PrimaryButtonClick += OnSave;
    }

    private void OnSave(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var label = (_label.Text ?? string.Empty).Trim();
        if (label.Length == 0)
        {
            _err.Text = "Description is required.";
            args.Cancel = true;
            return;
        }
        var annual = _annual.Value;
        if (double.IsNaN(annual) || annual < 0)
        {
            _err.Text = "Annual amount must be zero or positive.";
            args.Cancel = true;
            return;
        }

        _editing.Label = label;
        _editing.MonthlyAmountJan2025 = annual / 12.0;
        Result = _editing;
    }
}
