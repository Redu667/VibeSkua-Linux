using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Skua.Avalonia.Services;
using Skua.Avalonia.ViewModels;
using Skua.Avalonia.Views;
using Skua.Core.AppStartup;
using Skua.Core.Interfaces;
using Skua.Core.ViewModels;
using Skua.Flash.Linux;

namespace Skua.Avalonia;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    private static bool _servicesConfigured;

    public override void OnFrameworkInitializationCompleted()
    {
        // Same DI approach as Skua.App.WPF: a Microsoft.Extensions ServiceProvider
        // exposed through CommunityToolkit's Ioc.Default. As more views are ported,
        // their ViewModels register here exactly as they do on Windows.
        // Ioc.Default is process-global and can only be configured once, so guard
        // it (headless test harnesses re-run app init per test in one process).
        if (!_servicesConfigured)
        {
            Ioc.Default.ConfigureServices(ConfigureServices());
            _servicesConfigured = true;
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            string[] args = desktop.Args ?? Array.Empty<string>();

            // Client mode: a launched army member. Same full tabbed shell as the
            // manager (so every bot tab — Script Loader, Console, … — is available
            // and drives THIS client's game), but it opens on the Game tab and
            // auto-starts AQW, and the game view binds the engine to its own live
            // player. The Launcher/AccountManager spawns these via `--client`.
            bool clientMode = HasFlag(args, "--client");
            string? label = GetOption(args, "--instance") ?? GetOption(args, "--account");

            // Show the window immediately, then build the ViewModel graph OFF the
            // UI thread and assign it as DataContext when ready.
            //
            // Several Skua.Core services do sync-over-async on first run — e.g.
            // AdvancedSkillContainer's constructor fetches the default skill-sets
            // file via UpdateSkillSetsFile().GetAwaiter().GetResult(). Resolving
            // the graph on the UI thread deadlocks there: the awaited HTTP
            // continuation is posted back to the UI thread's SynchronizationContext,
            // which is the very thread blocked on GetResult(). A thread-pool thread
            // has no such context, so the continuation runs and construction
            // completes. This is what caused the app to launch with no window on a
            // fresh install (the files exist after first run, which masked it).
            MainWindow window = new();
            if (clientMode && !string.IsNullOrEmpty(label))
                window.Title = $"VibeSkua — {label}";
            desktop.MainWindow = window;

            System.Threading.Tasks.Task
                .Run(() => Ioc.Default.GetRequiredService<MainWindowViewModel>())
                .ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        System.Console.Error.WriteLine($"Failed to build main ViewModel: {t.Exception}");
                        return;
                    }
                    MainWindowViewModel shell = t.Result;
                    // Keep only the tabs relevant to this window (a client has no
                    // launch/army tabs; the manager has no per-game bot tabs).
                    shell.ApplyScope(clientMode);
                    if (clientMode)
                    {
                        ConfigureClientShell(shell, label);
                        // Army auto-pilot: if the AccountManager launched us with
                        // credentials/script (-u/-p/-s/--run-script), log this
                        // window into its own account and start its script once the
                        // game loads. Attach BEFORE the game view triggers load so
                        // the 'loaded' event is caught.
                        ClientStartup.FromArgs(args)?.Attach();
                    }
                    global::Avalonia.Threading.Dispatcher.UIThread.Post(() => window.DataContext = shell);
                });
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// In client mode, open the shell on the Game tab, auto-start the game, and
    /// tag the instance so army members are distinguishable. The GameView binds
    /// the engine to its own live player once started, so the sidebar tabs drive
    /// this client's game.
    /// </summary>
    private static void ConfigureClientShell(MainWindowViewModel shell, string? label)
    {
        NavItem? gameItem = null;
        foreach (NavItem item in shell.Items)
        {
            if (item.Content is GameViewModel gvm)
            {
                gvm.AutoStart = true;
                gvm.InstanceLabel = label;
                gameItem = item;
                break;
            }
        }
        if (gameItem is not null)
            shell.Selected = gameItem;
    }

    private static bool HasFlag(string[] args, string flag)
    {
        foreach (string a in args)
            if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>Value following <paramref name="option"/> (e.g. <c>--instance Foo</c>), or null.</summary>
    private static string? GetOption(string[] args, string option)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], option, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }

    private static IServiceProvider ConfigureServices()
    {
        ServiceCollection services = new();

        // The Linux Flash backend + the full Skua engine graph, using the real
        // Skua.Core.AppStartup registrations (same as the Windows app). This makes
        // the aggregate IScriptInterface bot — and every game-bound ViewModel —
        // resolvable in the app, backed by libskua_flash.so.
        services.AddSingleton<IFlashUtil>(sp => new RuffleFlashUtil(
            sp.GetRequiredService<IMessenger>(),
            sp.GetService<Lazy<IScriptManager>>()));
        services.AddCommonServices();
        services.AddCompiler();
        services.AddScriptableObjects();
        // Register the full main-app ViewModel graph (options, grabber, packet
        // tools, logs, core bots, …) via the real Skua.Core factories, so every
        // remaining panel's ViewModel resolves in-app.
        services.AddSkuaMainAppViewModels();

        // Linux platform services (mirror the Windows registrations in
        // Skua.WPF/Services/ConfigureServices.cs). Registered AFTER the engine so
        // the Linux-tuned versions win where they overlap with Core's defaults
        // (e.g. IProcessService: Core's ProcessStartService doesn't use
        // UseShellExecute and throws on Linux; ours routes through xdg-open).
        services.AddSingleton<Skua.Core.Interfaces.IProcessService, Services.ProcessService>();
        services.AddSingleton<Skua.Core.Interfaces.IClipboardService, Services.ClipboardService>();
        services.AddSingleton<Skua.Core.Interfaces.IDispatcherService, Services.DispatcherService>();
        services.AddSingleton<Skua.Core.Interfaces.ISettingsService, Services.SettingsService>();
        services.AddSingleton<Skua.Core.Interfaces.IDialogService, Services.DialogService>();
        services.AddSingleton<Skua.Core.Interfaces.ISoundService, Services.SoundService>();
        services.AddSingleton<Skua.Core.Interfaces.IWindowService, Services.WindowService>();
        services.AddSingleton<Skua.Core.Interfaces.IFileDialogService, Services.FileDialogService>();
        services.AddSingleton<Skua.Core.Interfaces.IScreenshotService, Services.ScreenshotService>();
        services.AddSingleton<Skua.Core.Interfaces.IThemeService, Services.ThemeService>();
        services.AddSingleton<Skua.Core.Interfaces.IHotKeyService, Services.HotKeyService>();
        // Army/multi-client: launch new bot clients by relaunching this AppImage
        // in --client mode (Linux has one binary, not a separate Skua.exe).
        services.AddSingleton<Skua.Core.Interfaces.IClientLauncher, Services.ClientLauncher>();
        // The live game for this client — owns the renderer + engine binding,
        // independent of the Game view, so switching tabs doesn't tear it down.
        services.AddSingleton<Services.GameSession>();

        services.AddSingleton<MainWindowViewModel>();

        // Portable Skua.Core ViewModels. These are the ported views; the rest
        // slot in here as their Avalonia views are added.
        services.AddTransient<AboutViewModel>();
        services.AddTransient<ChangeLogsViewModel>();
        services.AddTransient<Skua.Core.ViewModels.Manager.GoalsViewModel>();
        // SkillRulesViewModel has a copy constructor SkillRulesViewModel(SkillRulesViewModel).
        // Microsoft.Extensions.DI greedily selects the constructor with the most
        // resolvable parameters, so a plain AddTransient<SkillRulesViewModel>()
        // makes it try to resolve itself -> circular dependency at startup. Bind
        // the DI-intended parameterless constructor explicitly. (The copy ctor is
        // only used for manual cloning, e.g. the skill-rule editor dialog.)
        services.AddTransient<SkillRulesViewModel>(_ => new SkillRulesViewModel());
        services.AddTransient<Skua.Core.ViewModels.Manager.AppUpdaterViewModel>();
        services.AddTransient<Skua.Core.ViewModels.Manager.LauncherViewModel>();
        services.AddTransient<GitHubAuthViewModel>();
        services.AddTransient<AdvancedSkillEditorViewModel>();
        services.AddTransient<NotifyDropViewModel>();
        // Game-bound ViewModels, resolved against the hosted engine graph.
        services.AddTransient<ScriptStatsViewModel>();
        services.AddTransient<CurrentDropsViewModel>();
        services.AddTransient<BoostsViewModel>();
        services.AddTransient<ConsoleViewModel>();
        services.AddTransient<AutoViewModel>();
        services.AddTransient<JumpViewModel>();
        services.AddTransient<RegisteredQuestsViewModel>();
        services.AddTransient<FastTravelViewModel>();
        services.AddTransient<ToPickupDropsViewModel>();
        services.AddTransient<LoadoutsViewModel>();
        services.AddTransient<ScriptSchedulerViewModel>();
        services.AddTransient<PacketSpammerViewModel>();
        services.AddTransient<ScriptRepoViewModel>();
        services.AddTransient<RuntimeHelpersViewModel>();
        services.AddTransient<SavedAdvancedSkillsViewModel>();
        services.AddTransient<AdvancedSkillsViewModel>();
        services.AddTransient<ThemeSettingsViewModel>();
        services.AddTransient<BackgroundThemeViewModel>();
        services.AddTransient<ColorSchemeEditorViewModel>();
        services.AddTransient<ApplicationThemesViewModel>();
        services.AddTransient<HotKeysViewModel>();

        // Skua.Manager (multi-account launcher / army control) ViewModels. On
        // Windows this is a separate executable; on Linux it rides in the same
        // Avalonia app and its views resolve through the ViewLocator. The
        // services it needs (client updates/files) are portable Skua.Core types.
        services.AddSingleton<Skua.Core.Interfaces.IClientUpdateService, Skua.Core.Services.ClientUpdateService>();
        services.AddSingleton<Skua.Core.Interfaces.IClientFilesService, Skua.Core.Services.ClientFilesService>();
        services.AddTransient<Skua.Core.ViewModels.Manager.AccountManagerViewModel>();
        services.AddTransient<Skua.Core.ViewModels.Manager.ScriptUpdaterViewModel>();
        services.AddTransient<Skua.Core.ViewModels.Manager.ClientUpdatesViewModel>();
        services.AddSingleton(Skua.Core.AppStartup.Services.CreateManagerOptionsViewModel);
        services.AddSingleton(Skua.Core.AppStartup.Services.CreateManagerMainViewModel);

        return services.BuildServiceProvider();
    }
}
