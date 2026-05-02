using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace ToshanVault_App.Pages;

/// <summary>
/// Shared label-row + sticky-reveal eye toggle helper used by every dialog
/// that exposes a PasswordBox (bank credentials, vault web logins, etc).
///
/// Why a sibling row instead of <c>PasswordBox.Header</c>: WinUI's default
/// header template strips/hides non-text content placed in <c>Header</c>, so
/// the toggle was invisible. Putting label + toggle in a horizontal
/// StackPanel above the box keeps them clearly co-located, even when the
/// dialog hosts a vertical scroll bar.
/// </summary>
internal static class SecretFieldHelpers
{
    /// <summary>
    /// Append a label-row (caption + reveal-eye toggle) followed by a
    /// PasswordBox to <paramref name="parent"/>. Returns the PasswordBox so the
    /// caller can read its <c>Password</c> on Save. Default state: hidden;
    /// click eye to reveal, click again or move focus away to re-mask.
    /// </summary>
    public static PasswordBox AddSecret(Panel parent, string labelText, string initialValue)
    {
        var box = new PasswordBox
        {
            Password = initialValue,
            PasswordRevealMode = PasswordRevealMode.Hidden,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var label = new TextBlock
        {
            Text = labelText,
            VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
        };

        var toggle = new ToggleButton
        {
            Content = new FontIcon { Glyph = "\uE7B3", FontSize = 14 }, // RedEye
            Padding = new Thickness(8, 2, 8, 2),
            MinWidth = 36, MinHeight = 28,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(toggle, "Reveal value");
        toggle.Checked   += (_, _) => box.PasswordRevealMode = PasswordRevealMode.Visible;
        toggle.Unchecked += (_, _) => box.PasswordRevealMode = PasswordRevealMode.Hidden;
        box.LostFocus += (_, _) =>
        {
            if (toggle.IsChecked == true) toggle.IsChecked = false;
        };

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 0, 0, 2),
        };
        row.Children.Add(label);
        row.Children.Add(toggle);

        parent.Children.Add(row);
        parent.Children.Add(box);
        return box;
    }
}
