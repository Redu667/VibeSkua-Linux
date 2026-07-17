using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Skua.Core.Interfaces;
using Skua.Core.Models;

namespace Skua.Avalonia.Services;

/// <summary>
/// A named theme on Linux: base variant + accent color. The Fluent theme has a
/// single accent (no MaterialDesign primary/secondary split), so this is the
/// whole state a WPF ThemeItem carries that applies here.
/// </summary>
public sealed record LinuxTheme(string Name, bool IsDark, string Accent)
{
    public override string ToString() => Name;

    public string Serialize() => $"{Name}|{(IsDark ? "dark" : "light")}|{Accent}";

    public static LinuxTheme? Deserialize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        string[] parts = value.Split('|');
        return parts.Length == 3 ? new LinuxTheme(parts[0], parts[1] == "dark", parts[2]) : null;
    }
}

/// <summary>
/// Linux <see cref="IThemeService"/>. Base dark/light flips Avalonia's
/// <see cref="Application.RequestedThemeVariant"/>; the accent color drives
/// Fluent's <c>SystemAccentColor</c> resources (with derived light/dark
/// shades). Save/apply/remove named themes persists them through
/// <see cref="ISettingsService"/> ("LinuxUserThemes"/"LinuxCurrentTheme"), and
/// the saved current theme is re-applied at startup. The MaterialDesign
/// primary/secondary scheme split doesn't exist in Fluent — ChangeScheme is
/// tracked but every scheme edits the one accent.
/// </summary>
public sealed class ThemeService : IThemeService
{
    public const string DefaultAccent = "#FF607D8B"; // WPF Skua default (blue grey)

    private readonly ISettingsService _settings;
    private string _accent = DefaultAccent;

    public ThemeService(ISettingsService settings)
    {
        _settings = settings;
        Presets = new List<object>
        {
            new LinuxTheme("Skua", true, DefaultAccent),
            new LinuxTheme("Skua Light", false, DefaultAccent),
            new LinuxTheme("Midnight", true, "#FF3F51B5"),
            new LinuxTheme("Forest", true, "#FF4CAF50"),
            new LinuxTheme("Crimson", true, "#FFF44336"),
        };
        UserThemes = LoadUserThemes();
    }

    public event ThemeChangedEventHandler? ThemeChanged;
    public event SchemeChangedEventHandler? SchemeChanged;

    public List<object> Presets { get; }
    public List<object> UserThemes { get; }
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
        OnUI(() =>
        {
            if (Application.Current is { } app)
                app.RequestedThemeVariant = isDark ? ThemeVariant.Dark : ThemeVariant.Light;
        });
        ThemeChanged?.Invoke(CurrentTheme());
    }

    public void ChangeScheme(ColorScheme scheme)
    {
        ActiveScheme = scheme;
        SchemeChanged?.Invoke(scheme, SelectedColor);
    }

    public void ChangeCustomColor(object? obj)
    {
        Color? color = ParseColor(obj);
        if (color is null)
            return;
        _accent = color.Value.ToString().ToUpperInvariant();
        ApplyAccent(color.Value);
        PersistCurrent();
        ThemeChanged?.Invoke(CurrentTheme());
    }

    public void SaveTheme(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;
        LinuxTheme theme = new(name.Trim(), IsDarkTheme, _accent);
        UserThemes.RemoveAll(t => t is LinuxTheme lt && lt.Name.Equals(theme.Name, StringComparison.OrdinalIgnoreCase));
        UserThemes.Add(theme);
        PersistUserThemes();
        SetCurrentTheme(theme);
    }

    public void SetCurrentTheme(object? theme)
    {
        LinuxTheme? resolved = theme switch
        {
            LinuxTheme lt => lt,
            string name => AllThemes().FirstOrDefault(t => t.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase))
                           ?? LinuxTheme.Deserialize(name),
            _ => null,
        };
        if (resolved is null)
            return;

        _accent = resolved.Accent;
        _isDarkTheme = resolved.IsDark;
        if (ParseColor(resolved.Accent) is { } accent)
            ApplyAccent(accent);
        ApplyBaseTheme(resolved.IsDark);
        PersistCurrent();
    }

    public void RemoveTheme(object? theme)
    {
        string? name = theme switch
        {
            LinuxTheme lt => lt.Name,
            string s => s,
            _ => null,
        };
        if (name is null)
            return;
        UserThemes.RemoveAll(t => t is LinuxTheme lt && lt.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        PersistUserThemes();
        ThemeChanged?.Invoke(CurrentTheme());
    }

    /// <summary>Re-apply the persisted current theme (called once at startup).</summary>
    public void ApplySavedTheme()
    {
        LinuxTheme? saved = LinuxTheme.Deserialize(_settings.Get<string>("LinuxCurrentTheme"));
        if (saved is not null)
            SetCurrentTheme(saved);
    }

    private LinuxTheme CurrentTheme() => new("Current", IsDarkTheme, _accent);

    private IEnumerable<LinuxTheme> AllThemes()
        => UserThemes.OfType<LinuxTheme>().Concat(Presets.OfType<LinuxTheme>());

    private List<object> LoadUserThemes()
    {
        List<object> themes = new();
        StringCollection? stored = _settings.Get<StringCollection>("LinuxUserThemes");
        if (stored is null)
            return themes;
        foreach (string? entry in stored)
        {
            if (LinuxTheme.Deserialize(entry) is { } theme)
                themes.Add(theme);
        }
        return themes;
    }

    private void PersistUserThemes()
    {
        StringCollection stored = new();
        foreach (LinuxTheme theme in UserThemes.OfType<LinuxTheme>())
            stored.Add(theme.Serialize());
        _settings.Set("LinuxUserThemes", stored);
    }

    private void PersistCurrent()
        => _settings.Set("LinuxCurrentTheme", CurrentTheme().Serialize());

    private static Color? ParseColor(object? obj) => obj switch
    {
        Color c => c,
        string s when Color.TryParse(s.Trim(), out Color parsed) => parsed,
        _ => null,
    };

    /// <summary>
    /// Point Fluent's accent resources at the chosen color. Fluent derives its
    /// hover/pressed variants from SystemAccentColorDark1..3/Light1..3, so set
    /// those too with simple shades toward black/white.
    /// </summary>
    private static void ApplyAccent(Color accent)
        => OnUI(() =>
        {
            if (Application.Current is not { } app)
                return;
            app.Resources["SystemAccentColor"] = accent;
            app.Resources["SystemAccentColorDark1"] = Shade(accent, -0.15);
            app.Resources["SystemAccentColorDark2"] = Shade(accent, -0.30);
            app.Resources["SystemAccentColorDark3"] = Shade(accent, -0.45);
            app.Resources["SystemAccentColorLight1"] = Shade(accent, 0.15);
            app.Resources["SystemAccentColorLight2"] = Shade(accent, 0.30);
            app.Resources["SystemAccentColorLight3"] = Shade(accent, 0.45);
        });

    private static Color Shade(Color color, double amount)
    {
        byte Channel(byte c) => (byte)Math.Clamp(
            amount >= 0 ? c + (255 - c) * amount : c * (1 + amount), 0, 255);
        return Color.FromArgb(color.A, Channel(color.R), Channel(color.G), Channel(color.B));
    }

    private static void OnUI(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }
}
