using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using ToshanVault.Core.Models;
using ToshanVault.Data.Repositories;

namespace ToshanVault_App.Pages;

/// <summary>
/// Add / edit dialog for a single General Note. Captures Name, Owner and the
/// rich-text body. Attachments are only available after the note exists (i.e.
/// in edit mode), because the polymorphic attachment table needs the
/// vault_entry id as its target_id.
/// </summary>
internal sealed class GeneralNoteDialog : ContentDialog
{
    private static readonly string[] OwnerOptions = Enum.GetNames<VaultOwner>();
    private const double DialogScale = 0.9;
    private const double HorizontalContentChrome = 96;
    private const double VerticalContentChrome = 220;

    public string? NameValue  { get; private set; }
    public string? OwnerValue { get; private set; }
    public string? BodyValue  { get; private set; }
    public bool Saved { get; private set; }

    private readonly TextBox _name;
    private readonly ComboBox _owner;
    private readonly RichNotesField _body;
    private readonly TextBlock _err;

    public GeneralNoteDialog(
        XamlRoot root,
        VaultEntry? existing,
        string? initialBody,
        AttachmentService? attachments)
    {
        XamlRoot = root;
        Title = existing is null ? "Add note" : $"Edit · {existing.Name}";

        var dialogSize = GetDialogSize(root);
        var contentWidth = Math.Min(dialogSize.Width, Math.Max(240, dialogSize.Width - HorizontalContentChrome));
        var contentHeight = Math.Max(320, dialogSize.Height - VerticalContentChrome);

        Resources["ContentDialogMaxHeight"] = dialogSize.Height;
        Resources["ContentDialogMaxWidth"]  = dialogSize.Width;
        Resources["ContentDialogMinWidth"]  = dialogSize.Width;

        _name = new TextBox
        {
            Header = "Title",
            PlaceholderText = "Short title for this note",
            Text = existing?.Name ?? string.Empty,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        _owner = new ComboBox
        {
            Header = "Owner",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        foreach (var o in OwnerOptions) _owner.Items.Add(o);
        _owner.SelectedItem = !string.IsNullOrWhiteSpace(existing?.Owner) && OwnerOptions.Contains(existing!.Owner!)
            ? existing!.Owner!
            : OwnerOptions[0];

        _body = new RichNotesField(
            "Note (encrypted at rest, formatted)",
            initialBody,
            minHeight: Math.Max(320, contentHeight - 260));

        _err = new TextBlock
        {
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
        };

        var panel = new Grid { RowSpacing = 8, Width = contentWidth };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Grid.SetRow(_name, 0);
        Grid.SetRow(_owner, 1);
        Grid.SetRow(_err, 3);
        panel.Children.Add(_name);
        panel.Children.Add(_owner);
        panel.Children.Add(CreateStretchingNotesContainer(_body));
        panel.Children.Add(_err);

        if (existing is not null && attachments is not null)
        {
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var attPanel = new AttachmentsPanel(attachments, root, MainWindow.Hwnd,
                Attachment.KindGeneralNote, existing.Id);
            attPanel.WireRowEvents();
            Grid.SetRow(attPanel.Container, 4);
            panel.Children.Add(attPanel.Container);
            Loaded += async (_, _) => { try { await attPanel.ReloadAsync(); } catch { /* swallow */ } };
        }

        var saveButton = new Button
        {
            Content = "Save",
            MinWidth = 76,
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
        };
        saveButton.Click += (_, _) => SaveAndClose();
        var saveAccelerator = new KeyboardAccelerator { Key = VirtualKey.Enter };
        saveAccelerator.Invoked += (_, args) =>
        {
            SaveAndClose();
            args.Handled = true;
        };
        saveButton.KeyboardAccelerators.Add(saveAccelerator);

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 76,
        };
        cancelButton.Click += (_, _) => Hide();
        var cancelAccelerator = new KeyboardAccelerator { Key = VirtualKey.Escape };
        cancelAccelerator.Invoked += (_, args) =>
        {
            Hide();
            args.Handled = true;
        };
        cancelButton.KeyboardAccelerators.Add(cancelAccelerator);

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        buttonRow.Children.Add(saveButton);
        buttonRow.Children.Add(cancelButton);

        var contentGrid = new Grid { RowSpacing = 0 };
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var scroller = new ScrollViewer
        {
            Content = panel,
            MaxHeight = contentHeight,
            HorizontalScrollMode = ScrollMode.Disabled,
            VerticalScrollMode = ScrollMode.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        Grid.SetRow(scroller, 0);
        Grid.SetRow(buttonRow, 1);
        contentGrid.Children.Add(scroller);
        contentGrid.Children.Add(buttonRow);
        Content = contentGrid;
    }

    private static (double Width, double Height) GetDialogSize(XamlRoot root)
    {
        var width = root.Size.Width > 0 ? root.Size.Width * DialogScale : 1400;
        var height = root.Size.Height > 0 ? root.Size.Height * DialogScale : 900;
        return (width, height);
    }

    private static FrameworkElement CreateStretchingNotesContainer(RichNotesField notes)
    {
        var stack = (StackPanel)notes.Container;
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
        Grid.SetRow(grid, 2);
        return grid;
    }

    private void SaveAndClose()
    {
        var name = (_name.Text ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            _err.Text = "Title is required.";
            return;
        }
        NameValue  = name;
        OwnerValue = _owner.SelectedItem as string;
        BodyValue  = _body.GetValue();
        Saved = true;
        Hide();
    }
}
