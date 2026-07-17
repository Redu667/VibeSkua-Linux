using Skua.Core.ViewModels;
using System.Windows.Controls;

namespace Skua.WPF.UserControls;

/// <summary>
/// Interaction logic for CBOClassEquipmentUserControl.xaml
/// </summary>
public partial class CBOClassEquipmentUserControl : UserControl
{
    public CBOClassEquipmentUserControl()
    {
        InitializeComponent();
    }

    private void ComboBox_DropDownOpened(object sender, System.EventArgs e)
    {
        if (DataContext is CBOClassEquipmentViewModel vm)
        {
            vm.RefreshInventoryCommand.Execute(null);
        }
    }
}
