using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ToshanVault.Core.Models;

namespace ToshanVault_App.Pages;

/// <summary>
/// Add/Edit dialog for a <see cref="Recipe"/>. Title is required; everything
/// else is optional. We deliberately keep this minimal — recipes are catalogue
/// rows, not encrypted vault entries, so no attachments/credentials panel.
/// </summary>
internal sealed class RecipeDialog : ContentDialog
{
    public Recipe? Result { get; private set; }

    private readonly Recipe? _existing;
    private readonly TextBox _title, _author, _url;
    private readonly CheckBox _favourite;
    private readonly TextBlock _err;

    public RecipeDialog(XamlRoot root, Recipe? existing)
    {
        XamlRoot = root;
        _existing = existing;
        Title = existing is null ? "Add recipe" : $"Edit · {existing.Title}";
        PrimaryButtonText = "Save";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;

        this.Resources["ContentDialogMaxWidth"]  = 720d;
        this.Resources["ContentDialogMinWidth"]  = 560d;

        _title     = TB("Title", existing?.Title);
        _author    = TB("Channel / Author (optional)", existing?.Author);
        _url       = TB("YouTube / Web URL (optional)", existing?.YoutubeUrl);
        _favourite = new CheckBox { Content = "Favourite", IsChecked = existing?.IsFavourite ?? false };

        _err = new TextBlock
        {
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
        };

        var panel = new StackPanel { Spacing = 8, Width = 560 };
        panel.Children.Add(_title);
        panel.Children.Add(_author);
        panel.Children.Add(_url);
        panel.Children.Add(_favourite);
        panel.Children.Add(_err);
        Content = panel;

        PrimaryButtonClick += OnSave;
    }

    private void OnSave(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var title = _title.Text.Trim();
        if (title.Length == 0) { _err.Text = "Title is required."; args.Cancel = true; return; }

        Result = _existing ?? new Recipe();
        Result.Title       = title;
        Result.Author      = N(_author.Text);
        Result.YoutubeUrl  = N(_url.Text);
        Result.IsFavourite = _favourite.IsChecked == true;
    }

    private static TextBox TB(string header, string? value) =>
        new() { Header = header, Text = value ?? string.Empty, HorizontalAlignment = HorizontalAlignment.Stretch };

    private static string? N(string s) { var t = s?.Trim(); return string.IsNullOrEmpty(t) ? null : t; }
}
