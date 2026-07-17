using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Skua.Core.ViewModels;

namespace Skua.Avalonia.Views;

public partial class ThemeSettingsView : UserControl
{
    public ThemeSettingsView()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshThemes();
    }

    private ThemeSettingsViewModel? Vm => DataContext as ThemeSettingsViewModel;

    private void RefreshThemes()
    {
        if (Vm is { } vm)
            Themes.ItemsSource = vm.ThemeService.UserThemes.Concat(vm.ThemeService.Presets).ToList();
    }

    private void RefreshAfter_Click(object? sender, RoutedEventArgs e)
        // Click fires before the bound SaveThemeCommand executes, so refresh
        // on the next dispatcher pass, after the theme has been added.
        => global::Avalonia.Threading.Dispatcher.UIThread.Post(RefreshThemes);

    private void Apply_Click(object? sender, RoutedEventArgs e)
    {
        if (Themes.SelectedItem is { } theme)
            Vm?.ThemeService.SetCurrentTheme(theme);
    }

    private void Remove_Click(object? sender, RoutedEventArgs e)
    {
        if (Themes.SelectedItem is { } theme)
        {
            Vm?.ThemeService.RemoveTheme(theme);
            RefreshThemes();
        }
    }
}
