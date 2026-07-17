using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using Skua.Core.Models;
using Skua.Core.Utils;
using System.Diagnostics;
using Timer = System.Timers.Timer;

namespace Skua.Core.ViewModels.Manager;

public partial class LauncherViewModel : BotControlViewModelBase, IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly IDispatcherService _dispatcherService;
    public RangedObservableCollection<ProcessInfo> SkuaProcesses { get; } = new();
    private Timer _timer;
    private bool _disposed;

    public LauncherViewModel(ISettingsService settingsService, IDispatcherService dispatcherService)
        : base("Launcher")
    {
        StrongReferenceMessenger.Default.Register<LauncherViewModel, UpdateStartedMessage>(this, TerminateProcesses);
        StrongReferenceMessenger.Default.Register<LauncherViewModel, AddProcessMessage>(this, AddProcessWithName);
        _settingsService = settingsService;
        _dispatcherService = dispatcherService;

        _timer = new Timer
        {
            Interval = 1000
        };
        _timer.Elapsed += RemoveStoppedCurrentProcess!;
        _timer.AutoReset = true;
        _timer.Start();
    }

    private async void TerminateProcesses(LauncherViewModel recipient, UpdateStartedMessage message)
    {
        foreach (ProcessInfo? procInfo in recipient.SkuaProcesses.ToList())
        {
            await StopProcess(procInfo);
        }

        message.Reply(recipient.SkuaProcesses.Count == 0);
    }

    private void AddProcessWithName(LauncherViewModel recipient, AddProcessMessage message)
    {
        if (message.Process != null)
        {
            ProcessInfo procInfo = new(message.Process, message.AccountName ?? $"Skua #{message.Process.Id}");
            _dispatcherService.Invoke(() => recipient.SkuaProcesses.Add(procInfo));
        }
    }

    [RelayCommand]
    public async Task LaunchSkua()
    {
        await Task.Run(() =>
        {
            List<string> args = new();
            if (_settingsService.Get("syncTheme", false))
            {
                args.Add("--use-theme");
                args.Add(_settingsService.Get("CurrentTheme", "no-theme"));
            }

            string token = _settingsService.Get("UserGitHubToken", string.Empty);
            if (!string.IsNullOrEmpty(token))
            {
                args.Add("--gh-token");
                args.Add(token);
            }

            try
            {
                // Prefer a platform launcher when one is registered. On Linux
                // there is no separate Skua.exe — IClientLauncher relaunches the
                // AppImage in --client mode, so each launch pops out an
                // independent game window (an army member). Falls back to the
                // legacy ./Skua.exe launch when unregistered (the WPF app).
                IClientLauncher? launcher = Ioc.Default.GetService<IClientLauncher>();
                Process? proc = launcher is not null
                    ? launcher.LaunchClient(extraArgs: args)
                    : Process.Start("./Skua.exe", args);
                if (proc != null)
                {
                    ProcessInfo procInfo = new(proc, $"Skua #{proc.Id}");
                    _dispatcherService.Invoke(() => SkuaProcesses.Add(procInfo));
                }
            }
            catch
            {
                IDialogService dialogService = Ioc.Default.GetService<IDialogService>()!;

                dialogService.ShowMessageBox("Failed to launch a Skua client.", "Error");
            }
        });
    }

    public void KillAllSkuaProcesses()
    {
        Task.Factory.StartNew(() =>
        {
            foreach (ProcessInfo procInfo in SkuaProcesses)
            {
                procInfo.Process.Kill();
                _dispatcherService.Invoke(() => SkuaProcesses.Remove(procInfo));
            }
        });
    }

    private void RemoveStoppedCurrentProcess(object source, System.Timers.ElapsedEventArgs e)
    {
        foreach (ProcessInfo? procInfo in SkuaProcesses.ToList())
        {
            if (procInfo.HasExited)
            {
                _dispatcherService.Invoke(() => SkuaProcesses.Remove(procInfo));
            }
        }
    }

    [RelayCommand]
    public async Task StopProcess(object? proc)
    {
        ProcessInfo? procInfo = proc as ProcessInfo;
        if (procInfo == null && proc is Process process)
        {
            // For backwards compatibility, find ProcessInfo by Process
            procInfo = SkuaProcesses.FirstOrDefault(p => p.Process == process);
        }

        if (procInfo == null)
            return;

        await Task.Run(() =>
        {
            procInfo.Process.Kill();
            _dispatcherService.Invoke(() => SkuaProcesses.Remove(procInfo));
        });
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Stop and dispose timer
                _timer?.Stop();
                _timer.Elapsed -= RemoveStoppedCurrentProcess!;
                _timer?.Dispose();

                // Unregister from messenger
                StrongReferenceMessenger.Default.Unregister<UpdateStartedMessage>(this);
                StrongReferenceMessenger.Default.Unregister<AddProcessMessage>(this);

                // Clear and kill any remaining processes
                foreach (ProcessInfo procInfo in SkuaProcesses)
                {
                    try
                    {
                        if (!procInfo.HasExited)
                            procInfo.Process.Kill();
                    }
                    catch { }
                }
                SkuaProcesses?.Clear();
            }
            _disposed = true;
        }
    }
}