using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Serilog;
using ToshanVault.Core.Security;
using ToshanVault.Data.Repositories;
using ToshanVault_App.Hosting;

namespace ToshanVault_App.Pages;

public sealed partial class SettingsPage : Page
{
    private const string KeyBackupOnExit = "backup_on_exit";

    private readonly Vault _vault;
    private readonly SettingsRepository _settings;
    private bool _loading;

    public SettingsPage()
    {
        InitializeComponent();
        _vault = AppHost.GetService<Vault>();
        _settings = AppHost.GetService<SettingsRepository>();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _loading = true;
        try
        {
            BackupOnExitToggle.IsOn = await _settings.GetBoolAsync(KeyBackupOnExit);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load backup_on_exit setting");
        }
        finally { _loading = false; }
    }

    private async void ChangePassword_Click(object sender, RoutedEventArgs e)
    {
        var current = CurrentPasswordBox.Password ?? "";
        var newPwd = NewPasswordBox.Password ?? "";
        var confirm = ConfirmPasswordBox.Password ?? "";

        if (string.IsNullOrEmpty(current))
        {
            ShowInfo("Please enter your current password.", InfoBarSeverity.Warning);
            return;
        }
        if (string.IsNullOrEmpty(newPwd) || newPwd.Length < 4)
        {
            ShowInfo("New password must be at least 4 characters.", InfoBarSeverity.Warning);
            return;
        }
        if (newPwd != confirm)
        {
            ShowInfo("New password and confirmation do not match.", InfoBarSeverity.Warning);
            return;
        }
        if (current == newPwd)
        {
            ShowInfo("New password must be different from the current one.", InfoBarSeverity.Warning);
            return;
        }

        ChangePasswordBtn.IsEnabled = false;
        PasswordProgress.Visibility = Visibility.Visible;
        PasswordProgress.IsActive = true;

        try
        {
            await Task.Run(() => _vault.ChangePasswordAsync(current, newPwd));
            ShowInfo("Password changed successfully. Use the new password next time you unlock.", InfoBarSeverity.Success);
            CurrentPasswordBox.Password = "";
            NewPasswordBox.Password = "";
            ConfirmPasswordBox.Password = "";
        }
        catch (WrongPasswordException)
        {
            ShowInfo("Current password is incorrect.", InfoBarSeverity.Error);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to change master password");
            ShowInfo($"Failed: {ex.Message}", InfoBarSeverity.Error);
        }
        finally
        {
            ChangePasswordBtn.IsEnabled = true;
            PasswordProgress.Visibility = Visibility.Collapsed;
            PasswordProgress.IsActive = false;
        }
    }

    private async void BackupOnExit_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        try
        {
            await _settings.SetBoolAsync(KeyBackupOnExit, BackupOnExitToggle.IsOn);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to save backup_on_exit setting");
        }
    }

    private void ShowInfo(string message, InfoBarSeverity severity)
    {
        PasswordInfoBar.Message = message;
        PasswordInfoBar.Severity = severity;
        PasswordInfoBar.IsOpen = true;
    }

    /// <summary>
    /// Called by MainWindow.Closed handler to perform auto-backup if enabled.
    /// </summary>
    public static async Task BackupOnExitIfEnabledAsync()
    {
        try
        {
            var settings = AppHost.GetService<SettingsRepository>();
            if (!await settings.GetBoolAsync(KeyBackupOnExit)) return;

            var src = AppPaths.DatabasePath;
            if (!File.Exists(src)) return;

            var dir = Path.Combine(AppPaths.DataDirectory, "backups");
            Directory.CreateDirectory(dir);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var dst = Path.Combine(dir, $"vault-{stamp}.db");
            File.Copy(src, dst, overwrite: false);
            Log.Information("Auto-backup on exit saved to {Path}", dst);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Auto-backup on exit failed");
        }
    }
}
