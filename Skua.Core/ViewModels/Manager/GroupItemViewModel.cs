using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Messaging;
using System.Collections.ObjectModel;

namespace Skua.Core.ViewModels.Manager;

public partial class GroupItemViewModel : ObservableObject
{
    public GroupItemViewModel(string name)
    {
        Name = name;
        Accounts = new ObservableCollection<AccountItemViewModel>();
    }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private bool _isExpanded;

    public ObservableCollection<AccountItemViewModel> Accounts { get; }

    [RelayCommand]
    private void Remove()
    {
        WeakReferenceMessenger.Default.Send<RemoveGroupMessage>(new(this));
    }

    [RelayCommand]
    private void Start()
    {
        WeakReferenceMessenger.Default.Send<StartGroupMessage>(new(this, false));
    }

    [RelayCommand]
    private void StartWithScript()
    {
        WeakReferenceMessenger.Default.Send<StartGroupMessage>(new(this, true));
    }

    [RelayCommand]
    private void Rename()
    {
        WeakReferenceMessenger.Default.Send<RenameGroupMessage>(new(this));
    }
}
