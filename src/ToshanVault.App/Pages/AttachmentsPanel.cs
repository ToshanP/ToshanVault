using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using ToshanVault.Core.Models;
using ToshanVault.Data.Repositories;
using WinRT.Interop;

namespace ToshanVault_App.Pages;

/// <summary>
/// Reusable attachments section for any dialog that edits an existing
/// <see cref="BankAccount"/> or <see cref="VaultEntry"/>. Renders as a
/// labelled list with [+ Add] / [Open] / [Delete] actions, all encrypted
/// inline via <see cref="AttachmentService"/>.
///
/// Only available on existing rows — when constructed with no <c>targetId</c>
/// the panel shows a hint asking the user to save the record first. This
/// avoids the "queue and flush" complexity of buffering attachments in memory
/// before an entity has an id.
///
/// Open path goes through the OS shell via <see cref="Launcher"/>: the file
/// is decrypted to <c>%TEMP%</c> with the recognisable
/// <see cref="AttachmentService.TempFilePrefix"/> prefix and launched with the
/// user's default app. Sweep at app start cleans up leftovers if the user
/// crashes mid-view.
/// </summary>
internal sealed class AttachmentsPanel
{
    private readonly AttachmentService _svc;
    private readonly XamlRoot _root;
    private readonly IntPtr _hwnd;
    private readonly string _targetKind;
    private readonly long? _targetId;
    private readonly ObservableCollection<Attachment> _items = new();
    private readonly ListView _list;
    private readonly Button _addBtn;
    private readonly TextBlock _hint;

    public FrameworkElement Container { get; }

