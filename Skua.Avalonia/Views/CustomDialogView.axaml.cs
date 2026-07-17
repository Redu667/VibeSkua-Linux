using Avalonia.Controls;
using Avalonia.Interactivity;
using Skua.Core.ViewModels;

namespace Skua.Avalonia.Views;

public partial class CustomDialogView : UserControl
{
    public CustomDialogView()
    {
        InitializeComponent();
    }

    private void Button_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || DataContext is not CustomDialogViewModel vm)
            return;
        string text = button.Content?.ToString() ?? string.Empty;
        vm.Result = new(text, vm.Buttons.IndexOf(text));
        HostDialogWindow.CloseWithResult(this, true);
    }
}
