using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Messaging;
using Skua.Core.ViewModels;
using Skua.Core.ViewModels.Manager;
using System.Windows;
using System.Windows.Controls;

namespace Skua.WPF.UserControls;

/// <summary>
/// Interaction logic for GroupItemUserControl.xaml
/// </summary>
public partial class GroupItemUserControl : UserControl
{
    public GroupItemUserControl()
    {
        InitializeComponent();
    }

    private void RemoveAccountFromGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button &&
            button.DataContext is AccountItemViewModel account &&
            DataContext is GroupItemViewModel group)
        {
            WeakReferenceMessenger.Default.Send(new RemoveAccountFromGroupMessage(group, account));
        }
    }
}
