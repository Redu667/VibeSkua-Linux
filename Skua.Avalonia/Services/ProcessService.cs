using System.Diagnostics;
using Skua.Core.Interfaces;

namespace Skua.Avalonia.Services;

/// <summary>
/// Linux <see cref="IProcessService"/>. Uses <see cref="ProcessStartInfo.UseShellExecute"/>,
/// which on Linux routes to <c>xdg-open</c> for links, and the <c>code</c> CLI
/// for VS Code. All calls are best-effort and never throw.
/// </summary>
public sealed class ProcessService : IProcessService
{
    public void OpenLink(string link)
    {
        if (string.IsNullOrWhiteSpace(link))
            return;
        try
        {
            Process.Start(new ProcessStartInfo(link) { UseShellExecute = true });
        }
        catch { /* no browser / xdg-open unavailable — ignore */ }
    }

    public void OpenVSC() => OpenVSC(".");

    public void OpenVSC(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("code", $"\"{path}\"") { UseShellExecute = false });
        }
        catch { /* VS Code CLI not installed — ignore */ }
    }
}
