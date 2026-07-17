using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Skua.Avalonia.Views;

public partial class MessageBoxDialogView : UserControl
{
    public MessageBoxDialogView()
    {
        InitializeComponent();
    }

    private void BtnYes_Click(object? sender, RoutedEventArgs e)
        => HostDialogWindow.CloseWithResult(this, true);

    private void BtnNo_Click(object? sender, RoutedEventArgs e)
        => HostDialogWindow.CloseWithResult(this, false);
}
