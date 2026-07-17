using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.DependencyInjection;
using Skua.Flash.Linux;

namespace Skua.Avalonia.Services;

/// <summary>
/// The live game for a client — owns the native <see cref="GameRenderer"/> and
/// the engine binding, independent of any view. A client process has exactly one
/// game, so this is a process-wide singleton. Keeping it out of
/// <see cref="Views.GameView"/> means switching sidebar tabs (which detaches the
/// view) no longer tears the game down: the bot keeps running and the engine
/// stays bound; the view just stops/starts *displaying* the session's frames.
/// </summary>
public sealed class GameSession : IDisposable
{
    // AQW's Flash loader — the bot-less FALLBACK root movie, used only when the
    // bundled skua.swf can't be found. Must be https (AQW uses
    // SharedObject.getLocal(secure)).
    public const string GameUrl = "https://game.aq.com/game/gamefiles/Loader3.swf?ver=a";
    // Nominal https origin for skua.swf when it boots as the ROOT movie (loaded
    // from local bytes). An https origin keeps origin-gated APIs (SharedObject
    // secure) behaving as on the real site.
    public const string SkuaRootUrl = "https://game.aq.com/game/skua.swf";
    public const int Width = 960;
    public const int Height = 580;

    private readonly object _lock = new();
    private GameRenderer? _renderer;

    public bool IsRunning
    {
        get { lock (_lock) return _renderer is not null; }
    }

    public string? InstanceLabel { get; set; }

    /// <summary>
    /// Human-readable boot progress, shown by the game view's status line —
    /// deliberately IN THE USER'S FACE (right above the game surface) because a
    /// silent black screen proved undiagnosable. Driven by skua.swf's lifecycle
    /// events; a watchdog flips it to an error pointing at the log files if the
    /// game never reports loaded.
    /// </summary>
    public string Status { get; private set; } = "";
    private volatile bool _gameLoaded;

    /// <summary>
    /// Start the game if it isn't already running. Renderer creation (loader
    /// fetch + wgpu init) happens off the UI thread; <paramref name="onReady"/>
    /// is invoked with the result (true = a live renderer, false = failed/no
    /// render support). Idempotent — a second call while running reports true.
    /// </summary>
    private bool _startPending;

    public void StartAsync(Action<bool> onReady)
    {
        lock (_lock)
        {
            if (_renderer is not null)
            {
                onReady(true);
                return;
            }
            // One start at a time. The game view's auto-start fires from BOTH
            // DataContextChanged and AttachedToVisualTree, and renderer creation
            // is async — without this flag both calls raced past the null check
            // and created TWO live game instances. The engine bound one and told
            // it (only it) to load the game; the view displayed the other, which
            // never got loadClient — so the user stared at a black skua.swf stage
            // while a hidden twin booted AQW, logged in, and played invisibly
            // (the fetch log showed every boot request doubled). The second
            // caller just reports success; the view's capture loop shows frames
            // as soon as the single real renderer is up.
            if (_startPending)
            {
                onReady(true);
                return;
            }
            _startPending = true;
        }

        Task.Run(() =>
        {
            // Boot the Skua client the way Windows does: skua.swf is the ROOT
            // movie; once the engine binds, it calls loadClient and skua.swf
            // loads AQW into itself — that's what gives the bot its `game`
            // reference. Without skua.swf we fall back to loading the game
            // directly: it plays, but no script can drive it.
            GameRenderer? renderer = null;
            byte[] swf = Skua.Flash.Linux.RuffleFlashUtil.GetSkuaSwfBytes();
            if (swf.Length > 0)
            {
                renderer = GameRenderer.TryCreateFromBytes(swf, SkuaRootUrl, Width, Height);
            }
            else
            {
                Log("skua.swf not found next to the app — starting the game WITHOUT bot support. " +
                    "Scripts will not be able to drive this game.");
            }
            renderer ??= GameRenderer.TryCreate(GameUrl, Width, Height);

            bool ok = renderer is not null && renderer.IsValid;
            if (ok)
            {
                lock (_lock)
                {
                    if (_renderer is null)
                    {
                        _renderer = renderer;
                    }
                    else
                    {
                        // Shouldn't happen with the pending flag, but never allow
                        // a second live game: the view and the engine must always
                        // share the ONE renderer.
                        renderer!.Dispose();
                        renderer = _renderer;
                    }
                }
                BindEngine(renderer!);
            }
            lock (_lock)
            {
                _startPending = false;
            }
            onReady(ok);
        });
    }

