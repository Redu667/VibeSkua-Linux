using CommunityToolkit.Mvvm.Messaging;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Skua.Core.GameProxy;
using Skua.Core.Interfaces;
using Skua.Core.Interfaces.Services;
using Skua.Core.Options;
using Skua.Core.Plugins;
using Skua.Core.Scripts;
using Skua.Core.Scripts.Helpers;
using Skua.Core.Services;
using Skua.Core.Skills;
using Skua.Core.Utils;
using Skua.Core.ViewModels;
using Skua.Core.ViewModels.Manager;
using System.Reflection;

namespace Skua.Core.AppStartup;

public static class Services
{
    public static IServiceCollection AddCommonServices(this IServiceCollection services)
    {
        services.AddTransient(typeof(Lazy<>), typeof(LazyInstance<>));

        services.AddSingleton(typeof(IMessenger), WeakReferenceMessenger.Default);
        services.AddSingleton(typeof(StrongReferenceMessenger), StrongReferenceMessenger.Default);

        services.AddSingleton<IDecamelizer, Decamelizer>();
        services.AddSingleton<IGetScriptsService, GetScriptsService>();
        services.AddSingleton<IProcessService, ProcessStartService>();
        services.AddSingleton<IDiscordWebhookService, DiscordWebhookService>();
        services.AddSingleton<ILoadoutService, LoadoutService>();

        return services;
    }

    public static IServiceCollection AddCompiler(this IServiceCollection services)
    {
        services.AddTransient(CreateCompiler);

        return services;
    }

    public static IServiceCollection AddScriptableObjects(this IServiceCollection services)
    {
        services.AddSingleton<IScriptInterface, ScriptInterface>();
        services.AddSingleton<IScriptManager, ScriptManager>();
        services.AddSingleton<IScriptStatus, ScriptManager>();

        services.AddSingleton<IScriptInventoryHelper, ScriptInventoryHelper>();
        services.AddSingleton<IScriptInventory, ScriptInventory>();
        services.AddSingleton<IScriptHouseInv, ScriptHouseInv>();
        services.AddSingleton<IScriptTempInv, ScriptTempInv>();
        services.AddSingleton<IScriptBank, ScriptBank>();

        services.AddSingleton<IAdvancedSkillContainer, AdvancedSkillContainer>();
        services.AddSingleton<IUltraBossHelper, UltraBossHelper>();
        services.AddSingleton<IScriptCombat, ScriptCombat>();
        services.AddSingleton<IScriptKill, ScriptKill>();
        services.AddSingleton<IScriptHunt, ScriptHunt>();
        services.AddSingleton<IScriptSkill, ScriptSkill>();
        services.AddSingleton<IScriptAuto, ScriptAuto>();
        services.AddSingleton<IScriptSelfAuras, ScriptSelfAuras>();
        services.AddSingleton<IScriptTargetAuras, ScriptTargetAuras>();

        services.AddSingleton<IScriptFaction, ScriptFaction>();
        services.AddSingleton<IScriptMonster, ScriptMonster>();
        services.AddSingleton<IScriptPlayer, ScriptPlayer>();
        services.AddSingleton<IScriptQuest, ScriptQuest>();
        services.AddSingleton<IScriptBoost, ScriptBoost>();
        services.AddSingleton<IScriptShop, ScriptShop>();
        services.AddSingleton<IScriptDrop, ScriptDrop>();
        services.AddSingleton<IScriptMap, ScriptMap>();

        services.AddSingleton<IScriptServers, ScriptServers>();
        services.AddSingleton<IScriptEvent, ScriptEvent>();
        services.AddSingleton<IScriptSend, ScriptSend>();

        services.AddTransient<IScriptOptionContainer, ScriptOptionContainer>();
        services.AddTransient<IOptionContainer, OptionContainer>();
        services.AddSingleton<IScriptOption, ScriptOption>();
        services.AddSingleton<IScriptLite, ScriptLite>();

        services.AddSingleton<IScriptBotStats, ScriptBotStats>();
        services.AddSingleton<IScriptHandlers, ScriptHandlers>();
        services.AddSingleton<IScriptWait, ScriptWait>();
        services.AddSingleton<IScriptAccounts, ScriptAccounts>();

        services.AddSingleton<ICaptureProxy, CaptureProxy>();

        services.AddSingleton<IPluginManager, PluginManager>();
        services.AddTransient<IPluginContainer, PluginContainer>();
        services.AddSingleton<IPluginHelper, PluginHelper>();

        services.AddSingleton<IMapService, MapService>();
        services.AddSingleton<ILogService, LogService>();
        services.AddSingleton<IQuestDataLoaderService, QuestDataLoaderService>();
        services.AddSingleton<IGrabberService, GrabberService>();
        services.AddSingleton<IClientFilesService, ClientFilesService>();
        services.AddSingleton<IAuraMonitorService, AuraMonitorService>();
        services.AddSingleton<IJunkService, JunkService>();
        services.AddSingleton<BackgroundThemeService>();

        return services;
    }

