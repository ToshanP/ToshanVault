using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ToshanVault.Core.Models;
using ToshanVault.Data.Repositories;
using Windows.System;

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

    private readonly VaultEntry? _existing;
    private readonly TextBox _name, _number, _website;
    private readonly ComboBox _owner;
    private readonly AutoSuggestBox _category;
    private readonly TextBlock _err;

    public VaultEntryDialog(
        XamlRoot root,
        VaultEntry? existing,
        string? initialNumber,
        string? initialWebsite,
        AttachmentService? attachments = null,
        IReadOnlyList<string>? existingCategories = null)
    {
        XamlRoot = root;
        _existing = existing;
        Title = existing is null ? "Add vault entry" : $"Edit · {existing.Name}";
        PrimaryButtonText = "Save";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;

        // Stretch the dialog so the form fields fit without needing
        // an inner scrollbar on most monitors.
        this.Resources["ContentDialogMaxHeight"] = 1080d;
        this.Resources["ContentDialogMaxWidth"]  = 720d;
        this.Resources["ContentDialogMinWidth"]  = 560d;

        _name       = TB("Name (e.g. Netflix, Aussie Super)", existing?.Name);
        _number     = TB("Number / Membership ID (optional, encrypted at rest)", initialNumber);
        _website    = TB("Website (optional, encrypted at rest)",                initialWebsite);

        _owner = new ComboBox { Header = "Owner", HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var o in OwnerOptions) _owner.Items.Add(o);
        _owner.SelectedItem = !string.IsNullOrWhiteSpace(existing?.Owner) && OwnerOptions.Contains(existing!.Owner!)
            ? existing!.Owner!
            : OwnerOptions[0];

        // Free-text Category with autocomplete from existing categories so
        // users naturally re-use group names rather than creating one-off
        // typos that would split a group into two banners.
        _category = new AutoSuggestBox
        {
            Header = "Category (optional, used to group entries on the Vault page)",
            PlaceholderText = "e.g. Government, Banking, Pets",
            Text = existing?.Category ?? string.Empty,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            QueryIcon = new SymbolIcon(Symbol.Tag),
        };
        if (existingCategories is { Count: > 0 })
        {
            _category.ItemsSource = existingCategories;
            string lastGoodText = existing?.Category ?? string.Empty;
            string textAtFocus = lastGoodText;
            bool textChangedSinceFocus = false;
            bool tabNavigationSinceFocus = false;

            _category.GotFocus += (_, _) =>
            {
                textAtFocus = _category.Text ?? string.Empty;
                textChangedSinceFocus = false;
                tabNavigationSinceFocus = false;
            };
            _category.KeyDown += (_, e) =>
            {
                if (e.Key == VirtualKey.Tab) tabNavigationSinceFocus = true;
            };

            _category.TextChanged += (s, e) =>
            {
                if (e.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
                {
                    if (tabNavigationSinceFocus
                        && !textChangedSinceFocus
                        && string.IsNullOrEmpty(s.Text)
                        && !string.IsNullOrEmpty(textAtFocus))
                    {
                        s.Text = textAtFocus;
                        tabNavigationSinceFocus = false;
                        return;
                    }

                    textChangedSinceFocus = true;
                    lastGoodText = s.Text ?? string.Empty;
                    var q = lastGoodText.Trim();
                    s.ItemsSource = q.Length == 0
                        ? existingCategories
                        : existingCategories.Where(c => c.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
                }
                else if (e.Reason == AutoSuggestionBoxTextChangeReason.SuggestionChosen
                         && tabNavigationSinceFocus
                         && !textChangedSinceFocus
                         && string.IsNullOrEmpty(s.Text) && !string.IsNullOrEmpty(lastGoodText))
                {
                    // WinUI quirk: tabbing out without selecting a suggestion
                    // fires SuggestionChosen with empty text. Restore.
                    s.Text = lastGoodText;
                    tabNavigationSinceFocus = false;
                }
                else if (e.Reason == AutoSuggestionBoxTextChangeReason.ProgrammaticChange)
                {
                    // PrefillCategory or restore — keep lastGoodText in sync.
                    lastGoodText = s.Text ?? string.Empty;
                }
            };
        }

        _err = new TextBlock { Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"] };

        var panel = new StackPanel { Spacing = 8, Width = 560 };
        panel.Children.Add(_category);
        panel.Children.Add(_name);
        panel.Children.Add(_owner);
        panel.Children.Add(_number);
        panel.Children.Add(_website);
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

        if (existing is null)
        {
            // Defer focus so it runs after ContentDialog's own focus pass,
            // which would otherwise auto-focus the first child (_category).
            Loaded += (_, _) =>
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                    () => _name.Focus(FocusState.Programmatic));
        }

        PrimaryButtonClick += OnSave;
    }

    /// <summary>Prefill the Category field (used by per-group "+ Add" buttons).</summary>
    public void PrefillCategory(string category) => _category.Text = category ?? string.Empty;

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
        Result.Category = N(_category.Text);
        NumberValue = N(_number.Text);
        WebsiteValue = N(_website.Text);
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

    public bool DeleteRequested { get; private set; }

    public VaultCredentialsDialog(XamlRoot root, string subtitle, string owner, WebCredentialsModel model, bool allowDelete)
    {
        XamlRoot = root;
        _model = model;
        Title = $"Login secrets · {subtitle} · {owner}";
        PrimaryButtonText = "Save (encrypted)";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;
        if (allowDelete)
            SecondaryButtonText = "Delete credential";

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

        SecondaryButtonClick += (_, _) => { DeleteRequested = true; };
    }
}
