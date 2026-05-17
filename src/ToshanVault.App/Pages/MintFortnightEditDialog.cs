using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ToshanVault_App.Pages;

/// <summary>
/// Edit dialog for a single fortnight row.
/// Captures Contribution and Purchase Oz; forward propagation happens in the caller.
/// </summary>
internal sealed class MintFortnightEditDialog : ContentDialog
{
    public double ResultContribution { get; private set; }
    public double ResultPurchaseOz { get; private set; }

    private readonly NumberBox _contribution;
    private readonly NumberBox _purchaseOz;
    private readonly TextBlock _err;

    public MintFortnightEditDialog(XamlRoot root, FortnightVm vm)
    {
        XamlRoot = root;
        Title = $"Edit · {vm.DateDisplay}";
        PrimaryButtonText = "Save";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;

        _contribution = new NumberBox
        {
            Header = "Contribution (AUD)",
            Minimum = 0,
            SmallChange = 50,
            LargeChange = 500,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            Value = vm.Contribution,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        _purchaseOz = new NumberBox
        {
            Header = "Purchase Oz",
            Minimum = 0,
            SmallChange = 0.5,
            LargeChange = 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            Value = vm.PurchaseOz,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        _err = new TextBlock
        {
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
        };

        var panel = new StackPanel { Spacing = 12, Width = 360 };
        panel.Children.Add(_contribution);
        panel.Children.Add(_purchaseOz);
        panel.Children.Add(_err);
        Content = panel;

        PrimaryButtonClick += OnSave;

        Loaded += (_, _) =>
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () => _contribution.Focus(FocusState.Programmatic));
        };
    }

    private void OnSave(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var contrib = _contribution.Value;
        var oz = _purchaseOz.Value;

        if (double.IsNaN(contrib) || contrib < 0)
        {
            _err.Text = "Contribution must be zero or positive.";
            args.Cancel = true;
            return;
        }
        if (double.IsNaN(oz) || oz < 0)
        {
            _err.Text = "Purchase Oz must be zero or positive.";
            args.Cancel = true;
            return;
        }

        ResultContribution = contrib;
        ResultPurchaseOz = oz;
    }
}
