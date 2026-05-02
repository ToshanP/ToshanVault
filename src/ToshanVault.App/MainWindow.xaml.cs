using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using ToshanVault_App.Hosting;
using ToshanVault_App.Services;
using WinRT.Interop;

namespace ToshanVault_App;

public sealed partial class MainWindow : Window
{
    /// <summary>HWND of the single main window. Cached at construction so file
    /// pickers and other WinRT APIs that need an owner handle can grab it
    /// without walking back up to the App. There is only one MainWindow per
    /// process so the static is safe.</summary>
    public static IntPtr Hwnd { get; private set; }

    public MainWindow()
    {
        InitializeComponent();
        Hwnd = WindowNative.GetWindowHandle(this);

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon("Assets/AppIcon.ico");

        // Launch maximised. Calling Maximize() in the constructor is normally
        // sufficient, but on some Windows configurations the window manager
        // restores a previous "Normal" placement after Activate() runs, so we
        // also reassert it via the AppWindow.Changed event the first time we
        // see a Normal/Restored state — that's the timing that previously
        // beat the Maximize call. Both calls are idempotent and cheap.
        if (AppWindow.Presenter is OverlappedPresenter op)
        {
            op.Maximize();
            void EnsureMax(Microsoft.UI.Windowing.AppWindow _, Microsoft.UI.Windowing.AppWindowChangedEventArgs args)
            {
                // AppWindow.Changed fires for many reasons (size, position,
                // title-bar tweaks). Only re-Maximize when we actually catch
                // the window in a non-maximized state, and only unsubscribe
                // once we've confirmed it's stably maximized — otherwise the
                // first benign Changed event would detach the handler before
                // the post-Activate restore we're trying to defeat ever fires.
                if (op.State == OverlappedPresenterState.Restored ||
                    op.State == OverlappedPresenterState.Minimized)
                {
                    op.Maximize();
                    return; // stay subscribed; wait for Maximized confirmation
                }
                if (op.State == OverlappedPresenterState.Maximized)
                {
                    AppWindow.Changed -= EnsureMax;
                }
            }
            AppWindow.Changed += EnsureMax;
        }

        var nav = AppHost.GetService<NavigationService>();
        nav.RegisterRootFrame(RootFrame);
        nav.NavigateToLogin();
    }
}

