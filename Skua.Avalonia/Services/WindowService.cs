using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using Skua.Avalonia.Views;
using Skua.Core.Interfaces;

namespace Skua.Avalonia.Services;

/// <summary>
/// Linux <see cref="IWindowService"/> — the WPF service ported to Avalonia:
/// pop-out <see cref="HostWindow"/>s over portable ViewModels, plus the
/// managed-window registry (`Console`, `Script Repo`, …) that
/// <c>Skua.Core.AppStartup.ManagedWindows</c> fills and hotkeys/ViewModels
/// open via <see cref="ShowManagedWindow"/>. Managed windows are created
/// lazily on first show and hide on close (WPF's <c>HideWindow="True"</c>),
/// so they stay registered and reopenable.
/// </summary>
public sealed class WindowService : IWindowService
{
    private readonly object _gate = new();
    private readonly Dictionary<string, IManagedWindow> _managedViewModels = new();
    private readonly Dictionary<string, HostWindow> _managedWindows = new();

    public void ShowWindow<TViewModel>() where TViewModel : class
        => Show(() => Ioc.Default.GetService<TViewModel>());

    public void ShowWindow<TViewModel>(int width, int height) where TViewModel : class
        => Show(() => Ioc.Default.GetService<TViewModel>(), width, height);

    public void ShowWindow<TViewModel>(TViewModel viewModel) where TViewModel : class
        => Show(() => viewModel);

    private static void Show(Func<object?> viewModelFactory, int? width = null, int? height = null)
        => Dispatcher.UIThread.Post(() =>
        {
            object? viewModel = viewModelFactory();
            if (viewModel is null)
                return;
            HostWindow window = new() { DataContext = viewModel };
            if (width is not null)
                window.Width = width.Value;
            if (height is not null)
                window.Height = height.Value;
            window.Show();
        });

    public void RegisterManagedWindow<TViewModel>(string key, TViewModel viewModel)
        where TViewModel : class, IManagedWindow
    {
        lock (_gate)
        {
            if (!_managedViewModels.ContainsKey(key))
                _managedViewModels[key] = viewModel;
        }
    }

    public void ShowManagedWindow(string key)
    {
        IManagedWindow? viewModel;
        lock (_gate)
        {
            if (!_managedViewModels.TryGetValue(key, out viewModel))
                return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            HostWindow? window;
            lock (_gate)
            {
                _managedWindows.TryGetValue(key, out window);
            }

            if (window is null)
            {
                window = new HostWindow { DataContext = viewModel, HideOnClose = true };
                lock (_gate)
                {
                    _managedWindows[key] = window;
                }
            }

            window.Show();
            window.Activate();
            if (window.WindowState == WindowState.Minimized)
                window.WindowState = WindowState.Normal;
            if (window.DataContext is ObservableRecipient recipient)
                recipient.IsActive = true;
        });
    }
}
