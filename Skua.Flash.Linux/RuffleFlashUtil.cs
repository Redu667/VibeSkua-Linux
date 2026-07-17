using System.Collections.Concurrent;
using System.Dynamic;
using System.Security;
using System.Text;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Flash;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using Skua.Core.Utils;

namespace Skua.Flash.Linux;

/// <summary>
/// Linux <see cref="IFlashUtil"/> backed by <c>libskua_flash.so</c> (Ruffle).
/// </summary>
/// <remarks>
/// Behavioural twin of <c>Skua.WPF/Flash/FlashUtil.cs</c>: it builds the exact
/// same ExternalInterface <c>&lt;invoke&gt;</c> request XML and unwraps the exact
/// same response XML, but routes the two pipes through the native bridge instead
/// of the <c>AxShockwaveFlash</c> ActiveX control. Because the wire format is
/// identical, the same <c>skua.swf</c> and all of <see cref="IFlashUtil"/>'s
/// default methods (<c>GetGameObject</c>, <c>SetGameObject</c>, …) work unchanged.
/// </remarks>
public sealed class RuffleFlashUtil : IFlashUtil
{
    private readonly IMessenger _messenger;
    private readonly Lazy<IScriptManager>? _lazyManager;

    private nint _handle;
    private NativeMethods.SkuaFlashCallback? _callbackDelegate; // kept alive against GC
    // When bound, the bot drives the VISIBLE game (the GameView's live Ruffle
    // player) instead of a separate headless player — so the sidebar tabs act on
    // the game the user sees. Set by the game view once its renderer is ready.
    private GameRenderer? _renderer;
    private GameRenderer.FlashCallback? _renderCallback; // kept alive against GC
    private readonly object _flashLock = new();
    private readonly ConcurrentDictionary<string, (DateTime time, string value)> _callCache = new();
    private static byte[]? _cachedSwf;

    public event FlashCallHandler? FlashCall;

    public RuffleFlashUtil(IMessenger messenger, Lazy<IScriptManager>? manager = null)
    {
        _messenger = messenger;
        _lazyManager = manager;
    }

    public static void PreloadSwf()
    {
        if (_cachedSwf is null && ResolveSkuaSwf() is string path)
            _cachedSwf = File.ReadAllBytes(path);
    }

    /// <summary>
    /// Locate <c>skua.swf</c> on disk. The app bundles it next to the executable,
    /// but the process CWD is wherever the user launched the AppImage from — so a
    /// bare relative lookup silently fails and the bot goes inert. Probe the app
    /// base directory first, then the CWD (dev runs).
    /// </summary>
    public static string? ResolveSkuaSwf() =>
        ResolveSkuaSwf(AppContext.BaseDirectory, Environment.CurrentDirectory);

