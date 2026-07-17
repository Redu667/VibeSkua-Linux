using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.DependencyInjection;
using Skua.Core.Interfaces;
using Skua.Core.ViewModels;

namespace Skua.Avalonia.Services;

/// <summary>
/// Executes army bus messages against this client's engine — a direct port of
/// the WM_SKUA_* cases in Skua.App.WPF/EmbeddedMainWindow.xaml.cs::WndProc,
/// including the same /tmp side-channel files the Windows army features use
/// for payloads that don't fit in (wParam, lParam).
/// </summary>
public static class ArmyMessageHandler
{
    private const int WmSkuaStartScript = 0x0400 + 445;
    private const int WmSkuaStopScript = 0x0400 + 446;
    private const int WmSkuaLogin = 0x0400 + 447;
    private const int WmSkuaLogout = 0x0400 + 448;
    private const int WmSkuaJumpMap = 0x0400 + 449;
    private const int WmSkuaSetOption = 0x0400 + 450;
    private const int WmSkuaThrottle = 0x0400 + 452;
    private const int WmSkuaLoadScript = 0x0400 + 453;
    private const int WmSkuaArmyScheduler = 0x0400 + 454;
    private const int WmSkuaArmySchedulerStop = 0x0400 + 455;
    private const int WmSkuaJumpPlayer = 0x0400 + 457;

    public static void Handle(int msg, int wParam, int lParam)
    {
        switch (msg)
        {
            case WmSkuaLogin:
                Task.Run(() =>
                {
                    IScriptOption options = Ioc.Default.GetRequiredService<IScriptOption>();
                    IScriptServers servers = Ioc.Default.GetRequiredService<IScriptServers>();
                    string targetServer = string.IsNullOrWhiteSpace(options.ReloginServer) ? "Twilly" : options.ReloginServer;
                    servers.Relogin(targetServer);
                });
                break;

            case WmSkuaLogout:
                Ioc.Default.GetRequiredService<IScriptServers>().Logout();
                break;

            case WmSkuaJumpMap:
                Task.Run(() =>
                {
                    try
                    {
                        string tempFile = Path.Combine(Path.GetTempPath(), "skua_global_jump.txt");
                        if (!File.Exists(tempFile))
                            return;
                        string[] lines = File.ReadAllLines(tempFile);
                        string map = lines.Length > 0 ? lines[0].Trim() : "";
                        string cell = lines.Length > 1 && !string.IsNullOrWhiteSpace(lines[1]) ? lines[1].Trim() : "Enter";
                        string pad = "Spawn";
                        if (cell.Contains(','))
                        {
                            string[] cellParts = cell.Split(',');
                            cell = cellParts[0].Trim();
                            if (cellParts.Length > 1)
                                pad = cellParts[1].Trim();
                        }

                        IScriptMap mapService = Ioc.Default.GetRequiredService<IScriptMap>();
                        if (string.IsNullOrWhiteSpace(map) || mapService.Name.Equals(map.Split('-')[0], StringComparison.OrdinalIgnoreCase))
                            mapService.Jump(cell, pad, false);
                        else
                            mapService.Join(map, cell, pad, true, false);
                    }
                    catch { }
                });
                break;

            case WmSkuaJumpPlayer:
                Task.Run(() =>
                {
                    try
                    {
                        string tempFile = Path.Combine(Path.GetTempPath(), "skua_global_jump_player.txt");
                        if (!File.Exists(tempFile))
                            return;
                        string targetPlayer = File.ReadAllText(tempFile).Trim();
                        if (!string.IsNullOrWhiteSpace(targetPlayer))
                            Ioc.Default.GetRequiredService<IScriptPlayer>().Goto(targetPlayer);
                    }
                    catch { }
                });
                break;

            case WmSkuaLoadScript:
                Task.Run(() =>
                {
                    try
                    {
                        string tempFile = Path.Combine(Path.GetTempPath(), "skua_global_script.txt");
                        if (!File.Exists(tempFile))
                            return;
                        string targetScript = File.ReadAllText(tempFile);
                        if (string.IsNullOrWhiteSpace(targetScript) || !File.Exists(targetScript))
                            return;
                        ScriptLoaderViewModel sm = Ioc.Default.GetRequiredService<ScriptLoaderViewModel>();
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (sm.LoadScriptCommand.CanExecute(targetScript))
                                sm.LoadScriptCommand.Execute(targetScript);
                        });
                    }
                    catch { }
                });
                break;

            case WmSkuaSetOption:
                HandleSetOption(wParam, lParam == 1);
                break;

