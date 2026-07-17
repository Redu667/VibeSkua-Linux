using Avalonia.Threading;
using Skua.Core.Interfaces;

namespace Skua.Avalonia.Services;

/// <summary>
/// Linux <see cref="IDispatcherService"/> over Avalonia's UI-thread dispatcher
/// (the analogue of WPF's <c>Application.Current.Dispatcher</c>).
/// </summary>
public sealed class DispatcherService : IDispatcherService
{
    public void Invoke(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Invoke(action);
    }
}
