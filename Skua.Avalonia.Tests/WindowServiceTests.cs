using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Skua.Avalonia.Services;
using Skua.Avalonia.Views;
using Skua.Core.Interfaces;
using Xunit;

namespace Skua.Avalonia.Tests;

/// <summary>
/// The pop-out managed-window flow (the Linux twin of WPF's WindowService +
/// HostWindow): registered ViewModels open in a HostWindow sized/titled from
/// IManagedWindow, get activated (IsActive) on show, and can be re-shown
/// after being hidden.
/// </summary>
public class WindowServiceTests
{
    private sealed class PanelViewModel : ObservableRecipient, IManagedWindow
    {
        public string Title => "Test Panel";
        public int Width => 420;
        public int Height => 240;
        public bool CanResize => true;
    }

    [AvaloniaFact]
    public void ShowManagedWindow_opens_a_host_window_over_the_registered_viewmodel()
    {
        WindowService service = new();
        PanelViewModel vm = new();
        service.RegisterManagedWindow("Test Panel", vm);

        service.ShowManagedWindow("Test Panel");
        Dispatcher.UIThread.RunJobs();

        HostWindow window = Assert.Single(HostWindowsFor(vm));
        Assert.True(window.IsVisible);
        Assert.Equal("Test Panel", window.Title);
        Assert.Equal(420, window.Width);
        Assert.True(vm.IsActive, "recipient should be activated on show");

        // Hidden (what user-close does via HideOnClose) then reopened — the
        // registration survives and the same window comes back.
        window.Hide();
        Assert.False(window.IsVisible);
        service.ShowManagedWindow("Test Panel");
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.IsVisible);

        window.Close();
    }

    [AvaloniaFact]
    public void ShowManagedWindow_ignores_unknown_keys()
    {
        WindowService service = new();
        service.ShowManagedWindow("Nope");
        Dispatcher.UIThread.RunJobs(); // must not throw or open anything
    }

    private static HostWindow[] HostWindowsFor(object vm)
        => global::Avalonia.Application.Current!.ApplicationLifetime
                is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.Windows.OfType<HostWindow>().Where(w => ReferenceEquals(w.DataContext, vm)).ToArray()
            : HostWindowTracker.For(vm);

    /// <summary>Headless runs have no desktop lifetime to enumerate windows,
    /// so fall back to walking the dispatcher's open-window list via the
    /// HostWindow instances themselves.</summary>
    private static class HostWindowTracker
    {
        public static HostWindow[] For(object vm) => HostWindow.Open.Where(w => ReferenceEquals(w.DataContext, vm)).ToArray();
    }
}
