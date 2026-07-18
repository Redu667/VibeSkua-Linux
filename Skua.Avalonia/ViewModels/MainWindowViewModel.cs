using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Skua.Core.ViewModels;

namespace Skua.Avalonia.ViewModels;

/// <summary>Which window a nav tab belongs in.</summary>
public enum NavScope
{
    /// <summary>Shown in both the manager and a bot client.</summary>
    Both,
    /// <summary>Manager only — army/account/launch/update tabs.</summary>
    Manager,
    /// <summary>Bot client only — per-game control tabs.</summary>
    Client,
}

/// <summary>
/// A single entry in the main window's navigation sidebar: a display title and
/// the (portable Skua.Core) ViewModel whose View the ViewLocator renders.
/// <see cref="Scope"/> controls whether it appears in the manager, a client, or both.
/// </summary>
public sealed record NavItem(string Title, object Content, NavScope Scope = NavScope.Both);

/// <summary>
/// Shell ViewModel for the main window. Holds the navigation items and the
/// current selection; the window binds its content area to
/// <c>Selected.Content</c>, which the <see cref="ViewLocator"/> renders. As more
/// views are ported, they register in DI and get added to <see cref="Items"/> —
/// this grows into the full tabbed shell that Skua.WPF's MainWindow provides.
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    public ObservableCollection<NavItem> Items { get; }

    [ObservableProperty]
    private NavItem? _selected;

    public MainWindowViewModel(
        AboutViewModel about,
        ChangeLogsViewModel changeLogs,
        Skua.Core.ViewModels.Manager.GoalsViewModel goals,
        SkillRulesViewModel skillRules,
        Skua.Core.ViewModels.Manager.AppUpdaterViewModel appUpdater,
        Skua.Core.ViewModels.Manager.LauncherViewModel launcher,
        GitHubAuthViewModel gitHubAuth,
        NotifyDropViewModel notifyDrop,
        ScriptStatsViewModel scriptStats,
        CurrentDropsViewModel currentDrops,
        BoostsViewModel boosts,
        ConsoleViewModel console,
        AutoViewModel auto,
        JumpViewModel jump,
        RegisteredQuestsViewModel registeredQuests,
        FastTravelViewModel fastTravel,
        ToPickupDropsViewModel toPickupDrops,
        LoadoutsViewModel loadouts,
        ScriptSchedulerViewModel scriptScheduler,
        PacketSpammerViewModel packetSpammer,
        ScriptRepoViewModel scriptRepo,
        RuntimeHelpersViewModel runtimeHelpers,
        AdvancedSkillsViewModel advancedSkills,
        ApplicationThemesViewModel applicationThemes,
        HotKeysViewModel hotKeys,
        PacketLoggerViewModel packetLogger,
        PacketInterceptorViewModel packetInterceptor,
        LogsViewModel logs,
        LoaderViewModel loader,
        GrabberViewModel grabber,
        PluginsViewModel plugins,
        GameOptionsViewModel gameOptions,
        ApplicationOptionsViewModel appOptions,
        CoreBotsViewModel coreBots,
        ScriptLoaderViewModel scriptLoader,
        JunkItemsViewModel junkItems,
        Skua.Core.ViewModels.Manager.AccountManagerViewModel accountManager,
        Skua.Core.ViewModels.Manager.ScriptUpdaterViewModel scriptUpdater,
        Skua.Core.ViewModels.Manager.ManagerOptionsViewModel managerOptions)
    {
        // ViewModels resolved through DI (Ioc.Default), mirroring Skua.App.WPF.
        // Order preserved (tests index into this list); NavScope tags which
        // window each tab belongs in — see ApplyScope.
        Items = new ObservableCollection<NavItem>
        {
            new("About", about),
            new("Change Logs", changeLogs),
            new("Goals", goals, NavScope.Manager),
            new("Skill Rules", skillRules, NavScope.Client),
            // Manager (army) surface — flat sidebar items, matching the rest of
            // the shell (not a tabs-in-a-tab bundle). "Accounts" is where you
            // add accounts + groups and start each one (auto-login army).
            new("Accounts", accountManager, NavScope.Manager),
            new("Launcher", launcher, NavScope.Manager),
            // The Linux app self-updates via Velopack (AppImage) — this is the
            // one real updater. (The Windows-only "Client Files" updater that
            // pulled Skua.exe from auqw/Skua is intentionally not surfaced.)
            new("Updates", appUpdater, NavScope.Manager),
            new("Script Updater", scriptUpdater, NavScope.Manager),
            new("Manager Options", managerOptions, NavScope.Manager),
            new("GitHub Auth", gitHubAuth),
            new("Notify Drop", notifyDrop, NavScope.Client),
            new("Stats", scriptStats, NavScope.Client),
            new("Current Drops", currentDrops, NavScope.Client),
            new("Boosts", boosts, NavScope.Client),
            new("Console", console, NavScope.Client),
            new("Auto Attack", auto, NavScope.Client),
            new("Jump", jump, NavScope.Client),
            new("Registered Quests", registeredQuests, NavScope.Client),
            new("Fast Travel", fastTravel, NavScope.Client),
            new("Pickup Drops", toPickupDrops, NavScope.Client),
            new("Loadouts", loadouts, NavScope.Client),
            new("Scheduler", scriptScheduler, NavScope.Client),
            new("Packet Spammer", packetSpammer, NavScope.Client),
            new("Scripts", scriptRepo, NavScope.Client),
            new("Runtime", runtimeHelpers, NavScope.Client),
            new("Advanced Skills", advancedSkills, NavScope.Client),
            // One themes entry, matching Windows (WPF exposes only "Application
            // Themes"). ApplicationThemesViewModel already hosts the theme
            // settings, color scheme, and background sub-panels in its own tabs,
            // so a separate standalone "Theme" tab was redundant.
            new("Themes", applicationThemes),
            // Both windows: the manager needs hotkeys too (the Army* hotkeys
            // broadcast to all clients — a manager-level control).
            new("Hotkeys", hotKeys),
            new("Packet Logger", packetLogger, NavScope.Client),
            new("Packet Interceptor", packetInterceptor, NavScope.Client),
            new("Logs", logs),
            new("Quest Loader", loader, NavScope.Client),
            new("Grabber", grabber, NavScope.Client),
            new("Plugins", plugins),
            new("Game Options", gameOptions, NavScope.Client),
            new("App Options", appOptions),
            new("CoreBots", coreBots, NavScope.Client),
            new("Script Loader", scriptLoader, NavScope.Client),
            new("Junk Items", junkItems, NavScope.Client),
            // The in-window AQW game surface (Ruffle). Self-contained — its View
            // owns the native renderer, so no DI dependency is threaded here.
            new("Game", new GameViewModel(), NavScope.Client),
        };
        Selected = Items[0];
    }

    /// <summary>
    /// Keep only the tabs relevant to the given window: a bot <paramref name="client"/>
    /// drops the manager/launch tabs (a client shouldn't launch more clients),
    /// and the manager drops the per-game bot tabs. <see cref="NavScope.Both"/>
    /// tabs stay in either. Applied after construction so the index-based tests
    /// (which build the full shell directly) are unaffected.
    /// </summary>
    public void ApplyScope(bool client)
    {
        NavScope drop = client ? NavScope.Manager : NavScope.Client;
        for (int i = Items.Count - 1; i >= 0; i--)
        {
            if (Items[i].Scope == drop)
                Items.RemoveAt(i);
        }
        if (Selected is null || !Items.Contains(Selected))
            Selected = Items.Count > 0 ? Items[0] : null;
    }
}
