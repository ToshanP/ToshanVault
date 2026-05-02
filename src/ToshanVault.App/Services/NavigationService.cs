using System;
using Microsoft.UI.Xaml.Controls;

namespace ToshanVault_App.Services;

/// <summary>
/// Holds the root <see cref="Frame"/> of the main window so login / shell
/// pages can hand off to each other without reaching through XAML parents.
/// </summary>
public sealed class NavigationService
{
    private Frame? _rootFrame;

    public void RegisterRootFrame(Frame frame)
        => _rootFrame = frame ?? throw new ArgumentNullException(nameof(frame));

    public void NavigateToLogin()
    {
        if (_rootFrame is null) return;
        _rootFrame.Navigate(typeof(Pages.LoginPage));
        _rootFrame.BackStack.Clear();
    }

    public void NavigateToShell()
    {
        if (_rootFrame is null) return;
        _rootFrame.Navigate(typeof(Pages.MainShellPage));
        _rootFrame.BackStack.Clear();
    }
}
