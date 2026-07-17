using System.Diagnostics;

namespace Skua.Core.Interfaces;

/// <summary>
/// Launches a new Skua bot-client instance for the multi-account / army flow.
/// </summary>
/// <remarks>
/// On Windows the manager launches a separate <c>Skua.exe</c> per client. On
/// Linux there is a single self-contained AppImage, so the implementation
/// relaunches the current executable in <c>--client</c> mode instead — each
/// spawned process opens its own game window, so several make an army.
/// When no implementation is registered, <see cref="ViewModels.Manager.LauncherViewModel"/>
/// falls back to its legacy <c>./Skua.exe</c> launch (keeping the WPF app
/// unchanged).
/// </remarks>
public interface IClientLauncher
{
    /// <summary>
    /// Start a new client instance.
    /// </summary>
    /// <param name="instanceName">
    /// Optional label (e.g. an account name) shown in the client's window title
    /// and status so multiple clients are distinguishable.
    /// </param>
    /// <param name="extraArgs">Additional command-line args to forward.</param>
    /// <returns>The started process, or <see langword="null"/> on failure.</returns>
    Process? LaunchClient(string? instanceName = null, IEnumerable<string>? extraArgs = null);
}
