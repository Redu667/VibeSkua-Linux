using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Skua.Avalonia.Views;

public partial class InputDialogView : UserControl
{
    public InputDialogView()
    {
        InitializeComponent();
    }

    private void BtnConfirm_Click(object? sender, RoutedEventArgs e)
        => HostDialogWindow.CloseWithResult(this, true);

    private void BtnCancel_Click(object? sender, RoutedEventArgs e)
        => HostDialogWindow.CloseWithResult(this, false);
}
