using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ToshanVault.Core.Models;

namespace ToshanVault_App.Pages;

/// <summary>
/// Add/edit dialog for a single <see cref="BudgetItem"/>. The category and
/// type (Income / Fixed / Variable) are decided by the calling grid and are
/// not editable here — moving an item between buckets would just be a
/// delete-and-re-add. Captures Description, Amount, Frequency, optional Notes.
/// </summary>
internal sealed class BudgetItemDialog : ContentDialog
{
    public BudgetItem? Result { get; private set; }

    private readonly TextBox _label;
    private readonly NumberBox _amount;
    private readonly ComboBox _frequency;
    private readonly TextBox _notes;
    private readonly TextBlock _err;
    private readonly BudgetItem _editing;
    private readonly bool _isNew;

    public BudgetItemDialog(XamlRoot root, BudgetCategoryType type, long categoryId, BudgetItem? existing)
    {
        XamlRoot = root;
        _isNew = existing is null;
        _editing = existing ?? new BudgetItem
        {
            CategoryId = categoryId,
            Frequency = BudgetFrequency.Monthly,
        };

        Title = _isNew
            ? $"Add {TypeLabel(type).ToLowerInvariant()} item"
            : $"Edit · {existing!.Label}";
        PrimaryButtonText = "Save";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;

        _label = new TextBox
        {
            Header = "Description",
            PlaceholderText = type switch
            {
                BudgetCategoryType.Income   => "e.g. Salary, Rental income",
                BudgetCategoryType.Fixed    => "e.g. Mortgage, Council, Insurance",
                _                            => "e.g. Groceries, Petrol, Eating out",
            },
            Text = _editing.Label,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        _amount = new NumberBox
        {
            Header = "Amount (AUD)",
            Minimum = 0,
            SmallChange = 10,
            LargeChange = 100,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            Value = _isNew ? 0 : _editing.Amount,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        // OneOff is omitted from the dropdown because the page totals are a
        // *recurring* weekly/annual cashflow; one-off events don't belong on a
        // running budget. The enum value is kept in the schema so legacy or
        // imported rows don't fail validation.
        _frequency = new ComboBox
        {
            Header = "Frequency",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[]
            {
                BudgetFrequency.Weekly,
                BudgetFrequency.Fortnightly,
                BudgetFrequency.Monthly,
                BudgetFrequency.Quarterly,
                BudgetFrequency.Yearly,
            },
            SelectedItem = _editing.Frequency == BudgetFrequency.OneOff
                ? BudgetFrequency.Monthly
                : _editing.Frequency,
        };

        _notes = new TextBox
        {
            Header = "Notes (optional)",
            PlaceholderText = "Any context — biller, due date, etc.",
            Text = _editing.Notes ?? string.Empty,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 70,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        _err = new TextBlock
        {
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
        };

        var panel = new StackPanel { Spacing = 8, Width = 420 };
        panel.Children.Add(_label);
        panel.Children.Add(_amount);
        panel.Children.Add(_frequency);
        panel.Children.Add(_notes);
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
        if (double.IsNaN(_amount.Value) || _amount.Value < 0)
        {
            _err.Text = "Amount must be zero or positive.";
            args.Cancel = true;
            return;
        }
        if (_frequency.SelectedItem is not BudgetFrequency freq)
        {
            _err.Text = "Pick a frequency.";
            args.Cancel = true;
            return;
        }

        _editing.Label = label;
        _editing.Amount = _amount.Value;
        _editing.Frequency = freq;
        var n = (_notes.Text ?? string.Empty).Trim();
        _editing.Notes = n.Length == 0 ? null : n;
        Result = _editing;
    }

    private static string TypeLabel(BudgetCategoryType t) => t switch
    {
        BudgetCategoryType.Income   => "Income",
        BudgetCategoryType.Fixed    => "Fixed expense",
        BudgetCategoryType.Variable => "Variable expense",
        _                            => "Item",
    };
}
