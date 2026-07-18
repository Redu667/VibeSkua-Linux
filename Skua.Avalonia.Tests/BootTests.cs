using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Skua.Avalonia.ViewModels;
using Skua.Avalonia.Views;
using Skua.Core.Interfaces;
using Skua.Core.ViewModels;
using Skua.Core.ViewModels.Manager;
using Xunit;

namespace Skua.Avalonia.Tests;

/// <summary>
/// Runtime smoke tests: the app actually boots headless, DI resolves, and the
/// ViewLocator turns a portable Skua.Core ViewModel into its Avalonia View with
/// live data binding — the whole Layer 2 chain, verified without a display.
/// </summary>
public class BootTests
{
    private static MainWindowViewModel NewShell(out AboutViewModel about)
    {
        about = new AboutViewModel();
        return new MainWindowViewModel(
            about,
            new ChangeLogsViewModel(),
            new Skua.Core.ViewModels.Manager.GoalsViewModel(),
            new SkillRulesViewModel(),
            new Skua.Core.ViewModels.Manager.AppUpdaterViewModel(new Skua.Avalonia.Services.DispatcherService()),
            new Skua.Core.ViewModels.Manager.LauncherViewModel(
                new Skua.Avalonia.Services.SettingsService(),
                new Skua.Avalonia.Services.DispatcherService()),
            new GitHubAuthViewModel(
                new Skua.Avalonia.Services.ClipboardService(),
                new Skua.Avalonia.Services.ProcessService(),
                new Skua.Avalonia.Services.SettingsService()),
            new NotifyDropViewModel(new Skua.Avalonia.Services.SoundService()),
            // Game-bound VMs resolved from the app's hosted engine graph.
            Ioc.Default.GetRequiredService<ScriptStatsViewModel>(),
            Ioc.Default.GetRequiredService<CurrentDropsViewModel>(),
            Ioc.Default.GetRequiredService<BoostsViewModel>(),
            Ioc.Default.GetRequiredService<ConsoleViewModel>(),
            Ioc.Default.GetRequiredService<AutoViewModel>(),
            Ioc.Default.GetRequiredService<JumpViewModel>(),
            Ioc.Default.GetRequiredService<RegisteredQuestsViewModel>(),
            Ioc.Default.GetRequiredService<FastTravelViewModel>(),
            Ioc.Default.GetRequiredService<ToPickupDropsViewModel>(),
            Ioc.Default.GetRequiredService<LoadoutsViewModel>(),
            Ioc.Default.GetRequiredService<ScriptSchedulerViewModel>(),
            Ioc.Default.GetRequiredService<PacketSpammerViewModel>(),
            Ioc.Default.GetRequiredService<ScriptRepoViewModel>(),
            Ioc.Default.GetRequiredService<RuntimeHelpersViewModel>(),
            Ioc.Default.GetRequiredService<AdvancedSkillsViewModel>(),
            Ioc.Default.GetRequiredService<ApplicationThemesViewModel>(),
            Ioc.Default.GetRequiredService<HotKeysViewModel>(),
            Ioc.Default.GetRequiredService<PacketLoggerViewModel>(),
            Ioc.Default.GetRequiredService<PacketInterceptorViewModel>(),
            Ioc.Default.GetRequiredService<LogsViewModel>(),
            Ioc.Default.GetRequiredService<LoaderViewModel>(),
            Ioc.Default.GetRequiredService<GrabberViewModel>(),
            Ioc.Default.GetRequiredService<PluginsViewModel>(),
            Ioc.Default.GetRequiredService<GameOptionsViewModel>(),
            Ioc.Default.GetRequiredService<ApplicationOptionsViewModel>(),
            Ioc.Default.GetRequiredService<CoreBotsViewModel>(),
            Ioc.Default.GetRequiredService<ScriptLoaderViewModel>(),
            Ioc.Default.GetRequiredService<JunkItemsViewModel>(),
            Ioc.Default.GetRequiredService<Skua.Core.ViewModels.Manager.AccountManagerViewModel>(),
            Ioc.Default.GetRequiredService<Skua.Core.ViewModels.Manager.ScriptUpdaterViewModel>(),
            Ioc.Default.GetRequiredService<Skua.Core.ViewModels.Manager.ManagerOptionsViewModel>(),
            Ioc.Default.GetRequiredService<Skua.Core.ViewModels.Manager.ClientUpdatesViewModel>());
    }

