using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Skua.Avalonia.Views;

public partial class SkillRuleEditorDialogView : UserControl
{
    public SkillRuleEditorDialogView()
    {
        InitializeComponent();
    }

    private void BtnSave_Click(object? sender, RoutedEventArgs e)
        => HostDialogWindow.CloseWithResult(this, true);

    private void BtnCancel_Click(object? sender, RoutedEventArgs e)
        => HostDialogWindow.CloseWithResult(this, false);
}
