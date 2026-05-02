using System;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace ToshanVault_App.Pages;

/// <summary>
/// Reusable rich-text "notes" / "additional details" field used by every
/// dialog that wants formatting (bold/italic/underline + font family + size).
///
/// Storage contract: round-trips as a single string. If the string starts
/// with the RTF marker <c>{\rtf</c> it's loaded as RTF; otherwise it's loaded
/// as plain text (preserves backwards-compat with values written before the
/// rich editor existed). Values are always saved as RTF, so once a record is
/// edited it persists with formatting going forward.
///
/// Sibling label-row pattern matches <see cref="SecretFieldHelpers"/> — we
/// render label + toolbar in a horizontal StackPanel above the editor so it
/// reads the same as the rest of the form.
/// </summary>
internal sealed class RichNotesField
{
    private static readonly string[] FontFamilies =
        new[] { "Segoe UI", "Calibri", "Arial", "Times New Roman", "Consolas", "Verdana" };
    private static readonly double[] FontSizes =
        new double[] { 10, 11, 12, 14, 16, 18, 20, 24, 28 };
    private const string DefaultFontFamily = "Segoe UI";
    private const double DefaultFontSize = 14;

    public RichEditBox Editor { get; }
    public FrameworkElement Container { get; }

    private readonly ComboBox _familyCombo;
    private readonly ComboBox _sizeCombo;
    private bool _suppressComboHandlers;

    public RichNotesField(string labelText, string? initialValue, double minHeight = 200)
    {
        Editor = new RichEditBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = minHeight,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
        };
        ScrollViewer.SetVerticalScrollBarVisibility(Editor, ScrollBarVisibility.Auto);

        LoadValue(initialValue);

        // Default typography — applied after load so empty docs use the
        // expected look. Selection format inherits from caret position.
        Editor.Document.Selection.CharacterFormat.Name = DefaultFontFamily;
        Editor.Document.Selection.CharacterFormat.Size = (float)DefaultFontSize;

        var label = new TextBlock
        {
            Text = labelText,
            VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
        };

        var bold = MakeToggle("\uE8DD", "Bold (Ctrl+B)", () =>
            Editor.Document.Selection.CharacterFormat.Bold = FormatEffect.Toggle);
        var italic = MakeToggle("\uE8DB", "Italic (Ctrl+I)", () =>
            Editor.Document.Selection.CharacterFormat.Italic = FormatEffect.Toggle);
        var underline = MakeToggle("\uE8DC", "Underline (Ctrl+U)", () =>
        {
            // Underline takes a UnderlineType, not a FormatEffect — toggle by reading current state.
            var current = Editor.Document.Selection.CharacterFormat.Underline;
            Editor.Document.Selection.CharacterFormat.Underline =
                current == UnderlineType.None ? UnderlineType.Single : UnderlineType.None;
        });

        _familyCombo = new ComboBox { Width = 140 };
        foreach (var f in FontFamilies) _familyCombo.Items.Add(f);
        _familyCombo.SelectedItem = DefaultFontFamily;
        ToolTipService.SetToolTip(_familyCombo, "Font");
        _familyCombo.SelectionChanged += (_, _) =>
        {
            if (_suppressComboHandlers) return;
            if (_familyCombo.SelectedItem is string name)
                Editor.Document.Selection.CharacterFormat.Name = name;
        };

        _sizeCombo = new ComboBox { Width = 72 };
        foreach (var s in FontSizes) _sizeCombo.Items.Add(s);
        _sizeCombo.SelectedItem = DefaultFontSize;
        ToolTipService.SetToolTip(_sizeCombo, "Size");
        _sizeCombo.SelectionChanged += (_, _) =>
        {
            if (_suppressComboHandlers) return;
            if (_sizeCombo.SelectedItem is double size)
                Editor.Document.Selection.CharacterFormat.Size = (float)size;
        };

