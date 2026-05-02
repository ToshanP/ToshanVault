using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using ToshanVault.Core.Models;
using ToshanVault.Data.Repositories;

namespace ToshanVault_App.Pages;

/// <summary>Add/edit non-secret bank account fields. Includes an open/closed
/// toggle that gates close-with-reason and reopen-with-confirmation flows
/// inline (replaces the old standalone "Close" button on each row).</summary>
internal sealed class BankAccountDialog : ContentDialog
{
    public BankAccount? Result { get; private set; }
    private readonly BankAccount? _existing;
    private readonly TextBox _bank, _name, _bsb, _ifsc, _acct, _holder, _interest, _website;
    private readonly RichNotesField _notes;
    private readonly ComboBox _type;
    private readonly ToggleSwitch _statusToggle;
    private readonly TextBlock _statusHint;
    private readonly TextBlock _err;

    // Captured inline when the user flips the toggle to closed; applied on Save.
    private string? _pendingCloseReason;
    private DateTimeOffset? _pendingClosedDate;
    // Suppress the toggle's Toggled event during programmatic reverts (when the
    // user cancels the nested confirm dialog).
    private bool _suppressToggle;

    public BankAccountDialog(XamlRoot root, BankAccount? existing, AttachmentService? attachments = null)
    {
        XamlRoot = root;
        _existing = existing;
        Title = existing is null ? "Add bank account" : $"Edit · {existing.Bank} {existing.AccountName}";
        PrimaryButtonText = "Save";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;

        // Stretch the dialog frame so the rich notes area + form fit without
        // an internal scrollbar on most monitors. ContentDialog defaults cap
        // at ~756×620 — bumped via per-dialog resource overrides.
        this.Resources["ContentDialogMaxHeight"] = 1080d;
        this.Resources["ContentDialogMaxWidth"]  = 980d;
        this.Resources["ContentDialogMinWidth"]  = 820d;

        _bank     = TB("Bank (e.g. ANZ, CBA, HDFC)",      existing?.Bank);
        _name     = TB("Account name (e.g. Joint Everyday)", existing?.AccountName);
        _bsb      = TB("BSB (Australian, optional)",            existing?.Bsb);
        _ifsc     = TB("IFSC / Swift / BIC code (optional)",          existing?.IfscCode);
        _acct     = TB("Account number (optional)", existing?.AccountNumber);
        _holder   = TB("Holder name (optional)",    existing?.HolderName);
        _interest = TB("Interest rate % (optional, used for mortgage in retirement plan)", existing?.InterestRatePct?.ToString());
        _website  = TB("Website (optional, e.g. https://www.anz.com)", existing?.Website);
        _notes    = new RichNotesField("Notes (optional, formatted)", existing?.Notes, minHeight: 200);

        _type = new ComboBox { Header = "Account type", HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var t in Enum.GetValues<BankAccountType>()) _type.Items.Add(t.ToString());
        _type.SelectedItem = (existing?.AccountType ?? BankAccountType.Savings).ToString();

        _statusToggle = new ToggleSwitch
        {
            Header  = "Account status",
            OnContent  = "Open",
            OffContent = "Closed",
            IsOn = !(existing?.IsClosed ?? false),
        };
        _statusHint = new TextBlock
        {
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap,
        };
        UpdateStatusHint();
        _statusToggle.Toggled += async (_, _) =>
        {
            if (_suppressToggle) return;
            if (_statusToggle.IsOn) await OnToggleToOpenAsync(); else await OnToggleToClosedAsync();
            UpdateStatusHint();
        };

        _err = new TextBlock { Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"] };

        var panel = new StackPanel { Spacing = 8, Width = 820 };
        panel.Children.Add(_bank);
        panel.Children.Add(_name);
        panel.Children.Add(_type);
        panel.Children.Add(_bsb);
        panel.Children.Add(_ifsc);
        panel.Children.Add(_acct);
        panel.Children.Add(_holder);
        panel.Children.Add(_interest);
        panel.Children.Add(_website);
        panel.Children.Add(_notes.Container);
        if (existing is not null) // status toggle only meaningful for existing rows
        {
            panel.Children.Add(_statusToggle);
            panel.Children.Add(_statusHint);
        }
        panel.Children.Add(_err);
        // Attachments only on existing rows — new accounts must save first
        // then re-open. Avoids buffering plaintext in memory pre-insert.
        AttachmentsPanel? attPanel = null;
        if (existing is not null && attachments is not null)
        {
            attPanel = new AttachmentsPanel(attachments, root, MainWindow.Hwnd,
                Attachment.KindBankAccount, existing.Id);
            attPanel.WireRowEvents();
            panel.Children.Add(attPanel.Container);
            // Reload after the dialog is in the visual tree so XamlRoot is valid
            // for any error popups raised from inside ReloadAsync.
            Loaded += async (_, _) => { try { await attPanel.ReloadAsync(); } catch { /* swallow — surfaced via dialog */ } };
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

    private void UpdateStatusHint()
    {
        if (_existing is null) { _statusHint.Text = string.Empty; return; }
        var wasClosed = _existing.IsClosed;
        var nowOpen = _statusToggle.IsOn;
        if (wasClosed && !nowOpen)
        {
            var date = _existing.ClosedDate?.ToLocalTime().ToString("yyyy-MM-dd") ?? "(unknown)";
            var reason = string.IsNullOrWhiteSpace(_existing.CloseReason) ? "no reason given" : _existing.CloseReason;
            _statusHint.Text = $"Closed on {date} — {reason}";
        }
        else if (wasClosed && nowOpen)
        {
            _statusHint.Text = "Will reopen on Save. Closed date and reason will be cleared.";
        }
        else if (!wasClosed && !nowOpen && _pendingClosedDate is not null)
        {
            var reason = string.IsNullOrWhiteSpace(_pendingCloseReason) ? "no reason given" : _pendingCloseReason;
            _statusHint.Text = $"Will close on Save (today) — {reason}";
        }
        else
        {
            _statusHint.Text = string.Empty;
        }
    }

    private async Task OnToggleToClosedAsync()
    {
        // Going open -> closed. If account was already closed in DB, this is just
        // a no-op revert of an in-dialog reopen attempt; clear reopen intent.
        if (_existing?.IsClosed == true)
        {
            _pendingCloseReason = null;
            _pendingClosedDate = null;
            return;
        }

        var dlg = new CloseConfirmDialog(XamlRoot!, _existing!);
        var res = await dlg.ShowAsync();
        if (res != ContentDialogResult.Primary)
        {
            // User backed out — flip toggle back to open without re-firing.
            _suppressToggle = true;
            _statusToggle.IsOn = true;
            _suppressToggle = false;
            return;
        }
        _pendingCloseReason = string.IsNullOrWhiteSpace(dlg.Reason) ? null : dlg.Reason;
        _pendingClosedDate = DateTimeOffset.UtcNow;
    }

    private async Task OnToggleToOpenAsync()
    {
        // closed -> open
        if (_existing?.IsClosed != true)
        {
            // Was open in DB; this is reverting an in-dialog close attempt.
            _pendingCloseReason = null;
            _pendingClosedDate = null;
            return;
        }

        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Reopen {_existing.Bank} · {_existing.AccountName}?",
            Content = new TextBlock
            {
                Text = "Closed date and reason will be cleared. The account will return to the open list.",
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = "Reopen",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        var res = await confirm.ShowAsync();
        if (res != ContentDialogResult.Primary)
        {
            _suppressToggle = true;
            _statusToggle.IsOn = false;
            _suppressToggle = false;
        }
    }

    private static TextBox TB(string header, string? value) =>
        new() { Header = header, Text = value ?? string.Empty, HorizontalAlignment = HorizontalAlignment.Stretch };

    private void OnSave(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var bank = _bank.Text.Trim();
        var name = _name.Text.Trim();
        if (bank.Length == 0 || name.Length == 0)
        {
            _err.Text = "Bank and Account name are required.";
            args.Cancel = true;
            return;
        }

        double? interest = null;
        if (!string.IsNullOrWhiteSpace(_interest.Text))
        {
            if (!double.TryParse(_interest.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var iv) || iv < 0)
            {
                _err.Text = "Interest rate must be a non-negative number.";
                args.Cancel = true;
                return;
            }
            interest = iv;
        }

        var type = Enum.Parse<BankAccountType>((string)_type.SelectedItem);
        Result = _existing ?? new BankAccount();
        Result.Bank = bank;
        Result.AccountName = name;
        Result.Bsb = N(_bsb.Text);
        Result.IfscCode = N(_ifsc.Text);
        Result.AccountNumber = N(_acct.Text);
        Result.AccountType = type;
        Result.HolderName = N(_holder.Text);
        Result.InterestRatePct = interest;
        Result.Website = N(_website.Text);
        Result.Notes = _notes.GetValue();

        // Apply toggle-driven status transition. The page reads these and calls
        // CloseAsync / ReopenAsync after UpdateAsync so the repo API stays clean.
        if (_existing is not null)
        {
            Result.IsClosed = !_statusToggle.IsOn;
            if (Result.IsClosed && !_existing.IsClosed)
            {
                Result.ClosedDate = _pendingClosedDate ?? DateTimeOffset.UtcNow;
                Result.CloseReason = _pendingCloseReason;
            }
            else if (!Result.IsClosed && _existing.IsClosed)
            {
                Result.ClosedDate = null;
                Result.CloseReason = null;
            }
        }
    }

    private static string? N(string s) { var t = s?.Trim(); return string.IsNullOrEmpty(t) ? null : t; }
}

// ---------------------------------------------------------------------------
internal sealed record QaPair(string Question, string Answer);

internal sealed class CredentialsModel
{
    public string Username { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string CardPin { get; set; } = string.Empty;
    public string PhonePin { get; set; } = string.Empty;
    /// <summary>RTF (or legacy plain text). Encrypted at rest like the rest of
    /// the credential fields; the RTF marker is detected by RichNotesField on
    /// load. Empty/whitespace stays as empty string and is not persisted.</summary>
    public string Notes { get; set; } = string.Empty;
    public QaPair[] Qa { get; } = Enumerable.Range(0, BankCredentialsService.MaxQa).Select(_ => new QaPair("", "")).ToArray();
}

internal sealed class CredentialsDialog : ContentDialog
{
    // Labels are owned by BankCredentialsService (namespaced under "bank_login.")
    // to avoid collision with user-created vault fields.
    private readonly CredentialsModel _model;
    private readonly TextBox _username, _clientId;
    private readonly PasswordBox _password, _cardPin, _phonePin;
    private readonly RichNotesField _notes;
    private readonly TextBox[] _q = new TextBox[BankCredentialsService.MaxQa];
    private readonly PasswordBox[] _a = new PasswordBox[BankCredentialsService.MaxQa];

    /// <summary>Set when the user clicks the secondary "Delete credential"
    /// button. The page handles cascade deletion and skips the save path.</summary>
    public bool DeleteRequested { get; private set; }

    public CredentialsDialog(XamlRoot root, string subtitle, string owner, CredentialsModel model, bool allowDelete)
    {
        XamlRoot = root;
        _model = model;
        Title = $"Internet banking · {subtitle} · {owner}";
        PrimaryButtonText = "Save (encrypted)";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;
        if (allowDelete)
        {
            // Secondary slot doubles as Delete only when editing an existing
            // credential. ContentDialog has no third visible button; this is
            // fine because Cancel still maps to the Close button.
            SecondaryButtonText = "Delete credential";
        }

        this.Resources["ContentDialogMaxHeight"] = 1080d;
        this.Resources["ContentDialogMaxWidth"]  = 980d;
        this.Resources["ContentDialogMinWidth"]  = 820d;

        _username = new TextBox     { Header = "Username",   Text = model.Username, HorizontalAlignment = HorizontalAlignment.Stretch };
        _clientId = new TextBox     { Header = "Client ID / MAC Code",  Text = model.ClientId, HorizontalAlignment = HorizontalAlignment.Stretch };

        var panel = new StackPanel { Spacing = 8, Width = 820 };
        panel.Children.Add(_username);
        panel.Children.Add(_clientId);
        _password = SecretFieldHelpers.AddSecret(panel, "Password (encrypted at rest)", model.Password);
        _cardPin  = SecretFieldHelpers.AddSecret(panel, "Card PIN (encrypted at rest)", model.CardPin);
        _phonePin = SecretFieldHelpers.AddSecret(panel, "Phone-banking PIN (encrypted at rest)", model.PhonePin);
        _notes    = new RichNotesField("Notes (encrypted at rest, formatted)", model.Notes, minHeight: 160);
        panel.Children.Add(_notes.Container);
        panel.Children.Add(new TextBlock { Text = "Security questions (up to 10) — answers encrypted at rest.",
                                           Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"] });
        for (var i = 0; i < BankCredentialsService.MaxQa; i++)
        {
            _q[i] = new TextBox { Header = $"Q{i + 1}", Text = model.Qa[i].Question, HorizontalAlignment = HorizontalAlignment.Stretch };
            panel.Children.Add(_q[i]);
            _a[i] = SecretFieldHelpers.AddSecret(panel, $"A{i + 1}", model.Qa[i].Answer);
        }

        var sv = new ScrollViewer { Content = panel, MaxHeight = 920, HorizontalScrollMode = ScrollMode.Disabled, VerticalScrollMode = ScrollMode.Auto };
        Content = sv;

        PrimaryButtonClick += (_, _) =>
        {
            _model.Username = _username.Text;
            _model.ClientId = _clientId.Text;
            _model.Password = _password.Password;
            _model.CardPin  = _cardPin.Password;
            _model.PhonePin = _phonePin.Password;
            _model.Notes    = _notes.GetValue() ?? string.Empty;
            for (var i = 0; i < BankCredentialsService.MaxQa; i++)
                _model.Qa[i] = new QaPair(_q[i].Text, _a[i].Password);
        };

        SecondaryButtonClick += (_, _) =>
        {
            // Just flag the intent and let this dialog close. The page shows
            // the confirmation dialog AFTER ShowAsync returns — opening a
            // second ContentDialog from inside this handler is unreliable
            // (WinUI permits only one open ContentDialog per XAML root).
            DeleteRequested = true;
        };
    }

    /// <summary>
    /// Adds a label-row (caption + sticky-reveal eye toggle) followed by a
    /// PasswordBox to the parent panel. See <see cref="SecretFieldHelpers"/>.
    /// </summary>
    private static PasswordBox AddSecret(Panel parent, string labelText, string initialValue)
        => SecretFieldHelpers.AddSecret(parent, labelText, initialValue);
}

// ---------------------------------------------------------------------------
/// <summary>Picker shown before <see cref="CredentialsDialog"/> when the user
/// adds a new credential. Lists owners that don't yet have a credential row
/// for this account so we can't violate the UNIQUE(account, owner) constraint.</summary>
internal sealed class OwnerPickerDialog : ContentDialog
{
    private readonly ComboBox _combo;
    public string? SelectedOwner { get; private set; }

    public OwnerPickerDialog(XamlRoot root, IReadOnlyList<string> available)
    {
        XamlRoot = root;
        Title = "Add credential";
        PrimaryButtonText = "Continue";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;

        _combo = new ComboBox
        {
            Header = "Owner",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        foreach (var o in available) _combo.Items.Add(o);
        _combo.SelectedIndex = 0;

        var panel = new StackPanel { Spacing = 8, Width = 320 };
        panel.Children.Add(new TextBlock
        {
            Text = "Pick which family member's login this credential is for. The list shows only owners not already on this account.",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        });
        panel.Children.Add(_combo);
        Content = panel;

        PrimaryButtonClick += (_, _) => SelectedOwner = _combo.SelectedItem as string;
    }
}

// ---------------------------------------------------------------------------
internal sealed class CloseConfirmDialog : ContentDialog
{
    public string Reason { get; private set; } = string.Empty;
    private readonly TextBox _reason;

    public CloseConfirmDialog(XamlRoot root, BankAccount account)
    {
        XamlRoot = root;
        Title = $"Close {account.Bank} · {account.AccountName}?";
        PrimaryButtonText = "Confirm close";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Close;

        _reason = new TextBox { Header = "Reason (optional)", AcceptsReturn = true, Height = 80, HorizontalAlignment = HorizontalAlignment.Stretch };

        var panel = new StackPanel { Spacing = 8, Width = 460 };
        panel.Children.Add(new TextBlock
        {
            Text = "The account row will be moved to the Closed Accounts tab. " +
                   "Linked vault credentials will be KEPT — delete them manually from the Vault tab if no longer needed.",
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(_reason);
        Content = panel;

        PrimaryButtonClick += (_, _) => Reason = _reason.Text ?? string.Empty;
    }
}
