using CommunityToolkit.Mvvm.ComponentModel;

namespace Skua.Avalonia.ViewModels;

/// <summary>
/// Marker ViewModel for the in-window game surface. Carries no state — the
/// <see cref="Skua.Avalonia.Views.GameView"/> it maps to (via the ViewLocator)
/// owns the native renderer directly. Exists so the game view slots into the
/// shell's navigation like any other panel.
/// </summary>
public partial class GameViewModel : ObservableObject
{
    /// <summary>When true, the view starts the game as soon as it's shown
    /// (used by client mode so a launched army member goes straight to AQW).</summary>
    public bool AutoStart { get; set; }

    /// <summary>Optional account/instance label shown in the game status.</summary>
    public string? InstanceLabel { get; set; }
}
