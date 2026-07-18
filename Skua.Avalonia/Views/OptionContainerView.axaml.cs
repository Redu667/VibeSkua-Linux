using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Skua.Avalonia.Views;

/// <summary>
/// The script-options dialog. Values are bound two-way into the option item
/// ViewModels; on Save the host closes with a true result and
/// <c>OptionContainer.SaveOptions</c> (the ShowDialog callback) reads them back
/// and persists. Cancel closes without a result — but note the WPF dialog also
/// saves on window-close, so the callback runs either way; Cancel here simply
/// skips the explicit save intent.
/// </summary>
public partial class OptionContainerView : UserControl
{
    public OptionContainerView()
    {
        InitializeComponent();
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
        => HostDialogWindow.CloseWithResult(this, true);

    private void Cancel_Click(object? sender, RoutedEventArgs e)
        => HostDialogWindow.CloseWithResult(this, false);
}
