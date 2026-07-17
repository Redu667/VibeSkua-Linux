using System.Collections.Generic;
using System.Diagnostics;
using Skua.Core.Interfaces;

namespace Skua.Avalonia.Services;

/// <summary>
/// Linux <see cref="IClientLauncher"/>: launches a new bot client by relaunching
/// THIS executable in <c>--client</c> mode. There is a single self-contained
/// AppImage rather than a separate manager + client binary, so an "army" is just
/// several client processes of the same image, each opening its own game window.
/// </summary>
public sealed class ClientLauncher : IClientLauncher
{
    public Process? LaunchClient(string? instanceName = null, IEnumerable<string>? extraArgs = null)
    {
        ProcessStartInfo psi = new()
        {
            FileName = SelfExecutable(),
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("--client");
        if (!string.IsNullOrWhiteSpace(instanceName))
        {
            psi.ArgumentList.Add("--instance");
            psi.ArgumentList.Add(instanceName);
        }
        if (extraArgs is not null)
            foreach (string a in extraArgs)
                psi.ArgumentList.Add(a);

        return Process.Start(psi);
    }

    /// <summary>
    /// The command to relaunch. Prefer <c>$APPIMAGE</c> (set by the AppImage
    /// runtime to the .AppImage path) so a packaged build re-spawns the whole
    /// image; fall back to the running executable for a plain <c>dotnet run</c>
    /// / published build.
    /// </summary>
    private static string SelfExecutable()
    {
        string? appImage = System.Environment.GetEnvironmentVariable("APPIMAGE");
        if (!string.IsNullOrEmpty(appImage))
            return appImage;
        return System.Environment.ProcessPath ?? "VibeSkuaLinux";
    }
}
