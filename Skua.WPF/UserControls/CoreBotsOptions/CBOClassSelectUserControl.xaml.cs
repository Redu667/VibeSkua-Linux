using Skua.Core.ViewModels;
using System;
using System.Windows.Controls;

namespace Skua.WPF.UserControls;

/// <summary>
/// Interaction logic for CBOClassSelectUserControl.xaml
/// </summary>
public partial class CBOClassSelectUserControl : UserControl
{
    public CBOClassSelectUserControl()
    {
        InitializeComponent();
    }

    private void ClassComboBox_DropDownOpened(object sender, EventArgs e)
    {
        if (DataContext is CBOClassSelectViewModel vm)
        {
            string? soloClass = vm.SelectedSoloClass;
            string? farmClass = vm.SelectedFarmClass;
            string? dodgeClass = vm.SelectedDodgeClass;
            string? bossClass = vm.SelectedBossClass;
            vm.ReloadClassesCommand.Execute(null);
            vm.SelectedSoloClass = soloClass;
            vm.SelectedFarmClass = farmClass;
            vm.SelectedDodgeClass = dodgeClass;
            vm.SelectedBossClass = bossClass;
        }
    }
}