    public AttachmentsPanel(
        AttachmentService svc,
        XamlRoot root,
        IntPtr ownerHwnd,
        string targetKind,
        long? targetId)
    {
        _svc = svc ?? throw new ArgumentNullException(nameof(svc));
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _hwnd = ownerHwnd;
        _targetKind = targetKind;
        _targetId = targetId;

        var label = new TextBlock
        {
            Text = "Attachments",
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
        };

        _addBtn = new Button { Content = "+ Add file…", Margin = new Thickness(8, 0, 0, 0) };
        _addBtn.Click += async (_, _) => await OnAddAsync();
        _addBtn.IsEnabled = targetId is not null;

        var headerRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 0, 0, 4),
        };
        headerRow.Children.Add(label);
        headerRow.Children.Add(_addBtn);

        _list = new ListView
        {
            ItemsSource = _items,
            SelectionMode = ListViewSelectionMode.None,
            ItemTemplate = BuildRowTemplate(),
            MaxHeight = 220,
        };

        _hint = new TextBlock
        {
            Text = targetId is null
                ? "Save the record first, then re-open to attach files."
                : "PDFs, screenshots, anything up to 50 MB. Files are encrypted in the vault.",
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
        };

        var stack = new StackPanel { Spacing = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        stack.Children.Add(headerRow);
        stack.Children.Add(_list);
        stack.Children.Add(_hint);
        Container = stack;
    }

    /// <summary>Loads the current attachment list. Safe to call multiple times;
    /// no-ops when the panel was constructed without a target id.</summary>
    public async Task ReloadAsync()
    {
        _items.Clear();
        if (_targetId is null) return;
        var rows = await _svc.ListAsync(_targetKind, _targetId.Value);
        foreach (var r in rows) _items.Add(r);
    }

    private DataTemplate BuildRowTemplate()
    {
        // Bindings populate file name on the first column; the size text and
        // button Tags are filled in WireRowEvents below (each container is
        // virtualised + recycled, so handlers are re-attached per phase 0).
        const string templateXaml = @"<DataTemplate
    xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Grid ColumnSpacing='8' Padding='4,2'>
    <Grid.ColumnDefinitions>
      <ColumnDefinition Width='*'/>
      <ColumnDefinition Width='Auto'/>
      <ColumnDefinition Width='Auto'/>
      <ColumnDefinition Width='Auto'/>
    </Grid.ColumnDefinitions>
    <TextBlock Grid.Column='0' VerticalAlignment='Center' Text='{Binding FileName}' TextTrimming='CharacterEllipsis'/>
    <TextBlock Grid.Column='1' VerticalAlignment='Center' Opacity='0.7'/>
    <Button Grid.Column='2' Content='Open'/>
    <Button Grid.Column='3' Content='Delete' Margin='4,0,0,0'/>
  </Grid>
</DataTemplate>";
        return (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(templateXaml);
    }

    private async Task OnAddAsync()
    {
        if (_targetId is null) return;

        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, _hwnd);
        picker.ViewMode = PickerViewMode.List;
        picker.SuggestedStartLocation = PickerLocationId.Desktop;
        picker.FileTypeFilter.Add("*");

        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        try
        {
            var props = await file.GetBasicPropertiesAsync();
            if ((long)props.Size > AttachmentService.MaxFileBytes)
            {
                await ShowMessageAsync("Too large",
                    $"That file is {props.Size / (1024 * 1024)} MB. The hard limit is {AttachmentService.MaxFileBytes / (1024 * 1024)} MB.");
                return;
            }

            var buffer = await FileIO.ReadBufferAsync(file);
            var bytes = new byte[buffer.Length];
            using (var reader = DataReader.FromBuffer(buffer))
            {
                reader.ReadBytes(bytes);
            }

            var mime = file.ContentType;
            await _svc.AddAsync(_targetKind, _targetId.Value, file.Name, mime, bytes);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("Could not attach file", ex.Message);
        }
    }

    private async Task OnOpenAsync(long attachmentId)
    {
        try
        {
            var path = await _svc.DecryptToTempAsync(attachmentId);
            var sf = await StorageFile.GetFileFromPathAsync(path);
            await Windows.System.Launcher.LaunchFileAsync(sf);
            // We deliberately do NOT delete after launch — the OS may keep the
            // file open for a while. Sweep on next launch is the cleanup
            // mechanism. See AttachmentService.SweepOrphanedTempFiles.
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("Could not open attachment", ex.Message);
        }
    }

    private async Task OnDeleteAsync(long attachmentId, string fileName)
    {
        var confirm = new ContentDialog
        {
            XamlRoot = _root,
            Title = "Delete attachment?",
            Content = new TextBlock
            {
                Text = $"\"{fileName}\" will be permanently removed from the vault. This cannot be undone.",
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            await _svc.DeleteAsync(attachmentId);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("Could not delete attachment", ex.Message);
        }
    }

    /// <summary>Wire up the Open / Delete buttons from the data-bound template.
    /// Called from <see cref="WireRowEvents"/> after the ListView materialises
    /// containers (we attach to <see cref="ListViewBase.ContainerContentChanging"/>).</summary>
    public void WireRowEvents()
    {
        _list.ContainerContentChanging += (_, args) =>
        {
            if (args.Phase != 0) return;
            if (args.ItemContainer.ContentTemplateRoot is not Grid g) return;
            if (args.Item is not Attachment att) return;

            // Children: [TextBlock filename, TextBlock size, Button Open, Button Delete]
            // Re-attach handlers each pass — virtualised containers get reused.
            if (g.Children.Count >= 4 &&
                g.Children[1] is TextBlock sizeText &&
                g.Children[2] is Button openBtn &&
                g.Children[3] is Button delBtn)
            {
                sizeText.Text = FormatSize(att.SizeBytes);
                openBtn.Tag = att.Id;
                delBtn.Tag = att.Id;
                openBtn.Click -= OpenHandler;
                delBtn.Click -= DeleteHandler;
                openBtn.Click += OpenHandler;
                delBtn.Click += DeleteHandler;
            }
        };
    }

    private async void OpenHandler(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is long id) await OnOpenAsync(id);
    }

    private async void DeleteHandler(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is long id)
        {
            var item = _items.FirstOrDefault(a => a.Id == id);
            await OnDeleteAsync(id, item?.FileName ?? "this file");
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return bytes + " B";
        if (bytes < 1024 * 1024) return (bytes / 1024d).ToString("F1") + " KB";
        return (bytes / (1024d * 1024d)).ToString("F1") + " MB";
    }

    private Task ShowMessageAsync(string title, string body)
    {
        var dlg = new ContentDialog
        {
            XamlRoot = _root,
            Title = title,
            Content = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "OK",
            DefaultButton = ContentDialogButton.Close,
        };
        return dlg.ShowAsync().AsTask();
    }
}
