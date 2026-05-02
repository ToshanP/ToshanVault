using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ToshanVault.Core.Models;
using ToshanVault.Data.Repositories;

namespace ToshanVault_App.Pages;

// ---------------------------------------------------------------------------
// Add / edit an insurance policy. Display fields live in the insurance table;
// credentials + notes are encrypted via InsuranceCredentialsDialog.
// ---------------------------------------------------------------------------
internal sealed class InsuranceDialog : ContentDialog
{
    private static readonly string[] OwnerOptions = Enum.GetNames<ToshanVault.Core.Models.VaultOwner>();

    public Insurance? Result { get; private set; }

    private readonly Insurance? _existing;
    private readonly TextBox _insurer, _policyNumber, _insuranceType, _website;
    private readonly ComboBox _owner;
    private readonly CalendarDatePicker _renewal;
    private readonly TextBlock _err;

    public InsuranceDialog(XamlRoot root, Insurance? existing, AttachmentService? attachments)
    {
        XamlRoot = root;
        _existing = existing;
        Title = existing is null ? "Add insurance policy" : $"Edit · {existing.InsurerCompany}";
        PrimaryButtonText = "Save";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;

        this.Resources["ContentDialogMaxHeight"] = 1080d;
        this.Resources["ContentDialogMaxWidth"]  = 720d;
        this.Resources["ContentDialogMinWidth"]  = 560d;

        _insurer       = TB("Insurer (e.g. Bupa, NRMA, AIA)", existing?.InsurerCompany);
        _policyNumber  = TB("Policy number (optional)", existing?.PolicyNumber);
        _insuranceType = TB("Type (Health / Car / Home / Life / Travel / …)", existing?.InsuranceType);
        _website       = TB("Website (optional)", existing?.Website);

        _owner = new ComboBox { Header = "Owner", HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var o in OwnerOptions) _owner.Items.Add(o);
        _owner.SelectedItem = !string.IsNullOrWhiteSpace(existing?.Owner) && OwnerOptions.Contains(existing!.Owner!)
            ? existing!.Owner!
            : OwnerOptions[0];

        _renewal = new CalendarDatePicker
        {
            Header = "Renewal date (optional)",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        if (existing?.RenewalDate is { } d)
            _renewal.Date = new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        _err = new TextBlock
        {
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
        };

        var panel = new StackPanel { Spacing = 8, Width = 560 };
        panel.Children.Add(_insurer);
        panel.Children.Add(_owner);
        panel.Children.Add(_policyNumber);
        panel.Children.Add(_insuranceType);
        panel.Children.Add(_website);
        panel.Children.Add(_renewal);
        panel.Children.Add(_err);

        if (existing is not null && attachments is not null)
        {
            var attPanel = new AttachmentsPanel(attachments, root, MainWindow.Hwnd,
                Attachment.KindInsurance, existing.Id);
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
        var insurer = _insurer.Text.Trim();
        if (insurer.Length == 0)
        {
            _err.Text = "Insurer is required.";
            args.Cancel = true;
            return;
        }

        Result = _existing ?? new Insurance();
        Result.InsurerCompany = insurer;
        Result.Owner          = (string?)_owner.SelectedItem;
        Result.PolicyNumber   = N(_policyNumber.Text);
        Result.InsuranceType  = N(_insuranceType.Text);
        Result.Website        = N(_website.Text);
        Result.RenewalDate    = _renewal.Date is { } dt
            ? DateOnly.FromDateTime(dt.UtcDateTime.Date)
            : null;
    }

    private static TextBox TB(string header, string? value) =>
        new() { Header = header, Text = value ?? string.Empty, HorizontalAlignment = HorizontalAlignment.Stretch };

    private static string? N(string s) { var t = s?.Trim(); return string.IsNullOrEmpty(t) ? null : t; }
}

// ---------------------------------------------------------------------------
internal sealed class InsuranceCredentialsModel
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Notes    { get; set; } = string.Empty;
}

internal sealed class InsuranceCredentialsDialog : ContentDialog
{
    private readonly InsuranceCredentialsModel _model;
    private readonly TextBox _username;
    private readonly PasswordBox _password;
    private readonly RichNotesField _notes;

    public InsuranceCredentialsDialog(XamlRoot root, string subtitle, InsuranceCredentialsModel model)
    {
        XamlRoot = root;
        _model = model;
        Title = "Insurer login · " + subtitle;
        PrimaryButtonText = "Save (encrypted)";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;

        this.Resources["ContentDialogMaxHeight"] = 1080d;
        this.Resources["ContentDialogMaxWidth"]  = 720d;
        this.Resources["ContentDialogMinWidth"]  = 560d;

        _username = new TextBox { Header = "Username / Member ID", Text = model.Username, HorizontalAlignment = HorizontalAlignment.Stretch };

        var panel = new StackPanel { Spacing = 8, Width = 560 };
        panel.Children.Add(_username);
        _password = SecretFieldHelpers.AddSecret(panel, "Password (encrypted at rest)", model.Password);
        _notes    = new RichNotesField("Notes (encrypted at rest, formatted)", model.Notes, minHeight: 200);
        panel.Children.Add(_notes.Container);

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
            _model.Notes    = _notes.GetValue() ?? string.Empty;
        };
    }
}
