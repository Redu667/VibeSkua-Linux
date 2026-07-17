using Skua.Core.ViewModels;
using System.Windows.Controls;
using System.Windows.Input;

namespace Skua.WPF.UserControls;

/// <summary>
/// Interaction logic for AccountItemUserControl.xaml
/// </summary>
public partial class AccountItemUserControl : UserControl
{
    public AccountItemUserControl()
    {
        InitializeComponent();
    }

    private void Border_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Don't toggle if clicking on a button or checkbox
        if (e.OriginalSource is System.Windows.Controls.Primitives.ButtonBase or CheckBox)
            return;

        if (DataContext is AccountItemViewModel vm)
        {
            vm.ToggleSelectionCommand.Execute(null);
        }
    }
}