    /// <summary>Select a nav item by title (robust to reordering).</summary>
    private static NavItem Nav(MainWindowViewModel shell, string title)
        => shell.Items.First(i => i.Title == title);

    [AvaloniaFact]
    public void MainWindowViewModel_resolves_from_the_container()
    {
        // The desktop entry point resolves the shell straight from the container
        // (App.OnFrameworkInitializationCompleted:
        //   Ioc.Default.GetRequiredService<MainWindowViewModel>()).
        // Headless tests skip that branch (no IClassicDesktopStyleApplicationLifetime),
        // so assert it explicitly — this is what catches DI resolution errors like
        // greedy-constructor cycles that constructing the shell with `new` hides.
        MainWindowViewModel shell = Ioc.Default.GetRequiredService<MainWindowViewModel>();
        Assert.NotNull(shell);
        Assert.NotEmpty(shell.Items);
    }

    [AvaloniaFact]
    public void MainWindow_resolves_AboutView_for_the_core_viewmodel()
    {
        MainWindowViewModel shell = NewShell(out AboutViewModel about);
        MainWindow window = new() { DataContext = shell };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        // The ViewLocator must have realized an AboutView inside the shell...
        AboutView? view = window.GetVisualDescendants().OfType<AboutView>().FirstOrDefault();
        Assert.NotNull(view);

        // ...and it must be bound to the exact ViewModel instance we supplied.
        Assert.Same(about, view!.DataContext);
    }

    [AvaloniaFact]
    public void Full_engine_graph_is_hosted_in_the_app()
    {
        // App init (run by the headless harness) wires the full Skua.Core engine
        // graph into Ioc.Default, backed by the Linux RuffleFlashUtil. Resolving
        // the aggregate bot proves the whole engine is hosted inside the app.
        IScriptInterface? bot = Ioc.Default.GetService<IScriptInterface>();
        Assert.NotNull(bot);
    }

    [AvaloniaFact]
    public void Platform_services_are_registered_and_command_runs()
    {
        // App init (run by the headless harness) configures Ioc.Default with the
        // Linux platform services.
        Assert.NotNull(Ioc.Default.GetService<IProcessService>());
        Assert.NotNull(Ioc.Default.GetService<IClipboardService>());

        // A service-backed command must execute without throwing (OpenLink is
        // best-effort and swallows the absent xdg-open in CI).
        GoalsViewModel goals = new();
        goals.OpenKofiLink.Execute(null);
    }

    [AvaloniaFact]
    public void Selecting_a_nav_item_swaps_the_rendered_view()
    {
        MainWindowViewModel shell = NewShell(out _);
        MainWindow window = new() { DataContext = shell };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(window.GetVisualDescendants().OfType<AboutView>().FirstOrDefault());

        // Navigate to the second item; the ViewLocator should swap in ChangeLogsView.
        shell.Selected = Nav(shell, "Change Logs");
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(window.GetVisualDescendants().OfType<ChangeLogsView>().FirstOrDefault());
        Assert.Null(window.GetVisualDescendants().OfType<AboutView>().FirstOrDefault());
    }

    [AvaloniaFact]
    public void ViewLocator_resolves_view_for_viewmodel_in_a_subnamespace()
    {
        // GoalsViewModel lives in Skua.Core.ViewModels.Manager; its view is flat
        // in Skua.Avalonia.Views. Exercises the ViewLocator's flat fallback.
        MainWindowViewModel shell = NewShell(out _);
        MainWindow window = new() { DataContext = shell };
        window.Show();

        shell.Selected = Nav(shell, "Goals");
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(window.GetVisualDescendants().OfType<GoalsView>().FirstOrDefault());
    }

    [AvaloniaFact]
    public void SkillRules_form_renders_and_nests_aura_check_views()
    {
        MainWindowViewModel shell = NewShell(out _);
        var rules = (SkillRulesViewModel)Nav(shell, "Skill Rules").Content;
        rules.AddAuraCheckCommand.Execute(null); // one aura-check row

        MainWindow window = new() { DataContext = shell };
        window.Show();

        shell.Selected = Nav(shell, "Skill Rules");
        Dispatcher.UIThread.RunJobs();

        // The form resolved...
        Assert.NotNull(window.GetVisualDescendants().OfType<SkillRulesView>().FirstOrDefault());
        // ...and the nested ItemsControl rendered the AuraCheckViewModel via the ViewLocator.
        Assert.NotNull(window.GetVisualDescendants().OfType<AuraCheckView>().FirstOrDefault());
    }

