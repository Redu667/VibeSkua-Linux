using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia.Headless.XUnit;
using Skua.Avalonia.Services;
using Skua.Core.Interfaces;
using Skua.Core.Models;
using Xunit;

namespace Skua.Avalonia.Tests;

/// <summary>
/// The Linux theme backend: named themes (variant + accent) save, apply,
/// remove, and persist through ISettingsService, and accent colors parse and
/// land on Fluent's SystemAccentColor resources.
/// </summary>
public class ThemeServiceTests
{
    private sealed class MemorySettings : ISettingsService
    {
        private readonly Dictionary<string, object?> _values = new();
        public void Set<T>(string key, T value) => _values[key] = value;
        public T? Get<T>(string key) => _values.TryGetValue(key, out object? v) && v is T t ? t : default;
        public T Get<T>(string key, T defaultValue) => _values.TryGetValue(key, out object? v) && v is T t ? t : defaultValue;
        public void Initialize(AppRole role) { }
        public SharedSettings GetShared() => new();
        public ClientSettings GetClient() => new();
        public ManagerSettings GetManager() => new();
        public void SetApplicationVersion() { }
        public void ReloadSettings() { }
    }

    [AvaloniaFact]
    public void SaveTheme_persists_and_a_new_service_reloads_it()
    {
        MemorySettings settings = new();
        ThemeService service = new(settings);
        service.IsDarkTheme = false;
        service.ChangeCustomColor("#FF112233");

        service.SaveTheme("My Theme");

        Assert.Contains(service.UserThemes.OfType<LinuxTheme>(), t => t.Name == "My Theme" && !t.IsDark && t.Accent == "#FF112233");
        Assert.NotNull(settings.Get<StringCollection>("LinuxUserThemes"));

        // A fresh service over the same settings sees the saved theme and can
        // re-apply the persisted current theme at startup.
        ThemeService reloaded = new(settings);
        Assert.Contains(reloaded.UserThemes.OfType<LinuxTheme>(), t => t.Name == "My Theme");
        reloaded.ApplySavedTheme();
        Assert.False(reloaded.IsDarkTheme);
    }

    [AvaloniaFact]
    public void SetCurrentTheme_by_name_applies_variant_and_persists()
    {
        MemorySettings settings = new();
        ThemeService service = new(settings);
        Assert.True(service.IsDarkTheme);

        service.SetCurrentTheme("Skua Light"); // preset

        Assert.False(service.IsDarkTheme);
        Assert.Contains("light", settings.Get<string>("LinuxCurrentTheme") ?? "");
    }

    [AvaloniaFact]
    public void RemoveTheme_deletes_the_saved_entry()
    {
        MemorySettings settings = new();
        ThemeService service = new(settings);
        service.SaveTheme("Doomed");
        Assert.Contains(service.UserThemes.OfType<LinuxTheme>(), t => t.Name == "Doomed");

        service.RemoveTheme("Doomed");

        Assert.DoesNotContain(service.UserThemes.OfType<LinuxTheme>(), t => t.Name == "Doomed");
        StringCollection? stored = settings.Get<StringCollection>("LinuxUserThemes");
        Assert.NotNull(stored);
        Assert.DoesNotContain("Doomed|dark|" + ThemeService.DefaultAccent, stored!.Cast<string>());
    }

    [AvaloniaFact]
    public void ChangeCustomColor_sets_the_fluent_accent_resource()
    {
        ThemeService service = new(new MemorySettings());

        service.ChangeCustomColor("#FFAA5500");

        object? accent = global::Avalonia.Application.Current!.Resources["SystemAccentColor"];
        Assert.Equal(global::Avalonia.Media.Color.Parse("#FFAA5500"), accent);
    }

    [AvaloniaFact]
    public void Invalid_colors_and_unknown_themes_are_ignored()
    {
        MemorySettings settings = new();
        ThemeService service = new(settings);
        bool wasDark = service.IsDarkTheme;

        service.ChangeCustomColor("not-a-color");
        service.SetCurrentTheme("No Such Theme Exists");

        Assert.Equal(wasDark, service.IsDarkTheme);
    }
}