        // Reflect the selection's actual formatting back into the toolbar so
        // toggles light up correctly when the cursor moves through text that
        // was previously formatted.
        Editor.SelectionChanged += (_, _) =>
        {
            var fmt = Editor.Document.Selection.CharacterFormat;
            bold.IsChecked = fmt.Bold == FormatEffect.On;
            italic.IsChecked = fmt.Italic == FormatEffect.On;
            underline.IsChecked = fmt.Underline != UnderlineType.None;

            _suppressComboHandlers = true;
            try
            {
                if (!string.IsNullOrEmpty(fmt.Name) && Array.IndexOf(FontFamilies, fmt.Name) >= 0)
                    _familyCombo.SelectedItem = fmt.Name;
                var sizeMatch = Array.Find(FontSizes, s => Math.Abs(s - fmt.Size) < 0.01);
                if (sizeMatch != 0) _sizeCombo.SelectedItem = sizeMatch;
            }
            finally { _suppressComboHandlers = false; }
        };

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
        };
        toolbar.Children.Add(bold);
        toolbar.Children.Add(italic);
        toolbar.Children.Add(underline);
        toolbar.Children.Add(new AppBarSeparator { Margin = new Thickness(2, 0, 2, 0) });
        toolbar.Children.Add(_familyCombo);
        toolbar.Children.Add(_sizeCombo);
        toolbar.Children.Add(new AppBarSeparator { Margin = new Thickness(2, 0, 2, 0) });
        toolbar.Children.Add(MakeColorButton(
            "\uE8D3", "Font color",
            color => Editor.Document.Selection.CharacterFormat.ForegroundColor = color,
            includeAutomatic: true, automaticColor: Colors.Black));
        toolbar.Children.Add(MakeColorButton(
            "\uE790", "Highlight color",
            color => Editor.Document.Selection.CharacterFormat.BackgroundColor = color,
            includeAutomatic: true, automaticColor: Colors.Transparent,
            automaticLabel: "No highlight"));

        var headerRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Margin = new Thickness(0, 0, 0, 4),
        };
        headerRow.Children.Add(label);
        headerRow.Children.Add(toolbar);

        var stack = new StackPanel { Spacing = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        stack.Children.Add(headerRow);
        stack.Children.Add(Editor);
        Container = stack;
    }

    /// <summary>
    /// Returns the current document as RTF. If the editor is empty, returns
    /// null so callers can keep the underlying column NULL rather than store
    /// an empty RTF skeleton.
    /// </summary>
    public string? GetValue()
    {
        Editor.Document.GetText(TextGetOptions.None, out var plain);
        if (string.IsNullOrWhiteSpace(plain)) return null;
        Editor.Document.GetText(TextGetOptions.FormatRtf, out var rtf);
        return rtf;
    }

    private void LoadValue(string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        try
        {
            // RTF documents always start with "{\rtf" (per the RTF spec). Anything
            // else is legacy plain text written before the rich editor existed.
            if (value.StartsWith(@"{\rtf", StringComparison.Ordinal))
                Editor.Document.SetText(TextSetOptions.FormatRtf, value);
            else
                Editor.Document.SetText(TextSetOptions.None, value);
        }
        catch
        {
            // Corrupt RTF — fall back to plain text load so the user can see
            // and recover their content rather than facing a blank editor.
            Editor.Document.SetText(TextSetOptions.None, value);
        }
    }

    private static ToggleButton MakeToggle(string glyph, string tooltip, Action onClick)
    {
        var btn = new ToggleButton
        {
            Content = new FontIcon { Glyph = glyph, FontSize = 14 },
            Padding = new Thickness(8, 2, 8, 2),
            MinWidth = 36, MinHeight = 32,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(btn, tooltip);
        btn.Click += (_, _) => onClick();
        return btn;
    }

    // Preset palette — Office-ish set kept short so the flyout stays compact.
    // Hex order: black, white/auto, dark red, red, orange, yellow, green,
    // teal, blue, purple. Highlight defaults are typically lighter; we still
    // expose the same set to keep the picker simple.
    private static readonly (string Name, Color Color)[] PaletteColors =
    {
        ("Black",      Color.FromArgb(255, 0,   0,   0)),
        ("Dark grey",  Color.FromArgb(255, 89,  89,  89)),
        ("Grey",       Color.FromArgb(255, 165, 165, 165)),
        ("White",      Color.FromArgb(255, 255, 255, 255)),
        ("Dark red",   Color.FromArgb(255, 192, 0,   0)),
        ("Red",        Color.FromArgb(255, 255, 0,   0)),
        ("Orange",     Color.FromArgb(255, 255, 153, 0)),
        ("Yellow",     Color.FromArgb(255, 255, 255, 0)),
        ("Light green",Color.FromArgb(255, 146, 208, 80)),
        ("Green",      Color.FromArgb(255, 0,   176, 80)),
        ("Teal",       Color.FromArgb(255, 0,   176, 240)),
        ("Blue",       Color.FromArgb(255, 0,   112, 192)),
        ("Dark blue",  Color.FromArgb(255, 0,   32,  96)),
        ("Purple",     Color.FromArgb(255, 112, 48,  160)),
    };

    /// <summary>Toolbar button that opens a small palette flyout. The chosen
    /// color is applied to the current selection via <paramref name="apply"/>.
    /// "Automatic" is a sentinel that resets to the supplied
    /// <paramref name="automaticColor"/> (used as the document default).</summary>
    private static Button MakeColorButton(
        string glyph,
        string tooltip,
        Action<Color> apply,
        bool includeAutomatic,
        Color automaticColor,
        string automaticLabel = "Automatic")
    {
        var btn = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = 14 },
            Padding = new Thickness(8, 2, 8, 2),
            MinWidth = 36, MinHeight = 32,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(btn, tooltip);

        var grid = new Grid { Margin = new Thickness(4) };
        const int cols = 7;
        for (var c = 0; c < cols; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var rows = (int)Math.Ceiling((double)PaletteColors.Length / cols);
        for (var r = 0; r < rows + (includeAutomatic ? 1 : 0); r++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var flyout = new Flyout();

        for (var i = 0; i < PaletteColors.Length; i++)
        {
            var (name, color) = PaletteColors[i];
            var swatch = new Button
            {
                Width = 24, Height = 24,
                Margin = new Thickness(2),
                Padding = new Thickness(0),
                Background = new SolidColorBrush(color),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 96, 96, 96)),
                BorderThickness = new Thickness(1),
            };
            ToolTipService.SetToolTip(swatch, name);
            var capturedColor = color;
            swatch.Click += (_, _) => { apply(capturedColor); flyout.Hide(); };
            Grid.SetRow(swatch, i / cols);
            Grid.SetColumn(swatch, i % cols);
            grid.Children.Add(swatch);
        }

        if (includeAutomatic)
        {
            var auto = new Button
            {
                Content = automaticLabel,
                Margin = new Thickness(2, 4, 2, 2),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            auto.Click += (_, _) => { apply(automaticColor); flyout.Hide(); };
            Grid.SetRow(auto, rows);
            Grid.SetColumnSpan(auto, cols);
            grid.Children.Add(auto);
        }

        flyout.Content = grid;
        btn.Flyout = flyout;
        return btn;
    }
}
