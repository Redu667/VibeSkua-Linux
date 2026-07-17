using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;
using Skua.Avalonia.Services;
using Skua.Core.Interfaces;
using Skua.Core.Models;
using Skua.Core.ViewModels;
using Xunit;

namespace Skua.Avalonia.Tests;

/// <summary>
/// The Linux hotkey backend: WPF-compatible gesture parsing and first-run
/// seeding of the default bindings (the same "HotKeys" setting format both
/// platforms share).
/// </summary>
public class HotKeyServiceTests
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

    private static HotKeyService NewService(out MemorySettings settings)
    {
        settings = new MemorySettings();
        return new HotKeyService(new Dictionary<string, IRelayCommand>(), settings);
    }

    [Theory]
    [InlineData("Ctrl+F6", Key.F6, KeyModifiers.Control)]
    [InlineData("ctrl + shift + b", Key.B, KeyModifiers.Control | KeyModifiers.Shift)]
    [InlineData("Alt+2", Key.D2, KeyModifiers.Alt)]
    [InlineData("F6", Key.F6, KeyModifiers.None)]
    [InlineData("esc", Key.Escape, KeyModifiers.None)]
    [InlineData("Ctrl+Enter", Key.Return, KeyModifiers.Control)]
    public void ParseGesture_accepts_the_wpf_gesture_format(string gesture, Key key, KeyModifiers modifiers)
    {
        KeyGesture? parsed = HotKeyService.ParseGesture(gesture);
        Assert.NotNull(parsed);
        Assert.Equal(key, parsed!.Key);
        Assert.Equal(modifiers, parsed.KeyModifiers);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+")]
    [InlineData("NotAKey")]
    public void ParseGesture_rejects_invalid_input(string gesture)
        => Assert.Null(HotKeyService.ParseGesture(gesture));

    [Fact]
    public void ParseToHotKey_reports_modifier_flags()
    {
        HotKey? hk = NewService(out _).ParseToHotKey("Ctrl+Shift+F2");
        Assert.NotNull(hk);
        Assert.Equal("F2", hk!.Key);
        Assert.True(hk.Ctrl);
        Assert.True(hk.Shift);
        Assert.False(hk.Alt);
    }

    [Fact]
    public void GetHotKeys_seeds_the_default_bindings_on_first_run()
    {
        HotKeyService service = NewService(out MemorySettings settings);

        List<HotKeyItemViewModel> hotKeys = service.GetHotKeys<HotKeyItemViewModel>();

        Assert.Contains(hotKeys, hk => hk.Binding == "ToggleScript");
        Assert.Contains(hotKeys, hk => hk.Binding == "ToggleLagKiller" && hk.KeyGesture == "F6");
        // Seeded list is persisted so both platforms share the same setting.
        Assert.NotNull(settings.Get<System.Collections.Specialized.StringCollection>("HotKeys"));
        // Every entry carries the shared title/description metadata.
        Assert.All(hotKeys, hk => Assert.False(string.IsNullOrEmpty(hk.Title)));
    }
}