    private static void Log(string message)
    {
        try
        {
            Ioc.Default.GetService<Skua.Core.Interfaces.ILogService>()?.ScriptLog(message);
        }
        catch { /* logging must never throw */ }
        Console.Error.WriteLine($"[game-session] {message}");
        FileLog(message);
    }

    /// <summary>Append to ~/.config/Skua/vibeskua-client.log (timestamp + PID) —
    /// the C#-side counterpart of the native game log, so the managed lifecycle
    /// story (status transitions, lifecycle events, watchdog) survives on disk
    /// even when the user never opens the Logs tab. Best-effort.</summary>
    private static void FileLog(string message)
    {
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Skua");
            Directory.CreateDirectory(dir);
            long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            File.AppendAllText(
                Path.Combine(dir, "vibeskua-client.log"),
                $"[{ts} {Environment.ProcessId}] {message}\n");
        }
        catch { /* logging must never throw */ }
    }

    public int Capture(byte[] buffer, out int width, out int height)
    {
        GameRenderer? r;
        lock (_lock) r = _renderer;
        if (r is null) { width = 0; height = 0; return -1; }
        return r.Capture(buffer, out width, out height);
    }

    public void Mouse(int kind, double x, double y)
    {
        GameRenderer? r;
        lock (_lock) r = _renderer;
        r?.Mouse(kind, x, y);
    }

    public void Text(int codepoint)
    {
        GameRenderer? r;
        lock (_lock) r = _renderer;
        r?.Text(codepoint);
    }

    /// <summary>Explicitly stop the game (the Stop button / window close) — NOT
    /// called on tab switch. Unbinds the engine and disposes the renderer.</summary>
    public void Stop()
    {
        lock (_lock)
        {
            if (_renderer is null)
                return;
            UnbindEngine();
            _renderer.Dispose();
            _renderer = null;
        }
    }

    public void Dispose() => Stop();

    // --- engine wiring ------------------------------------------------------

    private void BindEngine(GameRenderer renderer)
    {
        try
        {
            if (Ioc.Default.GetService(typeof(Skua.Core.Interfaces.IFlashUtil)) is RuffleFlashUtil flash)
            {
                Status = "Skua client starting…";
                // Narrate the boot from skua.swf's own lifecycle events.
                flash.FlashCall += OnLifecycleEvent;
                flash.BindRenderer(renderer);
                StartLoadWatchdog();
            }
        }
        catch (Exception ex)
        {
            Status = $"Engine bind failed: {ex.Message}";
            Console.Error.WriteLine($"BindEngine failed: {ex}");
        }
    }

    private void OnLifecycleEvent(string function, object[] args)
    {
        switch (function)
        {
            case "requestLoadGame":
                Status = "Skua client ready — requesting AQW…";
                FileLog("lifecycle: requestLoadGame");
                break;
            case "pre-load":
                Status = "Downloading AQW… (no progress bar yet — can take a minute)";
                FileLog("lifecycle: pre-load");
                break;
            case "loaded":
                _gameLoaded = true;
                Status = "AQW loaded — log in!";
                FileLog("lifecycle: loaded — game view + engine share this game");
                break;
        }
    }

    /// <summary>If skua.swf never reports the game loaded, say so loudly instead
    /// of leaving a silent black screen.</summary>
    private void StartLoadWatchdog()
    {
        Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(90));
            if (!_gameLoaded && IsRunning)
            {
                Status = "AQW did not load. See ~/.config/Skua/vibeskua-game.log and vibeskua-ruffle.log";
                Log(Status);
            }
        });
    }

    private void UnbindEngine()
    {
        try
        {
            if (Ioc.Default.GetService(typeof(Skua.Core.Interfaces.IFlashUtil)) is RuffleFlashUtil flash)
            {
                flash.FlashCall -= OnLifecycleEvent;
                flash.UnbindRenderer();
            }
        }
        catch { /* best-effort */ }
        _gameLoaded = false;
        Status = "Stopped.";
    }
}
