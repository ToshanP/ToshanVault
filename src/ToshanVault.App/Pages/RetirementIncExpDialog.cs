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
    private readonly bool _isIncomeWeekly;

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
            PlaceholderText = kind == RetirementKind.Income
                ? "e.g. Redbank Plains, Pimpama"
                : "e.g. Council, Health Insurance",
            Text = _editing.Label,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        // Income is captured as weekly rent (52 weeks/year is the convention
        // already used for the totals row); Expense is captured as annual.
        // In both cases we round-trip through monthly_amount_jan2025 in the
        // schema, so no migration is needed.
        var isIncome = kind == RetirementKind.Income;
        var annual   = _editing.MonthlyAmountJan2025 * 12.0;
        _annual = new NumberBox
        {
            Header = isIncome ? "Weekly rent (AUD)" : "Annual amount (AUD)",
            Minimum = 0,
            SmallChange = isIncome ? 10 : 100,
            LargeChange = isIncome ? 100 : 1000,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            Value = _isNew ? 0 : (isIncome ? annual / 52.0 : annual),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        _isIncomeWeekly = isIncome;

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
        var entered = _annual.Value;
        if (double.IsNaN(entered) || entered < 0)
        {
            _err.Text = "Amount must be zero or positive.";
            args.Cancel = true;
            return;
        }

        // Convert the user-entered value into the monthly storage column.
        // Income captures weekly → annual = weekly × 52 → monthly = annual/12.
        // Expense captures annual → monthly = annual/12.
        var annual = _isIncomeWeekly ? entered * 52.0 : entered;
        _editing.Label = label;
        _editing.MonthlyAmountJan2025 = annual / 12.0;
        Result = _editing;
    }
}
