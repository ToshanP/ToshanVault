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
    private Action<string>? _shellNavigator;

    public void RegisterRootFrame(Frame frame)
        => _rootFrame = frame ?? throw new ArgumentNullException(nameof(frame));

    /// <summary>Wired by <see cref="Pages.MainShellPage"/> on load so other pages
    /// (e.g. Dashboard tiles) can switch the NavView selection by tag.</summary>
    public void RegisterShellNavigator(Action<string> nav)
        => _shellNavigator = nav ?? throw new ArgumentNullException(nameof(nav));

    public void NavigateInShell(string tag) => _shellNavigator?.Invoke(tag);

    /// <summary>Navigate to a shell page and pass an initial search filter so
    /// the target page pre-fills its search box.</summary>
    public void NavigateInShell(string tag, string? searchFilter)
    {
        PendingSearchFilter = searchFilter;
        NavigateInShell(tag);
    }

    /// <summary>One-shot search filter consumed by the target page on load.</summary>
    public string? PendingSearchFilter { get; set; }

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