    [AvaloniaFact]
    public void AppUpdaterView_resolves_for_its_viewmodel()
    {
        MainWindowViewModel shell = NewShell(out _);
        MainWindow window = new() { DataContext = shell };
        window.Show();

        shell.Selected = Nav(shell, "Updates");
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(window.GetVisualDescendants().OfType<AppUpdaterView>().FirstOrDefault());
    }

    [AvaloniaFact]
    public void LauncherView_resolves_with_parent_bound_item_command()
    {
        // Exercises the $parent-cast command binding in the process ItemTemplate.
        MainWindowViewModel shell = NewShell(out _);
        MainWindow window = new() { DataContext = shell };
        window.Show();

        shell.Selected = Nav(shell, "Launcher");
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(window.GetVisualDescendants().OfType<LauncherView>().FirstOrDefault());
    }

    [AvaloniaFact]
    public void GitHubAuthView_resolves_for_its_viewmodel()
    {
        MainWindowViewModel shell = NewShell(out _);
        MainWindow window = new() { DataContext = shell };
        window.Show();

        shell.Selected = Nav(shell, "GitHub Auth");
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(window.GetVisualDescendants().OfType<GitHubAuthView>().FirstOrDefault());
    }

    [AvaloniaFact]
    public void SkillEditor_and_NotifyDrop_views_resolve()
    {
        MainWindowViewModel shell = NewShell(out _);
        MainWindow window = new() { DataContext = shell };
        window.Show();

        // The standalone skill editor is embedded in "Advanced Skills" (its
        // EditViewModel), so the editor view resolves from that composite.
        shell.Selected = Nav(shell, "Advanced Skills");
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(window.GetVisualDescendants().OfType<AdvancedSkillEditorView>().FirstOrDefault());

        shell.Selected = Nav(shell, "Notify Drop");
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(window.GetVisualDescendants().OfType<NotifyDropView>().FirstOrDefault());
    }

