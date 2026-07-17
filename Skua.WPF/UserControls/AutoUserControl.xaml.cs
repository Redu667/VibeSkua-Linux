using Skua.Core.ViewModels;
using System.Windows.Controls;

namespace Skua.WPF.UserControls;

/// <summary>
/// Interaction logic for AutoUserControl.xaml
/// </summary>
public partial class AutoUserControl : UserControl
{
    public AutoUserControl()
    {
        InitializeComponent();
    }

    private void ComboBox_DropDownOpened(object sender, System.EventArgs e)
    {
        if (DataContext is AutoViewModel vm)
        {
            vm.ReloadClassesCommand.Execute(null);
        }
    }
}
