using Microsoft.UI.Xaml;
using ToshanVault_App.Hosting;
using ToshanVault.Data.Repositories;

namespace ToshanVault_App;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppHost.Build();
        // Best-effort sweep of any decrypted-attachment temp files left over
        // from a previous crash. Safe to run before login because it only
        // touches files the app itself created (prefix-scoped).
        AttachmentService.SweepOrphanedTempFiles();
        _window = new MainWindow();
        _window.Activate();
    }
}