    [AvaloniaFact]
    public void Game_bound_StatsView_renders_from_the_hosted_engine()
    {
        // ScriptStatsViewModel is backed by IScriptBotStats from the live engine
        // graph — the full path: Avalonia view -> ViewModel -> engine service.
        MainWindowViewModel shell = NewShell(out _);
        MainWindow window = new() { DataContext = shell };
        window.Show();

        shell.Selected = Nav(shell, "Stats");
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(window.GetVisualDescendants().OfType<ScriptStatsView>().FirstOrDefault());

        shell.Selected = Nav(shell, "Current Drops");
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(window.GetVisualDescendants().OfType<CurrentDropsView>().FirstOrDefault());

        shell.Selected = Nav(shell, "Boosts");
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(window.GetVisualDescendants().OfType<BoostsView>().FirstOrDefault());

        shell.Selected = Nav(shell, "Console");
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(window.GetVisualDescendants().OfType<ConsoleView>().FirstOrDefault());

        shell.Selected = Nav(shell, "Auto Attack");
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(window.GetVisualDescendants().OfType<AutoView>().FirstOrDefault());

        shell.Selected = Nav(shell, "Jump");
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(window.GetVisualDescendants().OfType<JumpView>().FirstOrDefault());

        shell.Selected = Nav(shell, "Registered Quests");
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(window.GetVisualDescendants().OfType<RegisteredQuestsView>().FirstOrDefault());

        shell.Selected = Nav(shell, "Fast Travel");
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(window.GetVisualDescendants().OfType<FastTravelView>().FirstOrDefault());

        shell.Selected = Nav(shell, "Pickup Drops");
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(window.GetVisualDescendants().OfType<ToPickupDropsView>().FirstOrDefault());

        shell.Selected = Nav(shell, "Loadouts");
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(window.GetVisualDescendants().OfType<LoadoutsView>().FirstOrDefault());

        shell.Selected = Nav(shell, "Scheduler");
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(window.GetVisualDescendants().OfType<ScriptSchedulerView>().FirstOrDefault());

        shell.Selected = Nav(shell, "Packet Spammer");
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(window.GetVisualDescendants().OfType<PacketSpammerView>().FirstOrDefault());

        shell.Selected = Nav(shell, "Scripts");
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(window.GetVisualDescendants().OfType<ScriptRepoView>().FirstOrDefault());

        shell.Selected = Nav(shell, "Runtime");
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(window.GetVisualDescendants().OfType<RuntimeHelpersView>().FirstOrDefault());
        // The composite renders a nested ported view via the ViewLocator.
        Assert.NotNull(window.GetVisualDescendants().OfType<ToPickupDropsView>().FirstOrDefault());

        shell.Selected = Nav(shell, "Advanced Skills");
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(window.GetVisualDescendants().OfType<AdvancedSkillsView>().FirstOrDefault());
        Assert.NotNull(window.GetVisualDescendants().OfType<SavedAdvancedSkillsView>().FirstOrDefault());

        // "Themes" hosts the theme settings sub-panel (there is no separate
        // "Theme" tab — it was a redundant duplicate).
        shell.Selected = Nav(shell, "Themes");
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(window.GetVisualDescendants().OfType<ApplicationThemesView>().FirstOrDefault());
        Assert.NotNull(window.GetVisualDescendants().OfType<ThemeSettingsView>().FirstOrDefault());

        shell.Selected = Nav(shell, "Hotkeys");
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(window.GetVisualDescendants().OfType<HotKeysView>().FirstOrDefault());

        shell.Selected = Nav(shell, "Packet Logger");
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(window.GetVisualDescendants().OfType<PacketLoggerView>().FirstOrDefault());

        shell.Selected = Nav(shell, "Packet Interceptor");
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(window.GetVisualDescendants().OfType<PacketInterceptorView>().FirstOrDefault());

        shell.Selected = Nav(shell, "Logs");
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(window.GetVisualDescendants().OfType<LogsView>().FirstOrDefault());

        shell.Selected = Nav(shell, "Quest Loader");
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(window.GetVisualDescendants().OfType<LoaderView>().FirstOrDefault());

        shell.Selected = Nav(shell, "Grabber");
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(window.GetVisualDescendants().OfType<GrabberView>().FirstOrDefault());

        shell.Selected = Nav(shell, "Plugins");
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(window.GetVisualDescendants().OfType<PluginsView>().FirstOrDefault());

        shell.Selected = Nav(shell, "Game Options");
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(window.GetVisualDescendants().OfType<GameOptionsView>().FirstOrDefault());

        shell.Selected = Nav(shell, "App Options");
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(window.GetVisualDescendants().OfType<ApplicationOptionsView>().FirstOrDefault());

        shell.Selected = Nav(shell, "CoreBots");
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(window.GetVisualDescendants().OfType<CoreBotsView>().FirstOrDefault());

        shell.Selected = Nav(shell, "Script Loader");
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(window.GetVisualDescendants().OfType<ScriptLoaderView>().FirstOrDefault());

        shell.Selected = Nav(shell, "Junk Items");
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(window.GetVisualDescendants().OfType<JunkItemsView>().FirstOrDefault());
    }

