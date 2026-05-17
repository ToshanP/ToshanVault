using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ToshanVault_App.Pages;

/// <summary>
/// Edit dialog for a single Mint yearly balance row.
/// Captures Actual Oz and Actual Invested; other fields are read-only projections.
/// Pattern matches <see cref="RetirementIncExpDialog"/>.
/// </summary>
internal sealed class MintYearlyBalanceDialog : ContentDialog
{
    public double ResultActualOz { get; private set; }
    public double ResultActualInvested { get; private set; }
    public bool Saved { get; private set; }

    private readonly NumberBox _actualOz;
    private readonly NumberBox _actualInvested;
    private readonly TextBlock _err;

    public MintYearlyBalanceDialog(XamlRoot root, YearlyBalanceVm vm)
    {
        XamlRoot = root;
        Title = $"Edit · {vm.YearLabel}";
        PrimaryButtonText = "Save";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;

        _actualOz = new NumberBox
        {
            Header = "Actual Oz",
            Minimum = 0,
            SmallChange = 0.5,
            LargeChange = 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            Value = vm.ActualOz,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        _actualInvested = new NumberBox
        {
            Header = "Actual Invested (AUD)",
            Minimum = 0,
            SmallChange = 500,
            LargeChange = 5000,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            Value = vm.ActualInvested,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        _err = new TextBlock
        {
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
        };

        var panel = new StackPanel { Spacing = 12, Width = 360 };
        panel.Children.Add(_actualOz);
        panel.Children.Add(_actualInvested);
        panel.Children.Add(_err);
        Content = panel;

        PrimaryButtonClick += OnSave;

        Loaded += (_, _) =>
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () => _actualOz.Focus(FocusState.Programmatic));
        };
    }

    private void OnSave(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var oz = _actualOz.Value;
        var invested = _actualInvested.Value;

        if (double.IsNaN(oz) || oz < 0)
        {
            _err.Text = "Actual Oz must be zero or positive.";
            args.Cancel = true;
            return;
        }
        if (double.IsNaN(invested) || invested < 0)
        {
            _err.Text = "Actual Invested must be zero or positive.";
            args.Cancel = true;
            return;
        }

        ResultActualOz = oz;
        ResultActualInvested = invested;
        Saved = true;
    }
}