    public static IServiceCollection AddSkuaMainAppViewModels(this IServiceCollection services)
    {
        services.AddTransient<LoadoutsViewModel>();
        services.AddTransient<ChangeLogsViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton(MainMenu.CreateViewModel);
        services.AddTransient<BotWindowViewModel>();
        services.AddSingleton<IEnumerable<BotControlViewModelBase>>(s => new List<BotControlViewModelBase>()
        {
            s.GetRequiredService<ScriptLoaderViewModel>(),
            s.GetRequiredService<ScriptRepoViewModel>(),
            s.GetRequiredService<ScriptSchedulerViewModel>(),
            s.GetRequiredService<LogsViewModel>(),
            s.GetRequiredService<AutoViewModel>(),
            s.GetRequiredService<JumpViewModel>(),
            s.GetRequiredService<FastTravelViewModel>(),
            s.GetRequiredService<CurrentDropsViewModel>(),
            s.GetRequiredService<JunkItemsViewModel>(),
            s.GetRequiredService<RuntimeHelpersViewModel>(),
            s.GetRequiredService<LoaderViewModel>(),
            s.GetRequiredService<GrabberViewModel>(),
            s.GetRequiredService<GameOptionsViewModel>(),
            s.GetRequiredService<ApplicationOptionsViewModel>(),
            s.GetRequiredService<ConsoleViewModel>(),
            s.GetRequiredService<AdvancedSkillsViewModel>(),
            s.GetRequiredService<PacketInterceptorViewModel>(),
            s.GetRequiredService<PacketSpammerViewModel>(),
            s.GetRequiredService<PacketLoggerViewModel>(),
            s.GetRequiredService<ApplicationThemesViewModel>(),
            s.GetRequiredService<HotKeysViewModel>(),
            s.GetRequiredService<PluginsViewModel>()
        });

        services.AddTransient<LoaderViewModel>();

        services.AddTransient(Grabber.CreateViewModel);
        services.AddSingleton(Grabber.CreateListViewModels);

        services.AddSingleton<JumpViewModel>();

        services.AddSingleton<FastTravelViewModel>();
        services.AddTransient<FastTravelEditorViewModel>();
        services.AddTransient<FastTravelEditorDialogViewModel>();

        services.AddSingleton<LogsViewModel>();
        services.AddSingleton(LogTabs.CreateViewModels);

        services.AddSingleton(Options.CreateGameOptions);
        services.AddSingleton(Options.CreateAppOptions);

        services.AddSingleton(PacketLogger.CreateViewModel);
        services.AddSingleton<PacketSpammerViewModel>();
        services.AddSingleton(PacketInterceptor.CreateViewModel);

        services.AddTransient<ConsoleViewModel>();

        services.AddSingleton<ScriptRepoViewModel>();
        services.AddSingleton<ScriptLoaderViewModel>();
        services.AddSingleton<ScriptSchedulerViewModel>();

        services.AddSingleton<AdvancedSkillsViewModel>();
        services.AddSingleton<AdvancedSkillEditorViewModel>();
        services.AddSingleton<SavedAdvancedSkillsViewModel>();
        services.AddTransient<SkillRulesViewModel>();

        services.AddSingleton<AutoViewModel>();

        services.AddSingleton<BoostsViewModel>();
        services.AddSingleton<ScriptStatsViewModel>();
        services.AddSingleton<RuntimeHelpersViewModel>();
        services.AddSingleton<NotifyDropViewModel>();
        services.AddSingleton<ToPickupDropsViewModel>();
        services.AddSingleton<RegisteredQuestsViewModel>();
        services.AddSingleton<CurrentDropsViewModel>();
        services.AddSingleton<JunkItemsViewModel>();

        services.AddThemeViewModels();

        services.AddSingleton<PluginsViewModel>();

        services.AddSingleton<HotKeysViewModel>();
        services.AddSingleton(HotKeys.CreateHotKeys);

        services.AddSingleton(CoreBots.CreateViewModel);
        services.AddSingleton(CoreBots.CreateOptions);
        services.AddSingleton<CBOClassEquipmentViewModel>();
        services.AddSingleton<CBOClassSelectViewModel>();
        services.AddSingleton<CBOLoadoutViewModel>();

        return services;
    }

    public static IServiceCollection AddThemeViewModels(this IServiceCollection services)
    {
        services.AddSingleton<ApplicationThemesViewModel>();
        services.AddSingleton<ThemeSettingsViewModel>();
        services.AddSingleton<ColorSchemeEditorViewModel>();
        services.AddSingleton<BackgroundThemeViewModel>();

        return services;
    }

