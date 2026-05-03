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
        var dbPath = AppPaths.DatabasePath;
        Log.Information("OnLaunched. Database: {Path}", dbPath);

        // Refuse to silently create a fresh DB when the configured one is
        // missing. A typo in appsettings.json or a misplaced vault.db can
        // otherwise look identical to a wiped-data scenario, and SQLite's
        // ReadWriteCreate mode will happily oblige by writing an empty file
        // at the wrong path. Prompt once before letting that happen so the
        // user can quit and fix the path / restore from backup.
        if (!System.IO.File.Exists(dbPath))
        {
            Log.Warning("Database file not found at {Path} — prompting before creation.", dbPath);
            if (!Native.PromptCreateNewDatabase(dbPath))
            {
                Log.Information("User declined to create a new database. Exiting.");
                Microsoft.UI.Xaml.Application.Current.Exit();
                return;
            }
            Log.Information("User confirmed creating a new database at {Path}.", dbPath);
        }

        AppHost.Build();
        // Best-effort sweep of any decrypted-attachment temp files left over
        // from a previous crash. Safe to run before login because it only
        // touches files the app itself created (prefix-scoped).
        AttachmentService.SweepOrphanedTempFiles();
        _window = new MainWindow();
        _window.Activate();
    }

    /// <summary>Tiny Win32 wrapper. Used pre-window so we can't rely on a
    /// XAML <c>ContentDialog</c> (no host page exists yet).</summary>
    private static class Native
    {
        private const uint MB_YESNO            = 0x00000004;
        private const uint MB_ICONWARNING      = 0x00000030;
        private const uint MB_DEFBUTTON2       = 0x00000100;
        private const uint MB_SETFOREGROUND    = 0x00010000;
        private const uint MB_TOPMOST          = 0x00040000;
        private const int  IDYES               = 6;

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        private static extern int MessageBoxW(System.IntPtr hWnd, string text, string caption, uint type);

        public static bool PromptCreateNewDatabase(string dbPath)
        {
            var msg =
                "ToshanVault database not found at:\n\n" +
                dbPath + "\n\n" +
                "Click YES to create a new, empty database at this location.\n" +
                "Click NO to quit so you can fix the path in appsettings.json " +
                "or restore a backup before launching again.";
            var rc = MessageBoxW(System.IntPtr.Zero, msg, "ToshanVault — database missing",
                MB_YESNO | MB_ICONWARNING | MB_DEFBUTTON2 | MB_SETFOREGROUND | MB_TOPMOST);
            return rc == IDYES;
        }
    }
}

