using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;

namespace Skua.Avalonia.Services;

/// <summary>
/// Client-mode startup for army members. The AccountManager relaunches this
/// AppImage with <c>-u/-p/-s</c> (credentials + server) and optionally
/// <c>--run-script &lt;path&gt;</c>; this wires those into the running client the
/// same way <c>Skua.WPF/SkuaStartupHandler</c> does on Windows:
///
///  * set login info immediately, and on the game's <c>loaded</c> event force a
///    re-login to the chosen server (so each army window logs into its own
///    account without the user typing anything), then
///  * if a script was passed, start it once logged in.
///
/// Registered once per client process; unhooks itself after firing.
/// </summary>
public sealed class ClientStartup
{
    private readonly string? _username;
    private readonly string? _password;
    private readonly string _server;
    private readonly string? _script;
    private IScriptInterface? _bot;
    private bool _done;

    public ClientStartup(string? username, string? password, string? server, string? script)
    {
        _username = username;
        _password = password;
        _server = string.IsNullOrWhiteSpace(server) ? "Twilly" : server!;
        _script = script;
    }

    public bool HasWork => !string.IsNullOrEmpty(_username) || !string.IsNullOrEmpty(_script);

    /// <summary>Parse the client args and return a handler, or null if there's
    /// nothing to auto-do (a plain manual client).</summary>
    public static ClientStartup? FromArgs(string[] args)
    {
        string? Get(string opt)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], opt, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return null;
        }

        var handler = new ClientStartup(
            Get("-u") ?? Get("--user"),
            Get("-p") ?? Get("--password"),
            Get("-s") ?? Get("--server"),
            Get("--run-script"));
        return handler.HasWork ? handler : null;
    }

    public void Attach()
    {
        try
        {
            _bot = Ioc.Default.GetService<IScriptInterface>();
            if (_bot is null)
                return;

            if (!string.IsNullOrEmpty(_username) && !string.IsNullOrEmpty(_password))
                _bot.Servers.SetLoginInfo(_username!, _password!);

            _bot.Flash.FlashCall += OnFlashCall;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ClientStartup.Attach failed: {ex}");
        }
    }

    private void OnFlashCall(string function, object[] args)
    {
        if (function != "loaded" || _done)
            return;
        _done = true;
        if (_bot is not null)
            _bot.Flash.FlashCall -= OnFlashCall;

        // Off the FlashCall dispatch thread so the (blocking) relogin + script
        // start don't stall event delivery.
        Task.Run(() =>
        {
            try
            {
                if (!string.IsNullOrEmpty(_username))
                    _bot!.Servers.EnsureRelogin(_server);

                if (!string.IsNullOrEmpty(_script))
                    StrongReferenceMessenger.Default.Send<StartScriptMessage, int>(
                        new(_script), (int)MessageChannels.ScriptStatus);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ClientStartup auto-login/script failed: {ex}");
            }
        });
    }
}