    public static IServiceCollection AddSkuaManagerViewModels(this IServiceCollection services)
    {
        services.AddThemeViewModels();
        services.AddSingleton<AccountManagerViewModel>();
        services.AddSingleton<LauncherViewModel>();
        services.AddSingleton<AppUpdaterViewModel>();
        services.AddSingleton<ScriptUpdaterViewModel>();
        services.AddSingleton<IClientUpdateService, ClientUpdateService>();
        services.AddSingleton<IClientFilesService, ClientFilesService>();
        services.AddSingleton<ClientUpdatesViewModel>();
        services.AddSingleton<GitHubAuthViewModel>();
        services.AddSingleton<ScriptRepoViewModel>(s =>
        {
            ScriptRepoViewModel vm = new(s.GetRequiredService<IGetScriptsService>(), s.GetRequiredService<IProcessService>())
            {
                IsManagerMode = true
            };
            return vm;
        });
        services.AddSingleton<GoalsViewModel>();
        services.AddSingleton<AboutViewModel>();
        services.AddSingleton<ChangeLogsViewModel>();
        services.AddSingleton(SkuaManager.CreateViewModel);
        services.AddSingleton(SkuaManager.CreateOptionsViewModel);

        return services;
    }

    /// <summary>
    /// Public factory for the Manager shell ViewModel, so hosts outside this
    /// assembly (e.g. the Avalonia app) can register it without reaching the
    /// internal <see cref="SkuaManager"/> helper.
    /// </summary>
    public static ManagerMainViewModel CreateManagerMainViewModel(IServiceProvider s)
        => SkuaManager.CreateViewModel(s);

    /// <summary>
    /// Public factory for the Manager options ViewModel (see
    /// <see cref="CreateManagerMainViewModel"/>).
    /// </summary>
    public static ManagerOptionsViewModel CreateManagerOptionsViewModel(IServiceProvider s)
        => SkuaManager.CreateOptionsViewModel(s);

    private static List<PortableExecutableReference>? _cachedBaseReferences;
    private static readonly object _referenceCacheLock = new();

    private static Compiler CreateCompiler(IServiceProvider s)
    {
        Compiler compiler = new();

        if (_cachedBaseReferences == null)
        {
            lock (_referenceCacheLock)
            {
                if (_cachedBaseReferences == null)
                {
                    // Collect a de-duplicated set of assembly paths to reference.
                    HashSet<string> refPaths = new(StringComparer.OrdinalIgnoreCase);

                    // The ENTIRE runtime (Trusted Platform Assemblies), so a script
                    // can `using` ANY BCL namespace (System.Net.Dns, System.Drawing,
                    // …) without us hand-listing each assembly — the AppDomain scan
                    // below only sees assemblies already LOADED, which misses BCL
                    // libs not yet touched before the first compile (a recurring
                    // "type X does not exist" on Linux). This is the standard Roslyn
                    // scripting approach.
                    if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string tpa)
                    {
                        foreach (string p in tpa.Split(Path.PathSeparator))
                        {
                            if (p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                                refPaths.Add(p);
                        }
                    }

                    // App/plugin assemblies (Skua.Core, Newtonsoft, CommunityToolkit,
                    // the WinForms shim in Skua.Flash.Linux, …) that aren't in the TPA.
                    foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        if (a.IsDynamic)
                            continue;
                        string loc = a.Location;
                        if (!string.IsNullOrEmpty(loc) && !loc.Contains("xunit"))
                            refPaths.Add(loc);
                    }

                    // Ensure the Linux WinForms/Drawing compat shim is referenced
                    // even if it hasn't loaded yet (bot scripts `using
                    // System.Windows.Forms;`). Absent on Windows (real WinForms).
                    string shimPath = Path.Combine(AppContext.BaseDirectory, "Skua.Flash.Linux.dll");
                    if (File.Exists(shimPath))
                        refPaths.Add(shimPath);

                    List<PortableExecutableReference> refs = new();
                    foreach (string p in refPaths)
                    {
                        if (!File.Exists(p))
                            continue;
                        try { refs.Add(MetadataReference.CreateFromFile(p)); }
                        catch { /* skip unreadable/native dlls */ }
                    }

                    _cachedBaseReferences = refs;
                }
            }
        }

        compiler.AddAssemblies(_cachedBaseReferences);
        compiler.AddNamespaces(new[]
        {
            "System",
            "System.Collections",
            "System.Collections.Generic",
            "System.Diagnostics",
            "System.Drawing",
            "System.Dynamic",
            "System.Globalization",
            "System.IO",
            "System.Linq",
            "System.Net",
            "System.Net.Http",
            "System.Reflection",
            "System.Runtime.CompilerServices",
            "System.Text",
            "System.Text.RegularExpressions",
            "System.Threading",
            "System.Threading.Tasks",
            "System.Timers",
            "System.Windows.Forms",
            "Skua.Core",
            "Skua.Core.Interfaces",
            "Skua.Core.Models",
            "Skua.Core.Models.Items",
            "Skua.Core.Models.Monsters",
            "Skua.Core.Models.Players",
            "Skua.Core.Models.Quests",
            "Skua.Core.Models.Servers",
            "Skua.Core.Models.Shops",
            "Skua.Core.Models.Skills",
            "Skua.Core.Models.Auras",
            "Skua.Core.Models.Factions",
            "Skua.Core.Options",
            "Skua.Core.Utils",
            "CommunityToolkit.Mvvm.DependencyInjection",
            "Newtonsoft.Json",
            "Newtonsoft.Json.Linq",
        });
        compiler.SaveGeneratedCode = true;
        return compiler;
    }
}