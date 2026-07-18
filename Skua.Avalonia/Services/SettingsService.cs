using Skua.Core.Interfaces;
using Skua.Core.Models;
using Skua.Core.Services;

namespace Skua.Avalonia.Services;

/// <summary>
/// Linux <see cref="ISettingsService"/>. Identical to the Windows implementation:
/// a thin wrapper over <see cref="UnifiedSettingsService"/>, which lives in
/// Skua.Core and is fully cross-platform (JSON files under the user's app-data
/// directory).
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private readonly UnifiedSettingsService _unifiedService;

    public SettingsService()
    {
        _unifiedService = new UnifiedSettingsService();
        // One binary plays both Windows roles: --client windows are the bot
        // client (Skua.exe), the default window is the manager (Skua.Manager.exe).
        // The role picks which non-shared settings section receives writes.
        bool clientMode = Array.Exists(
            Environment.GetCommandLineArgs(),
            a => string.Equals(a, "--client", StringComparison.OrdinalIgnoreCase));
        _unifiedService.Initialize(clientMode ? AppRole.Client : AppRole.Manager);
    }

    public T? Get<T>(string key) => _unifiedService.Get<T>(key);

    public T Get<T>(string key, T defaultValue) => _unifiedService.Get<T>(key, defaultValue);

    public void Set<T>(string key, T value) => _unifiedService.Set(key, value);

    public void Initialize(AppRole role) => _unifiedService.Initialize(role);

    public SharedSettings GetShared() => _unifiedService.GetShared();

    public ClientSettings GetClient() => _unifiedService.GetClient();

    public ManagerSettings GetManager() => _unifiedService.GetManager();

    public void SetApplicationVersion() => _unifiedService.SetApplicationVersion();

    public void ReloadSettings() => _unifiedService.ReloadSettings();
}
