using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Skua.Core.AppStartup;
using Skua.Core.Interfaces;
using Skua.Flash.Linux;

namespace Skua.App.Console;

/// <summary>
/// Headless Linux smoke test for the native Flash stack.
///
/// Drives the real managed → native → managed path: it constructs the Linux
/// <see cref="IFlashUtil"/> (<see cref="RuffleFlashUtil"/>), which P/Invokes
/// <c>libskua_flash.so</c>, exercises the same <c>IFlashUtil</c> default methods
/// Skua scripts use (<c>GetGameObject&lt;T&gt;</c>, <c>IsNull</c>), and verifies
/// the AS3 → host callback direction. Against the bundled <c>MockRuntime</c> this
/// proves the entire Flash seam works natively on Linux; swapping in the real
/// ruffle_core-backed runtime is the only remaining step (see the bridge README).
/// </summary>
internal static class Program
{
    private static int _passed;
    private static int _failed;

    private static int Main()
    {
        System.Console.WriteLine("Skua native-Linux Flash bridge smoke test");
        System.Console.WriteLine("==========================================");

        IMessenger messenger = WeakReferenceMessenger.Default;
        using var flash = new RuffleFlashUtil(messenger);

        try
        {
            flash.InitializeFlash();
            System.Console.WriteLine("[ok]   InitializeFlash() — libskua_flash.so loaded and ABI matched");
            _passed++;
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"[FAIL] InitializeFlash() threw: {ex.Message}");
            System.Console.WriteLine("       Is libskua_flash.so next to the binary? (built by the csproj target)");
            return 1;
        }

        IFlashUtil bot = flash;

        // Layer 3b definition-of-done: read world.strMapName from "AS3" into C#.
        Check("GetGameObject<string>(\"world.strMapName\") == \"battleon\"",
            bot.GetGameObject<string>("world.strMapName") == "battleon");

        Check("GetGameObject<string>(\"world.strAreaName\") == \"Battleon\"",
            bot.GetGameObject<string>("world.strAreaName") == "Battleon");

        // Typed deserialization through a nested path.
        Check("GetGameObject<int>(\"world.myAvatar.objData.intHP\") == 1000",
            bot.GetGameObject<int>("world.myAvatar.objData.intHP") == 1000);

        Check("GetGameObject<int>(\"world.curRoom\") == 1",
            bot.GetGameObject<int>("world.curRoom") == 1);

        // Boolean via the "<string>true/false</string>" return, as real skua.swf does.
        Check("IsNull(\"world\") == false", bot.IsNull("world") == false);
        Check("IsNull(\"world.doesNotExist\") == true", bot.IsNull("world.doesNotExist") == true);

        // IsWorldLoaded is a default method built on IsNull("world").
        Check("IsWorldLoaded == true", bot.IsWorldLoaded);

        // AS3 -> host callback direction. Events are dispatched on a dedicated
        // thread (so handlers can re-enter Call() without deadlocking the render
        // worker), so delivery is asynchronous — wait for it instead of racing it.
        string? eventFn = null;
        flash.FlashCall += (fn, args) => Volatile.Write(ref eventFn, fn);
        flash.Call("__skua_emit_test_event__", "ping");
        for (int i = 0; i < 100 && Volatile.Read(ref eventFn) is null; i++)
            Thread.Sleep(20);
        Check("FlashCall event delivered (AS3 -> host)", Volatile.Read(ref eventFn) == "testEvent");

        // Script engine: Skua's real Roslyn/Westwind Compiler, natively on Linux.
        RunScriptEngineTest();

        // Full engine graph: the entire Skua Bot (IScriptInterface) via the real
        // Skua.Core DI registrations, backed by the Linux Flash util.
        RunFullBotGraphTest();

