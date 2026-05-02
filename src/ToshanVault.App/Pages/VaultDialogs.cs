using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ToshanVault.Core.Models;
using ToshanVault.Data.Repositories;

namespace ToshanVault_App.Pages;

// ---------------------------------------------------------------------------
// Add / edit a web-login vault entry: identifying fields the user wants to see
// in the list (Name, Owner) plus the encrypted-but-displayed-plaintext fields
// (Number, Website, Additional Details). Per-entry secrets — Username,
// Password, Q&A — live in the separate VaultCredentialsDialog.
// ---------------------------------------------------------------------------
internal sealed class VaultEntryDialog : ContentDialog
{
    private static readonly string[] OwnerOptions =
        Enum.GetNames<VaultOwner>(); // Toshan / Devu / Prachi / Saloni

    public VaultEntry? Result { get; private set; }
    public string? NumberValue { get; private set; }
    public string? WebsiteValue { get; private set; }
    public string? AdditionalDetailsValue { get; private set; }

    private readonly VaultEntry? _existing;
    private readonly TextBox _name, _number, _website;
    private readonly RichNotesField _additional;
    private readonly ComboBox _owner;
    private readonly TextBlock _err;

    public VaultEntryDialog(
        XamlRoot root,
        VaultEntry? existing,
        string? initialNumber,
        string? initialWebsite,
        string? initialAdditionalDetails,
        AttachmentService? attachments = null)
    {
        XamlRoot = root;
        _existing = existing;
        Title = existing is null ? "Add vault entry" : $"Edit · {existing.Name}";
        PrimaryButtonText = "Save";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;

        // Stretch the dialog so the rich notes area + form fit without needing
        // an inner scrollbar on most monitors.
        this.Resources["ContentDialogMaxHeight"] = 1080d;
        this.Resources["ContentDialogMaxWidth"]  = 720d;
        this.Resources["ContentDialogMinWidth"]  = 560d;

        _name       = TB("Name (e.g. Netflix, Aussie Super)", existing?.Name);
        _number     = TB("Number / Membership ID (optional, encrypted at rest)", initialNumber);
        _website    = TB("Website (optional, encrypted at rest)",                initialWebsite);

        _additional = new RichNotesField(
            "Additional details (optional, encrypted at rest, formatted)",
            initialAdditionalDetails,
            minHeight: 220);

        _owner = new ComboBox { Header = "Owner", HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var o in OwnerOptions) _owner.Items.Add(o);
        _owner.SelectedItem = !string.IsNullOrWhiteSpace(existing?.Owner) && OwnerOptions.Contains(existing!.Owner!)
            ? existing!.Owner!
            : OwnerOptions[0];

        _err = new TextBlock { Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"] };

        var panel = new StackPanel { Spacing = 8, Width = 560 };
        panel.Children.Add(_name);
        panel.Children.Add(_owner);
        panel.Children.Add(_number);
        panel.Children.Add(_website);
        panel.Children.Add(_additional.Container);
        panel.Children.Add(_err);
        // Attachments only on existing entries.
        if (existing is not null && attachments is not null)
        {
            var attPanel = new AttachmentsPanel(attachments, root, MainWindow.Hwnd,
                Attachment.KindVaultEntry, existing.Id);
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
        var name = _name.Text.Trim();
        if (name.Length == 0)
        {
            _err.Text = "Name is required.";
            args.Cancel = true;
            return;
        }

        Result = _existing ?? new VaultEntry { Kind = WebCredentialsService.EntryKind };
        Result.Name = name;
        Result.Owner = (string?)_owner.SelectedItem;
        NumberValue = N(_number.Text);
        WebsiteValue = N(_website.Text);
        AdditionalDetailsValue = _additional.GetValue();
    }

    private static TextBox TB(string header, string? value) =>
        new() { Header = header, Text = value ?? string.Empty, HorizontalAlignment = HorizontalAlignment.Stretch };

    private static string? N(string s) { var t = s?.Trim(); return string.IsNullOrEmpty(t) ? null : t; }
}

// ---------------------------------------------------------------------------
internal sealed class WebCredentialsModel
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public QaPair[] Qa { get; } = Enumerable.Range(0, WebCredentialsService.MaxQa).Select(_ => new QaPair("", "")).ToArray();
}

internal sealed class VaultCredentialsDialog : ContentDialog
{
    private readonly WebCredentialsModel _model;
    private readonly TextBox _username;
    private readonly PasswordBox _password;
    private readonly TextBox[] _q = new TextBox[WebCredentialsService.MaxQa];
    private readonly PasswordBox[] _a = new PasswordBox[WebCredentialsService.MaxQa];

    public VaultCredentialsDialog(XamlRoot root, string subtitle, WebCredentialsModel model)
    {
        XamlRoot = root;
        _model = model;
        Title = "Login secrets · " + subtitle;
        PrimaryButtonText = "Save (encrypted)";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;

        this.Resources["ContentDialogMaxHeight"] = 1080d;
        this.Resources["ContentDialogMaxWidth"]  = 720d;
        this.Resources["ContentDialogMinWidth"]  = 560d;

        _username = new TextBox { Header = "Username / Id", Text = model.Username, HorizontalAlignment = HorizontalAlignment.Stretch };

        var panel = new StackPanel { Spacing = 8, Width = 560 };
        panel.Children.Add(_username);
        _password = SecretFieldHelpers.AddSecret(panel, "Password / Secret (encrypted at rest)", model.Password);
        panel.Children.Add(new TextBlock
        {
            Text = "Security questions (up to 10) — answers encrypted at rest.",
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        });
        for (var i = 0; i < WebCredentialsService.MaxQa; i++)
        {
            _q[i] = new TextBox { Header = $"Q{i + 1}", Text = model.Qa[i].Question, HorizontalAlignment = HorizontalAlignment.Stretch };
            panel.Children.Add(_q[i]);
            _a[i] = SecretFieldHelpers.AddSecret(panel, $"A{i + 1}", model.Qa[i].Answer);
        }

        Content = new ScrollViewer
        {
            Content = panel,
            MaxHeight = 920,
            HorizontalScrollMode = ScrollMode.Disabled,
            VerticalScrollMode = ScrollMode.Auto,
        };

        PrimaryButtonClick += (_, _) =>
        {
            _model.Username = _username.Text;
            _model.Password = _password.Password;
            for (var i = 0; i < WebCredentialsService.MaxQa; i++)
                _model.Qa[i] = new QaPair(_q[i].Text, _a[i].Password);
        };
    }
}
