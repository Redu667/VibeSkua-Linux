using Avalonia;
using Avalonia.Styling;
using Skua.Core.Interfaces;
using Skua.Core.Models;

namespace Skua.Avalonia.Services;

/// <summary>
/// Linux <see cref="IThemeService"/>. The base dark/light toggle is fully
/// functional (it flips Avalonia's <see cref="Application.RequestedThemeVariant"/>);
/// the color-scheme/preset editing surface is stubbed for now (Avalonia theming
/// differs from the WPF MaterialDesign scheme system and is a later refinement).
/// </summary>
public sealed class ThemeService : IThemeService
{
    public event ThemeChangedEventHandler? ThemeChanged;
    public event SchemeChangedEventHandler? SchemeChanged;

    public List<object> Presets { get; } = new();
    public List<object> UserThemes { get; } = new();
    public IEnumerable<object> ColorSelectionValues { get; } = new List<object>();
    public object ColorSelectionValue { get; set; } = new();
    public IEnumerable<object> ContrastValues { get; } = new List<object>();
    public object ContrastValue { get; set; } = new();
    public float DesiredContrastRatio { get; set; } = 4.5f;
    public bool IsColorAdjusted { get; set; }
    public object? SelectedColor { get; set; }
    public ColorScheme ActiveScheme { get; set; } = ColorScheme.Primary;

    private bool _isDarkTheme = true;
    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        set
        {
            _isDarkTheme = value;
            ApplyBaseTheme(value);
        }
    }

    public void ApplyBaseTheme(bool isDark)
    {
        if (Application.Current is { } app)
            app.RequestedThemeVariant = isDark ? ThemeVariant.Dark : ThemeVariant.Light;
        ThemeChanged?.Invoke(null);
    }

    public void ChangeScheme(ColorScheme scheme)
    {
        ActiveScheme = scheme;
        SchemeChanged?.Invoke(scheme, SelectedColor);
    }

    public void ChangeCustomColor(object? obj) { }
    public void SaveTheme(string name) { }
    public void SetCurrentTheme(object? theme) { }
    public void RemoveTheme(object? theme) { }
}
