using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Skua.Avalonia.Views;

/// <summary>
/// A standalone bot-client window: a single <see cref="GameView"/> (its own live
/// Ruffle player) with no manager chrome. Each launched client runs in this
/// window, so opening several — one per account — gives the "army" of
/// independent clients. Auto-starts the game on open so a spawned client goes
/// straight to AQW.
/// </summary>
public partial class GameClientWindow : Window
{
    private readonly GameView _game;

    public GameClientWindow() : this(null)
    {
    }

    public GameClientWindow(string? instanceLabel)
    {
        AvaloniaXamlLoader.Load(this);
        _game = this.FindControl<GameView>("Game")!;
        _game.InstanceLabel = instanceLabel;

        if (!string.IsNullOrEmpty(instanceLabel))
            Title = $"VibeSkua — {instanceLabel}";

        // Auto-start once the window is shown so a launched army member goes
        // straight into the game without a manual click.
        Opened += (_, _) => _game.StartGame();
    }
}