    [AvaloniaFact]
    public void InputDialog_view_resolves_via_the_viewlocator()
    {
        // Dialogs are hosted by ContentControl + ViewLocator; verify the input
        // dialog view resolves for its (manually-constructed) ViewModel.
        InputDialogViewModel vm = new("Save", "Enter name:", false);
        ContentControl host = new() { Content = vm };
        global::Avalonia.Controls.Window window = new() { Content = host, Width = 320, Height = 160 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(window.GetVisualDescendants().OfType<InputDialogView>().FirstOrDefault());
    }

    [AvaloniaFact]
    public void Dialog_views_resolve_via_the_viewlocator()
    {
        // Every dialog ViewModel must map to its Avalonia view through the
        // ContentControl + ViewLocator seam the dialog host relies on.
        object[] vms =
        {
            new MessageBoxDialogViewModel("Are you sure?", "Confirm", true),
            new CustomDialogViewModel("Pick one", "Choose", new[] { "Alpha", "Beta" }),
            new AssignHotKeyDialogViewModel("Assign hotkey"),
            new SkillRuleEditorDialogViewModel(new SkillRulesViewModel()),
            new FastTravelEditorDialogViewModel(
                new FastTravelEditorViewModel(
                    Ioc.Default.GetRequiredService<IMapService>(),
                    new global::CommunityToolkit.Mvvm.Input.RelayCommand<object>(_ => { }))),
            new SelectGroupDialogViewModel(new[] { new GroupItemViewModel("Group 1") }),
        };

        System.Type[] expected =
        {
            typeof(MessageBoxDialogView),
            typeof(CustomDialogView),
            typeof(AssignHotKeyDialogView),
            typeof(SkillRuleEditorDialogView),
            typeof(FastTravelEditorDialogView),
            typeof(SelectGroupDialogView),
        };

        for (int i = 0; i < vms.Length; i++)
        {
            ContentControl host = new() { Content = vms[i] };
            global::Avalonia.Controls.Window window = new() { Content = host, Width = 360, Height = 260 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.True(
                window.GetVisualDescendants().Any(v => v.GetType() == expected[i]),
                $"ViewLocator did not resolve {expected[i].Name} for {vms[i].GetType().Name}");
        }
    }

    [AvaloniaFact]
    public void Manager_shell_resolves_its_tab_views_from_the_hosted_graph()
    {
        // The Skua.Manager (multi-account launcher) rides in the same Avalonia
        // app on Linux. Resolve its shell from the app's DI and confirm the
        // ViewLocator realizes ManagerMainView and, on selecting each tab, the
        // ported Manager tab views (Accounts, Scripts, Options, ...).
        ManagerMainViewModel manager = Ioc.Default.GetRequiredService<ManagerMainViewModel>();
        ContentControl host = new() { Content = manager };
        global::Avalonia.Controls.Window window = new() { Content = host, Width = 800, Height = 500 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(window.GetVisualDescendants().OfType<ManagerMainView>().FirstOrDefault());

        System.Type[] tabViews =
        {
            typeof(AccountManagerView),
            typeof(ScriptUpdaterView),
            typeof(ManagerOptionsView),
        };
        foreach (System.Type tabView in tabViews)
        {
            TabItemViewModel? tab = manager.Tabs.FirstOrDefault(t => t.Content.GetType().Name
                .Replace("ViewModel", "View", System.StringComparison.Ordinal) == tabView.Name);
            Assert.NotNull(tab);
            manager.SelectedTab = tab!;
            Dispatcher.UIThread.RunJobs();
            Assert.True(
                window.GetVisualDescendants().Any(v => v.GetType() == tabView),
                $"ViewLocator did not resolve {tabView.Name} for the selected Manager tab");
        }
    }

    [AvaloniaFact]
    public void Theme_toggle_flips_the_application_variant()
    {
        var theme = new Skua.Avalonia.Services.ThemeService(new Skua.Avalonia.Services.SettingsService());
        theme.IsDarkTheme = false;
        Assert.Equal(global::Avalonia.Styling.ThemeVariant.Light, global::Avalonia.Application.Current!.RequestedThemeVariant);
        theme.IsDarkTheme = true;
        Assert.Equal(global::Avalonia.Styling.ThemeVariant.Dark, global::Avalonia.Application.Current!.RequestedThemeVariant);
    }

    [AvaloniaFact]
    public void Scripts_using_System_Windows_Forms_compile_on_linux()
    {
        // CoreBots and most AQW scripts `using System.Windows.Forms;` (MessageBox,
        // Clipboard, …). Linux has no such assembly, so without the shim the script
        // compiler fails at the using and every dependent type (CoreBots, …)
        // cascades to "not found" — exactly the error a tester hit. The shim in
        // Skua.Flash.Linux must make this compile through the REAL compiler
        // (base references scanned from the AppDomain + the default namespaces).
        var compiler = Ioc.Default.GetRequiredService<global::Skua.Core.Compiler>();
        compiler.AddDefaultReferencesAndNamespaces();

        // Mirrors the WinForms surface CoreBots.cs actually uses (a progress-bar
        // splash): Form/ProgressBar/DockStyle/Application.Run + System.Drawing
        // (Size from Primitives, ColorTranslator/Font from the shim).
        // Mirrors the surface CoreBots.cs uses: the WinForms splash (shim), plus
        // System.Drawing (Size/ColorTranslator from Primitives, Font from the shim)
        // and an arbitrary BCL type (System.Net.Dns) that exercises the full-runtime
        // (TPA) reference set — the same class of "type X not found" as the shim.
        const string code = @"
using System.Windows.Forms;
using System.Drawing;
using System.Net;
public class Test {
    public string Run() {
        var form = new Form { Text = ""x"", Size = new Size(300, 100),
            StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog };
        var bar = new ProgressBar { Dock = DockStyle.Top, Style = ProgressBarStyle.Continuous, Value = 50 };
        form.Controls.Add(bar);
        form.BackColor = ColorTranslator.FromHtml(""#101010"");
        form.Font = new Font(""Arial"", 10);
        System.Windows.Forms.Application.Run(form);
        var r = MessageBox.Show(""hello"", ""cap"", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
        Clipboard.SetText(""x"");
        _ = Dns.GetHostName();
        return r.ToString() + Clipboard.GetText() + bar.Value;
    }
}";
        dynamic? instance = compiler.CompileClass(code);
        Assert.False(compiler.Error, $"compile failed: {compiler.ErrorMessage}");
        Assert.NotNull(instance);
        Assert.Equal("Yesx50", (string)instance!.Run());
    }

    [AvaloniaFact]
    public void Windows_platform_gates_are_neutralized_on_linux()
    {
        // CoreBots stops the bot on non-Windows via
        //   if (!OperatingSystem.IsWindowsVersionAtLeast(10)) { ...stop... }
        // The compiler rewrites those probes to `true` on Linux so scripts run.
        var compiler = Ioc.Default.GetRequiredService<global::Skua.Core.Compiler>();
        compiler.AddDefaultReferencesAndNamespaces();
        const string code = @"
public class Test {
    public string Run() {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10)) return ""blocked"";
        if (!OperatingSystem.IsWindows()) return ""blocked2"";
        return ""ok"";
    }
}";
        dynamic? instance = compiler.CompileClass(code);
        Assert.False(compiler.Error, $"compile failed: {compiler.ErrorMessage}");
        Assert.Equal("ok", (string)instance!.Run());
    }

    [AvaloniaFact]
    public void Neutralized_source_changes_the_disk_cache_key()
    {
        // The bug that kept re-firing "Skua requires Windows 10" on Linux: the
        // compiled-script disk cache was keyed on the RAW source, but the Windows-
        // gate neutralization ran AFTER hashing. So a stale non-neutralized
        // CoreBots.dll kept being reused regardless of the fix. The cure is to hash
        // the *neutralized* source, so a gate-bearing script hashes differently
        // from its raw form and can never collide with a stale DLL. This asserts the
        // transform actually moves the key for gate-bearing source (and leaves
        // gate-free source untouched, so unrelated scripts still cache-hit).
        const string gated = @"class C { void M() { if (!OperatingSystem.IsWindowsVersionAtLeast(10)) {} } }";
        const string plain = @"class C { void M() { int x = 1; } }";

        string neutralizedGated = global::Skua.Core.Compiler.NeutralizeWindowsPlatformGates(gated);
        string neutralizedPlain = global::Skua.Core.Compiler.NeutralizeWindowsPlatformGates(plain);

        Assert.NotEqual(gated, neutralizedGated);                        // gate rewritten
        Assert.DoesNotContain("IsWindowsVersionAtLeast", neutralizedGated);
        Assert.NotEqual(gated.GetHashCode(), neutralizedGated.GetHashCode()); // => different cache key
        Assert.Equal(plain, neutralizedPlain);                           // gate-free source untouched
    }

    [AvaloniaFact]
    public void Windows_gate_is_neutralized_on_the_include_compile_path()
    {
        // CoreBots is compiled as a //cs_include (loadContext != null, explicit
        // cache hash) — a different code path from a top-level script. Exercise
        // exactly that path and confirm the gate is still neutralized there.
        var compiler = Ioc.Default.GetRequiredService<global::Skua.Core.Compiler>();
        compiler.AddDefaultReferencesAndNamespaces();
        const string code = @"
public class IncludeTest {
    public string Run() {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10)) return ""blocked"";
        return ""ok"";
    }
}";
        int hash = global::Skua.Core.Compiler.NeutralizeWindowsPlatformGates(code).GetHashCode();
        dynamic? instance = compiler.CompileClass(code, hash, new global::Skua.Core.ScriptLoadContext(), "IncludeTest");
        Assert.False(compiler.Error, $"compile failed: {compiler.ErrorMessage}");
        Assert.Equal("ok", (string)instance!.Run());
    }

    [AvaloniaFact]
    public void Include_paths_resolve_case_insensitively()
    {
        // AQW //cs_include directives are Windows-cased and often disagree with the
        // real repo path (e.g. "Good/BLOD/CoreBLOD.cs" while the file is
        // "Good/BLoD/CoreBLOD.cs"). On Linux the exact path doesn't exist and the
        // include silently drops → CS0246 for the missing Core type. Verify the
        // resolver finds the real file despite the case mismatch.
        var method = typeof(global::Skua.Core.Scripts.ScriptManager)
            .GetMethod("ResolveCaseInsensitivePath",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        string Resolve(string p) => (string?)method.Invoke(null, new object[] { p }) ?? "<null>";

        string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "skua-c1-" + System.Guid.NewGuid().ToString("N"));
        string realDir = System.IO.Path.Combine(root, "Good", "BLoD");
        System.IO.Directory.CreateDirectory(realDir);
        string realFile = System.IO.Path.Combine(realDir, "CoreBLOD.cs");
        System.IO.File.WriteAllText(realFile, "// core");

        try
        {
            // Wrong directory case ("BLOD") still resolves to the real file ("BLoD").
            string wrongCase = System.IO.Path.Combine(root, "Good", "BLOD", "CoreBLOD.cs");
            Assert.Equal(realFile, Resolve(wrongCase));
            // Exact path short-circuits to itself.
            Assert.Equal(realFile, Resolve(realFile));
            // A genuinely missing file resolves to null (no false positives).
            Assert.Equal("<null>", Resolve(System.IO.Path.Combine(root, "Good", "BLoD", "Nope.cs")));

            // Backtracking: an EMPTY phantom wrong-case dir ("Good/BLOD/") — which old
            // builds created — must not shadow the real "Good/BLoD/". A naive first-match
            // walk descends into the empty phantom and fails; this must still find the file.
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(root, "Good", "BLOD"));
            Assert.Equal(realFile, Resolve(wrongCase));
        }
        finally
        {
            try { System.IO.Directory.Delete(root, true); } catch { }
        }
    }

