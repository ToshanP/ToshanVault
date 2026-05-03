using System;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace ToshanVault_App.Pages;

/// <summary>
/// A popup window hosting a full-screen rich-text notes editor.
/// Occupies ~90% of the primary screen area and is modal-ish (the main
/// window stays behind but the user naturally focuses on this window).
///
/// Usage:
/// <code>
/// var (saved, value) = await NotesWindow.ShowAsync("Bank - ANZ", existingRtf);
/// if (saved) { /* persist value */ }
/// </code>
/// </summary>
public sealed partial class NotesWindow : Window
{
    private readonly TaskCompletionSource<(bool Saved, string? Value)> _tcs = new();
    private RichNotesField? _notes;

    private NotesWindow(string title, string? initialValue)
    {
        InitializeComponent();

        TitleText.Text = title;

        _notes = new RichNotesField(string.Empty, initialValue, minHeight: 400);

        // The RichNotesField.Container is a StackPanel (toolbar + editor) which
        // doesn't stretch vertically. Restructure into a Grid so the editor
        // fills the full available height of the window.
        var stack = (Microsoft.UI.Xaml.Controls.StackPanel)_notes.Container;
        var header = stack.Children[0];
        var editor = stack.Children[1];
        stack.Children.Clear();

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        Grid.SetRow((FrameworkElement)header, 0);
        Grid.SetRow((FrameworkElement)editor, 1);
        ((FrameworkElement)editor).VerticalAlignment = VerticalAlignment.Stretch;

        grid.Children.Add(header);
        grid.Children.Add(editor);

        EditorHost.Child = grid;

        SizeWindow();

        Closed += (_, _) =>
        {
            // If the user closes via the X button, treat as cancel.
            _tcs.TrySetResult((false, null));
        };
    }

    private void SizeWindow()
    {
        // Size to 90% of the primary display work area.
        var area = DisplayArea.Primary;
        if (area is not null)
        {
            int w = (int)(area.WorkArea.Width * 0.9);
            int h = (int)(area.WorkArea.Height * 0.9);
            AppWindow.Resize(new SizeInt32(w, h));

            // Center on screen.
            int x = area.WorkArea.X + (area.WorkArea.Width - w) / 2;
            int y = area.WorkArea.Y + (area.WorkArea.Height - h) / 2;
            AppWindow.Move(new PointInt32(x, y));
        }
        else
        {
            AppWindow.Resize(new SizeInt32(1400, 900));
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var value = _notes?.GetValue();
        _tcs.TrySetResult((true, value));
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _tcs.TrySetResult((false, null));
        Close();
    }

    /// <summary>
    /// Opens the notes editor popup window and returns the result.
    /// </summary>
    /// <param name="title">Title shown at the top (e.g. "Bank - ANZ Notes").</param>
    /// <param name="initialValue">RTF or plain text to load into the editor, or null.</param>
    /// <returns>Tuple: (true, rtfValue) if saved, (false, null) if cancelled.</returns>
    public static Task<(bool Saved, string? Value)> ShowAsync(string title, string? initialValue)
    {
        var win = new NotesWindow(title, initialValue);
        win.Activate();
        return win._tcs.Task;
    }
}