        System.Console.WriteLine("==========================================");
        System.Console.WriteLine($"Result: {_passed} passed, {_failed} failed");
        return _failed == 0 ? 0 : 1;
    }

    /// <summary>
    /// Compiles and runs a script class through Skua's own <see cref="Skua.Core.Compiler"/>
    /// (Roslyn + Westwind.Scripting) to prove the scripting pipeline is portable.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Uses the dynamic script compiler.")]
    private static void RunScriptEngineTest()
    {
        const string scriptCode = @"
public class SmokeScript
{
    public int Compute(int a, int b) => a + b;
    public string Greet() => ""compiled by Skua's Compiler on Linux"";
}";
        try
        {
            var compiler = new Skua.Core.Compiler();
            compiler.AddDefaultReferencesAndNamespaces();
            compiler.AllowReferencesInCode = true;

            dynamic? instance = compiler.CompileClass(scriptCode);
            if (compiler.Error || instance is null)
            {
                System.Console.WriteLine($"[FAIL] script compile: {compiler.ErrorMessage}");
                _failed++;
                return;
            }

            int sum = instance.Compute(2, 3);
            string greet = instance.Greet();
            Check($"Script compiled + ran ({greet}); Compute(2,3)={sum}", sum == 5);
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"[FAIL] script engine threw: {ex.Message}");
            _failed++;
        }
    }

    /// <summary>
    /// Builds the full Skua service graph using the real <c>Skua.Core.AppStartup</c>
    /// registrations (the same ones the Windows app uses), backed by the Linux
    /// <see cref="RuffleFlashUtil"/>, and resolves the aggregate bot
    /// <see cref="IScriptInterface"/> — proving the entire engine constructs and
    /// runs natively on Linux.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Uses the dynamic script compiler.")]
    private static void RunFullBotGraphTest()
    {
        try
        {
            ServiceCollection services = new();
            services.AddSingleton<IFlashUtil>(sp => new RuffleFlashUtil(
                sp.GetRequiredService<IMessenger>(),
                sp.GetService<Lazy<IScriptManager>>()));
            // Platform services the engine graph needs that Core's registrations
            // don't provide (on Windows these come from the app project).
            services.AddSingleton<ISettingsService, ConsoleSettingsService>();
            services.AddSingleton<IDialogService, StubDialogService>();
            services.AddCommonServices();
            services.AddCompiler();
            services.AddScriptableObjects();

            ServiceProvider provider = services.BuildServiceProvider();
            try
            {
                // Some services resolve dependencies through the global Ioc.Default.
                CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.ConfigureServices(provider);

                IScriptInterface bot = provider.GetRequiredService<IScriptInterface>();
                Check("Full Skua Bot (IScriptInterface) graph resolves on Linux", bot is not null);

                IFlashUtil flash = provider.GetRequiredService<IFlashUtil>();
                flash.InitializeFlash();
                string? map = flash.GetGameObject<string>("world.strMapName");
                Check($"Bot's IFlashUtil round-trips world.strMapName == \"battleon\" (got \"{map}\")",
                    map == "battleon");
            }
            finally
            {
                // Some singletons are IAsyncDisposable and cancel background tasks
                // on teardown; that is expected and irrelevant to the test result.
                try { ((IAsyncDisposable)provider).DisposeAsync().AsTask().GetAwaiter().GetResult(); }
                catch { /* teardown noise */ }
            }
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"[FAIL] full bot graph: {ex.GetType().Name}: {ex.Message}");
            _failed++;
        }
    }

    /// <summary>Minimal cross-platform <see cref="ISettingsService"/> for the
    /// headless test — wraps Skua.Core's cross-platform UnifiedSettingsService.</summary>
    private sealed class ConsoleSettingsService : ISettingsService
    {
        private readonly Skua.Core.Services.UnifiedSettingsService _s = new();
        public ConsoleSettingsService() => _s.Initialize(Skua.Core.Models.AppRole.Client);
        public T? Get<T>(string key) => _s.Get<T>(key);
        public T Get<T>(string key, T def) => _s.Get<T>(key, def);
        public void Set<T>(string key, T value) => _s.Set(key, value);
        public void Initialize(Skua.Core.Models.AppRole role) => _s.Initialize(role);
        public Skua.Core.Models.SharedSettings GetShared() => _s.GetShared();
        public Skua.Core.Models.ClientSettings GetClient() => _s.GetClient();
        public Skua.Core.Models.ManagerSettings GetManager() => _s.GetManager();
        public void SetApplicationVersion() => _s.SetApplicationVersion();
        public void ReloadSettings() => _s.ReloadSettings();
    }

    /// <summary>No-op <see cref="IDialogService"/> for the headless test (there is
    /// no UI). The Avalonia app supplies the real one.</summary>
    private sealed class StubDialogService : IDialogService
    {
        public bool? ShowDialog<TViewModel>(TViewModel vm) where TViewModel : class => null;
        public bool? ShowDialog<TViewModel>(TViewModel vm, string title) where TViewModel : class => null;
        public bool? ShowDialog<TViewModel>(TViewModel vm, Action<TViewModel> cb) where TViewModel : class => null;
        public void ShowMessageBox(string message, string caption) { }
        public bool? ShowMessageBox(string message, string caption, bool yesAndNo) => null;
        public Skua.Core.Models.DialogResult ShowMessageBox(string message, string caption, params string[] buttons)
            => Skua.Core.Models.DialogResult.Cancelled;
    }

    private static void Check(string label, bool condition)
    {
        if (condition)
        {
            System.Console.WriteLine($"[ok]   {label}");
            _passed++;
        }
        else
        {
            System.Console.WriteLine($"[FAIL] {label}");
            _failed++;
        }
    }
}
