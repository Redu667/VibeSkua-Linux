using Avalonia.Controls;
using Avalonia.Interactivity;
using Skua.Core.ViewModels.Manager;

namespace Skua.Avalonia.Views;

public partial class SelectGroupDialogView : UserControl
{
    public SelectGroupDialogView()
    {
        InitializeComponent();
    }

    private void BtnConfirm_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SelectGroupDialogViewModel vm)
            return;
        // The command records the choice on the VM; the host result mirrors it
        // so callers can use either.
        vm.ConfirmCommand.Execute(null);
        HostDialogWindow.CloseWithResult(this, vm.DialogResult);
    }

    private void BtnCancel_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SelectGroupDialogViewModel vm)
            vm.CancelCommand.Execute(null);
        HostDialogWindow.CloseWithResult(this, false);
    }
}
