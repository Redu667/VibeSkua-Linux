using Skua.Core.Interfaces;
using Skua.Core.Models;

namespace Skua.Avalonia.Services;

/// <summary>
/// Minimal Linux <see cref="IHotKeyService"/>. Global hotkey capture on Linux
/// needs SharpHook or X11 grabs (a later refinement — see LINUX.md); this stub
/// lets hotkey-consuming ViewModels resolve and manage their configured bindings
/// without a live global-hook backend.
/// </summary>
public sealed class HotKeyService : IHotKeyService
{
    public void Reload() { }

    public List<T> GetHotKeys<T>() where T : IHotKey, new() => new();

    public HotKey? ParseToHotKey(string keyGesture) => null;
}