    [AvaloniaFact]
    public void Skua_swf_is_bundled_and_resolvable()
    {
        // The bot only works if skua.swf boots as the ROOT movie, and old code
        // looked it up with a CWD-relative path — which fails inside an AppImage
        // (the CWD is wherever the user launched from), silently starting the
        // game with a dead bot. Assert the app-base-directory probe finds the
        // bundled copy, and that the probe order prefers the app dir.
        Assert.NotNull(Skua.Flash.Linux.RuffleFlashUtil.ResolveSkuaSwf());

        string a = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "skua-swf-a-" + System.Guid.NewGuid().ToString("N"));
        string b = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "skua-swf-b-" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(a);
        System.IO.Directory.CreateDirectory(b);
        try
        {
            Assert.Null(Skua.Flash.Linux.RuffleFlashUtil.ResolveSkuaSwf(a, b));
            System.IO.File.WriteAllText(System.IO.Path.Combine(b, "skua.swf"), "x");
            Assert.Equal(System.IO.Path.Combine(b, "skua.swf"), Skua.Flash.Linux.RuffleFlashUtil.ResolveSkuaSwf(a, b));
            System.IO.File.WriteAllText(System.IO.Path.Combine(a, "skua.swf"), "x");
            Assert.Equal(System.IO.Path.Combine(a, "skua.swf"), Skua.Flash.Linux.RuffleFlashUtil.ResolveSkuaSwf(a, b));
        }
        finally
        {
            try { System.IO.Directory.Delete(a, true); } catch { }
            try { System.IO.Directory.Delete(b, true); } catch { }
        }
    }

    [AvaloniaFact]
    public void Script_load_context_resolves_cached_includes_at_runtime()
    {
        // The crash: a running script touches a type from an INCLUDED script
        // ('1InTheFiendsShadow') and the runtime asks the load context to resolve
        // that assembly by name. ScriptLoadContext's resolver probed the dead
        // unversioned Cached-Scripts dir while compiled DLLs live in the
        // versioned one — FileNotFoundException at runtime. Compile an include
        // into the cache, then resolve it BY NAME from a fresh context, exactly
        // as the runtime does.
        var compiler = Ioc.Default.GetRequiredService<global::Skua.Core.Compiler>();
        compiler.AddDefaultReferencesAndNamespaces();
        const string code = @"public class DepLibXyz { public string Ping() => ""pong""; }";
        dynamic? inst = compiler.CompileClass(code, code.GetHashCode(), new global::Skua.Core.ScriptLoadContext(), "DepLibXyz");
        Assert.False(compiler.Error, $"compile failed: {compiler.ErrorMessage}");
        Assert.NotNull(inst);

        var fresh = new global::Skua.Core.ScriptLoadContext();
        var asm = fresh.LoadFromAssemblyName(new System.Reflection.AssemblyName("DepLibXyz"));
        Assert.NotNull(asm);
        Assert.NotNull(asm.GetType("DepLibXyz"));
    }

    [AvaloniaFact]
    public void Client_startup_parses_army_login_and_script_args()
    {
        // The AccountManager relaunches the AppImage per account with
        // --client --instance <acc> -u <user> -p <pass> -s <server> --run-script <path>.
        // A client with credentials or a script must produce a startup handler
        // (auto-login + auto-run); a plain manual client (--client only) must not.
        var withWork = Skua.Avalonia.Services.ClientStartup.FromArgs(new[]
        {
            "--client", "--instance", "Acc1",
            "-u", "user1", "-p", "pass1", "-s", "Twilly",
            "--run-script", "/scripts/Farm.cs",
        });
        Assert.NotNull(withWork);
        Assert.True(withWork!.HasWork);

        var plain = Skua.Avalonia.Services.ClientStartup.FromArgs(new[] { "--client", "--instance", "Manual" });
        Assert.Null(plain);
    }

    [AvaloniaFact]
    public void GameSession_is_a_shared_singleton()
    {
        // The game must live in ONE process-wide session (not per-view), so
        // switching tabs — which detaches/recreates the GameView — keeps the same
        // running game and engine binding instead of tearing it down.
        var a = Ioc.Default.GetService<Skua.Avalonia.Services.GameSession>();
        var b = Ioc.Default.GetService<Skua.Avalonia.Services.GameSession>();
        Assert.NotNull(a);
        Assert.Same(a, b);
    }

    [AvaloniaFact]
    public void GameClientWindow_carries_its_instance_label()
    {
        // Client mode (a launched army member) opens a standalone GameClientWindow
        // whose title + GameView label identify the instance. Construct it (do not
        // Show, so the game auto-start / native renderer isn't spun up) and assert
        // the label threads through to both the window title and the GameView.
        GameClientWindow window = new("Account1");

        Assert.Equal("VibeSkua — Account1", window.Title);
        GameView game = Assert.IsType<GameView>(window.Content);
        Assert.Equal("Account1", game.InstanceLabel);
    }

    [AvaloniaFact]
    public void Client_launcher_is_registered_for_the_army_flow()
    {
        // The Launcher/AccountManager spawn client instances through IClientLauncher.
        // On Linux the app registers the self-relaunch (--client) implementation, so
        // "Launch Skua" / per-account start pops out independent game windows.
        IClientLauncher? launcher = Ioc.Default.GetService<IClientLauncher>();
        Assert.NotNull(launcher);
    }

    [AvaloniaFact]
    public void AboutView_shows_bound_markdown_content()
    {
        MainWindowViewModel shell = NewShell(out AboutViewModel about);
        MainWindow window = new() { DataContext = shell };
        window.Show();

        // AboutViewModel populates MarkdownDoc from a background task; pump until
        // it arrives (bounded), then confirm the bound text reached the control.
        for (int i = 0; i < 50 && string.IsNullOrEmpty(about.MarkdownDoc); i++)
        {
            Dispatcher.UIThread.RunJobs();
            System.Threading.Thread.Sleep(10);
        }
        Dispatcher.UIThread.RunJobs();

        SelectableTextBlock text = window.GetVisualDescendants()
            .OfType<SelectableTextBlock>()
            .First();

        Assert.Contains("VibeSkua", text.Text);
    }
}
