using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using Skua.Core.Models;

namespace Skua.Avalonia.Services;

/// <summary>
/// Linux <see cref="IHotKeyService"/> — the WPF service ported onto Avalonia
/// input bindings: gestures from the shared "HotKeys" setting are parsed into
/// <see cref="KeyBinding"/>s bound to the engine's hotkey commands
/// (<c>Skua.Core.AppStartup.HotKeys.CreateHotKeys</c>) and registered on the
/// main window, exactly like WPF's <c>MainWindow.InputBindings</c>. Bindings
/// are window-scoped (they fire while the app has focus), matching Windows
/// behavior where hotkeys only run when Skua is the foreground process.
/// </summary>
public sealed class HotKeyService : IHotKeyService
{
    public HotKeyService(Dictionary<string, IRelayCommand> hotKeys, ISettingsService settingsService)
    {
        _hotKeys = hotKeys;
        _settingsService = settingsService;
    }

    private readonly Dictionary<string, IRelayCommand> _hotKeys;
    private readonly ISettingsService _settingsService;
    private readonly List<KeyBinding> _registeredBindings = new();

    private static Window? MainWindow =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    public void Reload()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(Reload);
            return;
        }

        ClearRegisteredBindings();

        Window? window = MainWindow;
        if (window is null)
            return;

        StringCollection hotkeys = LoadOrSeedHotKeys();
        foreach (string? hk in hotkeys)
        {
            if (string.IsNullOrEmpty(hk))
                continue;

            string[] split = hk.Split('|');
            if (split.Length < 2 || !_hotKeys.ContainsKey(split[0]))
                continue;

            string gesture = split[1].Trim();
            if (string.IsNullOrWhiteSpace(gesture) ||
                gesture.Equals("Unassigned", StringComparison.OrdinalIgnoreCase) ||
                gesture.Equals("Failed to bind", StringComparison.OrdinalIgnoreCase) ||
                gesture.Equals("Failed to bind.", StringComparison.OrdinalIgnoreCase))
                continue;

            KeyGesture? parsed = ParseGesture(gesture);
            if (parsed is null)
            {
                StrongReferenceMessenger.Default.Send<HotKeyErrorMessage>(new(split[0]));
                continue;
            }

            KeyBinding binding = new() { Gesture = parsed, Command = _hotKeys[split[0]] };
            window.KeyBindings.Add(binding);
            _registeredBindings.Add(binding);
        }
    }

    private void ClearRegisteredBindings()
    {
        Window? window = MainWindow;
        if (window is not null)
        {
            foreach (KeyBinding binding in _registeredBindings)
                window.KeyBindings.Remove(binding);
        }
        _registeredBindings.Clear();
    }

    public List<T> GetHotKeys<T>() where T : IHotKey, new()
    {
        StringCollection hotkeys = LoadOrSeedHotKeys();

        List<T> parsed = new();
        foreach (string? hk in hotkeys)
        {
            if (string.IsNullOrEmpty(hk))
                continue;
            string[] split = hk.Split('|');
            string gesture = split.Length > 1 ? split[1] : string.Empty;
            parsed.Add(new()
            {
                Binding = split[0],
                Title = Skua.Core.AppStartup.HotKeys.GetFormattedTitle(split[0]),
                Description = Skua.Core.AppStartup.HotKeys.GetDescription(split[0]),
                KeyGesture = gesture,
            });
        }
        return parsed;
    }

    public HotKey? ParseToHotKey(string keyGesture)
    {
        KeyGesture? kg = ParseGesture(keyGesture);
        return kg is null
            ? null
            : new HotKey(kg.Key.ToString(),
                kg.KeyModifiers.HasFlag(KeyModifiers.Control),
                kg.KeyModifiers.HasFlag(KeyModifiers.Alt),
                kg.KeyModifiers.HasFlag(KeyModifiers.Shift));
    }

    /// <summary>
    /// Same tolerant parsing as WPF's ParseToKeyBinding: modifiers are detected
    /// by substring ("ctrl"/"ctl"/"alt"/"shift"), the remainder is the key name.
    /// </summary>
    public static KeyGesture? ParseGesture(string keyGesture)
    {
        string ksc = keyGesture.ToLowerInvariant();
        KeyModifiers modifiers = KeyModifiers.None;

        if (ksc.Contains("alt"))
            modifiers |= KeyModifiers.Alt;
        if (ksc.Contains("shift"))
            modifiers |= KeyModifiers.Shift;
        if (ksc.Contains("ctrl") || ksc.Contains("ctl"))
            modifiers |= KeyModifiers.Control;

        string key = ksc
            .Replace("+", string.Empty)
            .Replace("alt", string.Empty)
            .Replace("shift", string.Empty)
            .Replace("ctrl", string.Empty)
            .Replace("ctl", string.Empty)
            .Trim();

        if (string.IsNullOrEmpty(key))
            return null;

        // Aliases WPF's KeyConverter accepted that the Key enum does not.
        key = key switch
        {
            "esc" => "Escape",
            "enter" => "Return",
            "del" => "Delete",
            "ins" => "Insert",
            "pgup" => "PageUp",
            "pgdn" or "pgdown" => "PageDown",
            "spacebar" => "Space",
            _ => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(key),
        };

        // Digit keys serialize as "D0".."D9" but users type "0".."9".
        if (key.Length == 1 && char.IsDigit(key[0]))
            key = "D" + key;

        return Enum.TryParse(key, ignoreCase: true, out Key parsedKey) && parsedKey != Key.None
            ? new KeyGesture(parsedKey, modifiers)
            : null;
    }

    private static readonly string[] DefaultHotKeys =
    {
        "ToggleScript", "LoadScript", "OpenBank", "OpenConsole", "ToggleAutoAttack", "ToggleAutoHunt", "ToggleLagKiller",
    };

    /// <summary>Load the persisted list, seeding the default entries on first run
    /// (same seeding rules as WPF's EnsureAllBindingsExist).</summary>
    private StringCollection LoadOrSeedHotKeys()
    {
        StringCollection? hotkeys = _settingsService.Get<StringCollection>("HotKeys");
        bool isFirstRun = hotkeys is null;
        hotkeys ??= new StringCollection();

        if (isFirstRun)
        {
            HashSet<string> existing = new();
            HashSet<string> usedGestures = new(StringComparer.OrdinalIgnoreCase);
            foreach (string? hk in hotkeys)
            {
                if (string.IsNullOrWhiteSpace(hk))
                    continue;
                string[] split = hk.Split('|');
                if (split.Length > 0 && !string.IsNullOrWhiteSpace(split[0]))
                    existing.Add(split[0]);
                if (split.Length > 1 && !string.IsNullOrWhiteSpace(split[1]))
                    usedGestures.Add(split[1]);
            }

            foreach (string key in DefaultHotKeys)
            {
                if (existing.Contains(key))
                    continue;
                string gesture = key == "ToggleLagKiller" && !usedGestures.Contains("F6") ? "F6" : string.Empty;
                hotkeys.Add($"{key}|{gesture}");
            }

            _settingsService.Set("HotKeys", hotkeys);
        }

        return hotkeys;
    }
}
