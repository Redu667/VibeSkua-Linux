using CommunityToolkit.Mvvm.DependencyInjection;
using Skua.Core.ViewModels;
using System.Windows.Controls;

namespace Skua.WPF.UserControls;

/// <summary>
/// Interaction logic for JumpUserControl.xaml
/// </summary>
public partial class JumpUserControl : UserControl
{
    private readonly JumpViewModel _viewModel;

    public JumpUserControl()
    {
        InitializeComponent();
        _viewModel = Ioc.Default.GetRequiredService<JumpViewModel>();
        DataContext = _viewModel;

        // Refresh cells when dropdown is opened
        Cells.DropDownOpened += (s, e) => _viewModel.UpdateCellsCommand.Execute(null);
    }
}
