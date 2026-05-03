using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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

    public string? NameValue  { get; private set; }
    public string? OwnerValue { get; private set; }
    public string? BodyValue  { get; private set; }

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
        PrimaryButtonText = "Save";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;

        // Note dialog is ~30% wider than the standard vault dialog because
        // the rich-text body benefits from longer line lengths.
        Resources["ContentDialogMaxHeight"] = 1080d;
        Resources["ContentDialogMaxWidth"]  = 936d;
        Resources["ContentDialogMinWidth"]  = 728d;

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
            minHeight: 320);

        _err = new TextBlock
        {
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
        };

        var panel = new StackPanel { Spacing = 8, Width = 728 };
        panel.Children.Add(_name);
        panel.Children.Add(_owner);
        panel.Children.Add(_body.Container);
        panel.Children.Add(_err);

        if (existing is not null && attachments is not null)
        {
            var attPanel = new AttachmentsPanel(attachments, root, MainWindow.Hwnd,
                Attachment.KindGeneralNote, existing.Id);
            attPanel.WireRowEvents();
            panel.Children.Add(attPanel.Container);
            Loaded += async (_, _) => { try { await attPanel.ReloadAsync(); } catch { /* swallow */ } };
        }

        Content = new ScrollViewer
        {
            Content = panel,
            MaxHeight = 920,
            HorizontalScrollMode = ScrollMode.Disabled,
            VerticalScrollMode = ScrollMode.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        PrimaryButtonClick += OnSave;
    }

    private void OnSave(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var name = (_name.Text ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            _err.Text = "Title is required.";
            args.Cancel = true;
            return;
        }
        NameValue  = name;
        OwnerValue = _owner.SelectedItem as string;
        BodyValue  = _body.GetValue();
    }
}
