using Microsoft.UI.Xaml;
using Serilog;
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
        Logging.Initialise();
        InitializeComponent();
        this.UnhandledException += OnUnhandledException;
        System.AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Fatal(e.ExceptionObject as System.Exception, "AppDomain unhandled exception. Terminating={Terminating}", e.IsTerminating);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "WinUI unhandled exception: {Message}", e.Message);
        // Don't mark as handled — let the framework decide. Logging it is the goal.
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Log.Information("OnLaunched");
        AppHost.Build();
        // Best-effort sweep of any decrypted-attachment temp files left over
        // from a previous crash. Safe to run before login because it only
        // touches files the app itself created (prefix-scoped).
        AttachmentService.SweepOrphanedTempFiles();
        _window = new MainWindow();
        _window.Activate();
    }
}

