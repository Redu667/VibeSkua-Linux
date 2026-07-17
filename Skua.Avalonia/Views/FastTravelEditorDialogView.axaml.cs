using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Skua.Avalonia.Views;

public partial class FastTravelEditorDialogView : UserControl
{
    public FastTravelEditorDialogView()
    {
        InitializeComponent();
    }

    private void BtnSave_Click(object? sender, RoutedEventArgs e)
        => HostDialogWindow.CloseWithResult(this, true);

    private void BtnCancel_Click(object? sender, RoutedEventArgs e)
        => HostDialogWindow.CloseWithResult(this, false);
}
