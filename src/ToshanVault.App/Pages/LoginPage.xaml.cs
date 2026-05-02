using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using ToshanVault.Core.Security;
using ToshanVault.Data.Schema;
using ToshanVault_App.Hosting;
using ToshanVault_App.Services;

namespace ToshanVault_App.Pages;

public sealed partial class LoginPage : Page
{
    private readonly Vault _vault;
    private readonly NavigationService _nav;
    private readonly MigrationRunner _migrations;
    private bool _isFirstRun;

    public LoginPage()
    {
        InitializeComponent();
        _vault = AppHost.GetService<Vault>();
        _nav = AppHost.GetService<NavigationService>();
        _migrations = AppHost.GetService<MigrationRunner>();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        try
        {
            await _migrations.RunAsync();
            _isFirstRun = !await _vault.IsInitialisedAsync();
            ModeLabel.Text = _isFirstRun
                ? "First run — set a master password (you'll need it every launch)"
                : "Enter your master password to unlock";
            ConfirmBox.Visibility = _isFirstRun ? Visibility.Visible : Visibility.Collapsed;
            UnlockButton.Content = _isFirstRun ? "Create vault" : "Unlock";
            // Migrations + init detection succeeded: enable the form.
            PasswordBox.IsEnabled = true;
            ConfirmBox.IsEnabled = _isFirstRun;
            UnlockButton.IsEnabled = true;
            // Defer focus to the next dispatcher pass — calling Focus() before
            // the page's first layout completes is a known WinUI no-op.
            DispatcherQueue.TryEnqueue(() => PasswordBox.Focus(FocusState.Programmatic));
        }
        catch (Exception ex)
        {
            // Leave PasswordBox/UnlockButton disabled (set in XAML) so the user
            // cannot click into a half-initialised database.
            ShowError($"Failed to initialise vault: {ex.Message}");
        }
    }

    private async void UnlockButton_Click(object sender, RoutedEventArgs e)
    {
        ErrorBar.IsOpen = false;
        var pwd = PasswordBox.Password ?? string.Empty;
        if (pwd.Length == 0) { ShowError("Password is required."); return; }

        UnlockButton.IsEnabled = false;
        try
        {
            if (_isFirstRun)
            {
                if (pwd != (ConfirmBox.Password ?? string.Empty))
                {
                    ShowError("Passwords do not match.");
                    return;
                }
                await _vault.InitialiseAsync(pwd);
            }
            else
            {
                await _vault.UnlockAsync(pwd);
            }

            // Clear the input controls before navigating away.
            PasswordBox.Password = string.Empty;
            ConfirmBox.Password = string.Empty;

            _nav.NavigateToShell();
        }
        catch (WrongPasswordException)
        {
            ShowError("Wrong password.");
            await Task.Delay(750);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            UnlockButton.IsEnabled = true;
        }
    }

    private void ShowError(string message)
    {
        ErrorBar.Title = "Sign-in error";
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }

    /// <summary>
    /// Press Enter in either password box to trigger the unlock button — saves
    /// a mouse trip on every launch. No-op while the button is disabled (e.g.
    /// during the in-flight unlock attempt or before migrations finish).
    /// </summary>
    private void OnPasswordKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        if (!UnlockButton.IsEnabled) return;
        e.Handled = true;
        UnlockButton_Click(UnlockButton, new RoutedEventArgs());
    }
}
