using System.Reflection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ToshanVault_App.Hosting;

namespace ToshanVault_App.Pages;

public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = ver is not null ? $"Version {ver.Major}.{ver.Minor}.{ver.Build}" : "Version 1.0.0";
        DataPathText.Text = AppPaths.DatabasePath;
    }
}