            case WmSkuaArmyScheduler:
                Task.Run(() =>
                {
                    try
                    {
                        string tempFile = Path.Combine(Path.GetTempPath(), "skua_global_playlist.json");
                        if (!File.Exists(tempFile))
                            return;
                        var data = JsonSerializer.Deserialize<List<ScriptSchedulerViewModel.SavedScriptItem>>(File.ReadAllText(tempFile));
                        ScriptSchedulerViewModel scheduler = Ioc.Default.GetRequiredService<ScriptSchedulerViewModel>();
                        Dispatcher.UIThread.Post(() =>
                        {
                            scheduler.ScriptQueue.Clear();
                            if (data is not null)
                            {
                                foreach (ScriptSchedulerViewModel.SavedScriptItem item in data)
                                {
                                    if (!File.Exists(item.Path))
                                        continue;
                                    ScriptItemViewModel vm = new(item.Path) { Id = item.Id };
                                    if (!string.IsNullOrEmpty(item.Name))
                                        vm.Name = item.Name;
                                    scheduler.ScriptQueue.Add(vm);
                                }
                            }
                            if (scheduler.ScriptQueue.Count > 0 && !scheduler.IsRunningQueue && scheduler.StartQueueCommand.CanExecute(null))
                                scheduler.StartQueueCommand.Execute(null);
                        });
                    }
                    catch { }
                });
                break;

            case WmSkuaArmySchedulerStop:
                Dispatcher.UIThread.Post(() =>
                {
                    ScriptSchedulerViewModel scheduler = Ioc.Default.GetRequiredService<ScriptSchedulerViewModel>();
                    if (scheduler.StopQueueCommand.CanExecute(null))
                        scheduler.StopQueueCommand.Execute(null);
                });
                break;

            case WmSkuaThrottle:
                {
                    bool throttle = wParam == 1;
                    IScriptInterface bot = Ioc.Default.GetRequiredService<IScriptInterface>();
                    if (!bot.Options.HeadlessMode)
                    {
                        try { bot.Flash?.SetGameObject("stage.frameRate", throttle ? 2 : 24); } catch { }
                    }
                }
                break;

            case WmSkuaStartScript:
                HandleSetOption(99, true);
                break;

            case WmSkuaStopScript:
                HandleSetOption(99, false);
                break;
        }
    }

    private static void HandleSetOption(int optionId, bool value)
    {
        IScriptOption options = Ioc.Default.GetRequiredService<IScriptOption>();
        options.IsIpcMessageProcessing = true;
        try
        {
            switch (optionId)
            {
                case 1: options.LagKiller = value; break;
                case 2: options.HeadlessMode = value; break;
                case 3: options.HidePlayers = value; break;
                case 4: options.DisableFX = value; break;
                case 5: options.InfiniteRange = value; break;
                case 6: options.Magnetise = value; break;
                case 7: options.SkipCutscenes = value; break;
                case 8: options.UseFunctionBasedSkills = value; break;
                case 9: options.StreamerMode = value; break;
                case 99: ToggleArmyScript(value); break;
            }
        }
        finally
        {
            options.IsIpcMessageProcessing = false;
        }
    }

    private static void ToggleArmyScript(bool start)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ScriptLoaderViewModel sm = Ioc.Default.GetRequiredService<ScriptLoaderViewModel>();
            ScriptSchedulerViewModel scheduler = Ioc.Default.GetRequiredService<ScriptSchedulerViewModel>();
            if (start)
            {
                if (sm.ScriptManager.ScriptRunning)
                    return;
                if (sm.ToggleScriptCommand.CanExecute(null))
                {
                    sm.ToggleScriptCommand.Execute(null);
                }
                else
                {
                    IScriptInterface bot = Ioc.Default.GetRequiredService<IScriptInterface>();
                    string ident = bot.Player.LoggedIn ? bot.Player.Username : "Offline Account";
                    ILogService log = Ioc.Default.GetRequiredService<ILogService>();
                    if (string.IsNullOrWhiteSpace(sm.ScriptManager.LoadedScript))
                        log.ScriptLog($"[{ident}] Army start ignored: No script loaded.");
                    else if (!bot.Player.LoggedIn)
                        log.ScriptLog($"[{ident}] Army start ignored: Account is not logged in.");
                    else
                        log.ScriptLog($"[{ident}] Army start ignored: Toggle command blocked.");
                }
            }
            else
            {
                if (scheduler.IsRunningQueue && scheduler.StopQueueCommand.CanExecute(null))
                    scheduler.StopQueueCommand.Execute(null);
                else if (sm.ScriptManager.ScriptRunning && sm.ToggleScriptCommand.CanExecute(null))
                    sm.ToggleScriptCommand.Execute(null);
            }
        });
    }
}
