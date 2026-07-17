using Skua.Core.Interfaces;

namespace Skua.Avalonia.Services;

/// <summary>
/// Minimal Linux <see cref="IWindowService"/>. Managed-window registration is
/// tracked so keys resolve; actually opening separate top-level windows is a
/// later refinement (most panels live in the main shell's content area). Exists
/// so ViewModels that resolve <see cref="IWindowService"/> don't hit a null.
/// </summary>
public sealed class WindowService : IWindowService
{
    private readonly Dictionary<string, object> _managed = new();

    public void ShowWindow<TViewModel>(int width, int height) where TViewModel : class { }
    public void ShowWindow<TViewModel>() where TViewModel : class { }
    public void ShowWindow<TViewModel>(TViewModel viewModel) where TViewModel : class { }

    public void ShowManagedWindow(string key) { /* surfaced in the shell instead */ }

    public void RegisterManagedWindow<TViewModel>(string key, TViewModel viewModel)
        where TViewModel : class, IManagedWindow
        => _managed[key] = viewModel;
}
