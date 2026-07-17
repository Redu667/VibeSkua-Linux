using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using Skua.Core.Models;

namespace Skua.Avalonia.Services;

/// <summary>
/// Linux twin of the WPF tray balloon notifications
/// (Skua.App.WPF/MainWindow.xaml.cs): script stopped / script error / relogin
/// events raise a desktop notification via <c>notify-send</c> (libnotify) —
/// but only while the main window is hidden or minimized, matching WPF which
/// only balloons when minimized to tray.
/// </summary>
public sealed class TrayNotifier
{
    public TrayNotifier()
    {
        StrongReferenceMessenger.Default.Register<TrayNotifier, ScriptStoppedMessage, int>(
            this, (int)MessageChannels.ScriptStatus, (r, _) => r.Notify("Script Stopped", string.Empty));
        StrongReferenceMessenger.Default.Register<TrayNotifier, ScriptErrorMessage, int>(
            this, (int)MessageChannels.ScriptStatus, (r, _) => r.Notify("Script Error", string.Empty));
        StrongReferenceMessenger.Default.Register<TrayNotifier, ReloginTriggeredMessage, int>(
            this, (int)MessageChannels.GameEvents, (r, _) =>
            {
                string who = "";
                try { who = Ioc.Default.GetService<IScriptPlayer>()?.Username ?? ""; }
                catch { }
                r.Notify("Relogin", string.IsNullOrEmpty(who) ? "Relogin triggered." : $"Relogin triggered for {who}.");
            });
    }

    private void Notify(string title, string body)
        => Dispatcher.UIThread.Post(() =>
        {
            var window = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            bool backgrounded = window is null
                || !window.IsVisible
                || window.WindowState == global::Avalonia.Controls.WindowState.Minimized;
            if (!backgrounded)
                return;

            try
            {
                Process.Start(new ProcessStartInfo("notify-send")
                {
                    ArgumentList = { "-a", "VibeSkua", title, body },
                    UseShellExecute = false,
                    RedirectStandardError = true,
                });
            }
            catch
            {
                // No libnotify on this system — notifications are best-effort.
            }
        });
}