    public static string? ResolveSkuaSwf(params string[] directories)
    {
        foreach (string dir in directories)
        {
            if (string.IsNullOrEmpty(dir))
                continue;
            string candidate = Path.Combine(dir, "skua.swf");
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    /// <summary>Read the bundled <c>skua.swf</c>, or an empty array if missing.</summary>
    public static byte[] GetSkuaSwfBytes()
    {
        if (_cachedSwf is { Length: > 0 })
            return _cachedSwf;
        _cachedSwf = ResolveSkuaSwf() is string path ? File.ReadAllBytes(path) : Array.Empty<byte>();
        return _cachedSwf;
    }

    /// <summary>
    /// Whether the bot is wired to a live game (either a headless player via
    /// <see cref="InitializeFlash"/> or the visible game via <see cref="BindRenderer"/>).
    /// </summary>
    public bool IsInitialized => _handle != 0 || _renderer is not null;

    /// <summary>
    /// Route this <see cref="IFlashUtil"/> to the visible Ruffle player (the
    /// <see cref="GameView"/>'s <see cref="GameRenderer"/>, whose ROOT movie is
    /// <c>skua.swf</c>) and register the AS3→host (FlashCall) sink. Then perform
    /// the Skua startup handshake the Windows host does: once skua.swf's
    /// callbacks are live, call <c>loadClient</c> so it loads AQW into itself —
    /// only then does its API have the <c>game</c> reference bot calls resolve
    /// through. Called by the game session when its renderer is ready.
    /// </summary>
    public void BindRenderer(GameRenderer renderer)
    {
        EnsureDispatchThread();
        lock (_flashLock)
        {
            DestroyHandle(); // drop any headless player; the renderer is the game now
            _renderer = renderer;

            // AS3 → host sink (skua.swf's ExternalInterface.call(...)).
            _renderCallback = OnFlashCall;
            renderer.SetCallback(_renderCallback, 0);
        }

        // Handshake off-thread: wait for skua.swf's ExternalInterface callbacks
        // to register (its document class inits within the first few ticks),
        // then tell it to load the game. `isTrue` returns "true" only once the
        // callbacks are live, so it doubles as the readiness probe.
        Task.Run(() =>
        {
            const int maxAttempts = 100; // ~10s
            for (int i = 0; i < maxAttempts; i++)
            {
                lock (_flashLock)
                {
                    if (!ReferenceEquals(_renderer, renderer))
                        return; // unbound/rebound while waiting
                }
                if (Call<string>("isTrue") == "true")
                {
                    Log("Skua client bridge ready — loading the game (loadClient).");
                    Call("loadClient");
                    return;
                }
                Thread.Sleep(100);
            }
            Log("Skua client bridge did NOT come up — skua.swf callbacks never registered. " +
                "The game may still play, but the bot cannot drive it.");
        });
    }

    /// <summary>Best-effort log to the app's Logs tab (Script section), stderr,
    /// AND ~/.config/Skua/vibeskua-client.log — the on-disk copy is what makes
    /// bridge failures diagnosable from a user's machine.</summary>
    private static void Log(string message)
    {
        try
        {
            CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default
                .GetService<Skua.Core.Interfaces.ILogService>()?.ScriptLog(message);
        }
        catch { /* logging must never throw */ }
        Console.Error.WriteLine($"[skua-flash] {message}");
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

    /// <summary>Detach from the game renderer (e.g. when the game view stops).</summary>
    public void UnbindRenderer()
    {
        lock (_flashLock)
        {
            _renderer = null;
            _renderCallback = null;
        }
    }

    public void InitializeFlash()
    {
        EnsureDispatchThread();
        lock (_flashLock)
        {
            DestroyHandle();

            _handle = NativeMethods.skua_flash_create();
            if (_handle == 0)
                throw new InvalidOperationException("skua_flash_create() returned null; libskua_flash.so failed to initialize.");

            uint abi = NativeMethods.AbiVersion();
            if (abi != NativeMethods.ExpectedAbiVersion)
            {
                DestroyHandle();
                throw new InvalidOperationException(
                    $"libskua_flash.so ABI version {abi} does not match expected {NativeMethods.ExpectedAbiVersion}.");
            }

            // Keep the delegate referenced for the lifetime of the handle so the
            // GC does not collect the thunk the native side holds.
            _callbackDelegate = OnFlashCall;
            NativeMethods.skua_flash_set_callback(_handle, _callbackDelegate, 0);

            byte[] swf = GetSkuaSwfBytes();
            if (swf.Length > 0)
            {
                int rc = NativeMethods.skua_flash_load_swf(_handle, swf, (nuint)swf.Length);
                if (rc != 0)
                    throw new InvalidOperationException($"skua_flash_load_swf() failed with code {rc}.");
            }
        }
    }

    // AS3 → host events are DISPATCHED OFF the native callback thread. The
    // native callback runs on the render worker — the same thread that services
    // host → AS3 calls — so a FlashCall handler that calls back into Flash
    // (which much of Skua.Core does: the Windows ActiveX host delivered these
    // events re-entrantly) would deadlock the game forever: the handler blocks
    // waiting for the worker, which is blocked inside the handler. A dedicated
    // dispatch thread preserves event ORDER while letting handlers re-enter.
    private readonly System.Collections.Concurrent.BlockingCollection<(string fn, object[] args)> _flashEvents = new();
    private Thread? _dispatchThread;

    private void EnsureDispatchThread()
    {
        if (_dispatchThread is not null)
            return;
        _dispatchThread = new Thread(() =>
        {
            foreach ((string fn, object[] args) in _flashEvents.GetConsumingEnumerable())
            {
                try
                {
                    // Surface skua.swf's own diagnostics: its `debug` traffic and
                    // client-lifecycle events tell the whole boot story in the
                    // Logs tab (was previously invisible → undiagnosable).
                    if (fn == "debug")
                    {
                        // skua.swf wraps varargs in one array — flatten for readability.
                        object[] flat = args.Length == 1 && args[0] is object[] inner ? inner : args;
                        Log($"skua.swf: {string.Join(" ", flat.Select(a => a?.ToString()))}");
                    }
                    else if (fn is "requestLoadGame" or "pre-load" or "loaded")
                        Log($"skua.swf event: {fn}");

                    // Invoke each subscriber INDEPENDENTLY. A multicast Invoke stops
                    // at the first handler that throws, robbing every later
                    // subscriber of the event — which is how the game view's
                    // "loaded" tracking silently died when an engine handler threw.
                    if (FlashCall is FlashCallHandler handlers)
                    {
                        foreach (Delegate d in handlers.GetInvocationList())
                        {
                            try
                            {
                                ((FlashCallHandler)d)(fn, args);
                            }
                            catch (Exception ex)
                            {
                                Log($"FlashCall handler {d.Method.DeclaringType?.Name}.{d.Method.Name} threw on '{fn}': {ex.Message}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"FlashCall dispatch for '{fn}' threw: {ex.Message}");
                }
            }
        })
        {
            IsBackground = true,
            Name = "FlashCall Dispatch",
        };
        _dispatchThread.Start();
    }

    private void OnFlashCall(nint user, nint invokeXmlPtr)
    {
        try
        {
            string? xml = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(invokeXmlPtr);
            if (string.IsNullOrEmpty(xml))
                return;
            XElement el = XElement.Parse(xml);
            string function = el.Attribute("name")!.Value;
            object[] args = el.Elements().Select(FromFlashXml).ToArray();
            _flashEvents.Add((function, args));
        }
        catch
        {
            /* a malformed event must never crash the native callback */
        }
    }

    public string? Call(string function, params object[] args) => Call<string>(function, args);

    public T? Call<T>(string function, params object[] args)
    {
        try
        {
            object? o = Call(function, typeof(T), args);
            return o is not null ? (T)o : (T?)DefaultProvider.GetDefault<T>(typeof(T));
        }
        catch
        {
            return (T?)DefaultProvider.GetDefault<T>(typeof(T));
        }
    }

    public object? Call(string function, Type type, params object[] args)
    {
        if (_lazyManager?.Value.ShouldExit == true && Thread.CurrentThread.Name == "Script Thread")
            _lazyManager.Value.ScriptCts?.Token.ThrowIfCancellationRequested();

        try
        {
            StringBuilder req = new StringBuilder().Append($"<invoke name=\"{function}\" returntype=\"xml\">");
            if (args.Length > 0)
            {
                req.Append("<arguments>");
                foreach (object o in args)
                    req.Append(ToFlashXml(o));
                req.Append("</arguments>");
            }
            req.Append("</invoke>");

            string reqString = req.ToString();
            string? result;

            bool canCache = function is "getGameObject" or "getGameObjectS";
            if (canCache && _callCache.TryGetValue(reqString, out var cached) && (DateTime.Now - cached.time).TotalMilliseconds < 15)
            {
                result = cached.value;
            }
            else
            {
                lock (_flashLock)
                {
                    if (_renderer is not null)
                        result = _renderer.Call(reqString);
                    else if (_handle != 0)
                        result = NativeMethods.Call(_handle, reqString);
                    else
                        return default;
                }
                if (canCache && result is not null)
                {
                    if (_callCache.Count > 500)
                        _callCache.Clear();
                    _callCache[reqString] = (DateTime.Now, result);
                }
            }

            if (string.IsNullOrEmpty(result))
                return default;

            XElement el = XElement.Parse(result);
            return el.FirstNode is null ? default : Convert.ChangeType(el.FirstNode.ToString(), type);
        }
        catch (Exception e)
        {
            _messenger.Send(new FlashErrorMessage(e, function, args));
            return default;
        }
    }

    /// <summary>
    /// Serialise a .NET value into ExternalInterface request XML. Kept identical
    /// to <c>Skua.WPF FlashUtil.ToFlashXml</c> for wire compatibility.
    /// </summary>
    public static string ToFlashXml(object? o)
    {
        switch (o)
        {
            case null:
                return "<null/>";

            case bool b:
                return $"<{b.ToString().ToLower()}/>";

            case double:
            case float:
            case long:
            case int:
                return $"<number>{o}</number>";

            case ExpandoObject:
                StringBuilder sb = new StringBuilder().Append("<object>");
                foreach (KeyValuePair<string, object> kvp in (IDictionary<string, object>)o)
                    sb.Append($"<property id=\"{kvp.Key}\">{ToFlashXml(kvp.Value)}</property>");
                return sb.Append("</object>").ToString();

            default:
                if (o is Array arr)
                {
                    StringBuilder asb = new StringBuilder().Append("<array>");
                    int k = 0;
                    foreach (object el in arr)
                        asb.Append($"<property id=\"{k++}\">{ToFlashXml(el)}</property>");
                    return asb.Append("</array>").ToString();
                }
                return $"<string>{SecurityElement.Escape(o.ToString())}</string>";
        }
    }

    /// <summary>
    /// Parse an ExternalInterface value element into a .NET object. Kept
    /// identical to <c>Skua.WPF FlashUtil.FromFlashXml</c>.
    /// </summary>
    public object FromFlashXml(XElement el)
    {
        switch (el.Name.ToString())
        {
            case "number":
                return int.TryParse(el.Value, out int i) ? i : float.TryParse(el.Value, out float f) ? f : 0;

            case "true":
                return true;

            case "false":
                return false;

            case "null":
                return null!;

            case "array":
                return el.Elements().Select(FromFlashXml).ToArray();

            case "object":
                dynamic d = new ExpandoObject();
                el.Elements().ForEach(e => ((IDictionary<string, object>)d)[e.Attribute("id")!.Value] = FromFlashXml(e.Elements().First()));
                return d;

            default:
                return el.Value;
        }
    }

    public IFlashObject<T> CreateFlashObject<T>(string path)
        => new FlashObject<T>(Call<int>("lnkCreate", path), this);

    private void DestroyHandle()
    {
        if (_handle != 0)
        {
            NativeMethods.skua_flash_destroy(_handle);
            _handle = 0;
        }
        _callbackDelegate = null;
    }

    public void Dispose()
    {
        try
        {
            _flashEvents.CompleteAdding(); // lets the dispatch thread drain and exit
        }
        catch { /* already completed */ }
        lock (_flashLock)
        {
            DestroyHandle();
        }
    }
}
