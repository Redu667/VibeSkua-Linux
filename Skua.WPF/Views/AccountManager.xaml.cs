using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using Skua.Core.ViewModels;
using Skua.Core.ViewModels.Manager;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Skua.WPF.Views;

/// <summary>
/// Interaction logic for AccountManager.xaml
/// </summary>
public sealed partial class AccountManagerView : UserControl
{
    private readonly AccountManagerViewModel viewModel;
    private readonly IDialogService dialogService;

    public AccountManagerView()
    {
        InitializeComponent();
        viewModel = Ioc.Default.GetRequiredService<AccountManagerViewModel>();
        dialogService = Ioc.Default.GetRequiredService<IDialogService>();
        DataContext = viewModel;

        // Set initial ComboBox selection based on ViewModel
        ColumnsComboBox.SelectedIndex = viewModel.Columns - 1;

        // Subscribe to property changes to keep ComboBox in sync
        viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(viewModel.Columns))
            {
                ColumnsComboBox.SelectedIndex = viewModel.Columns - 1;
            }
        };

        StrongReferenceMessenger.Default.Register<AccountManagerView, ClearPasswordBoxMessage>(this, ClearPasswordBox);
        WeakReferenceMessenger.Default.Register<AccountManagerView, AddAccountToGroupMessage>(this, (r, m) => r.AddAccountToGroup(m.Account));
        WeakReferenceMessenger.Default.Register<AccountManagerView, AddTagsMessage>(this, (r, m) => r.EditAccountTags(m.Account));
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        StrongReferenceMessenger.Default.UnregisterAll(this);
        WeakReferenceMessenger.Default.UnregisterAll(this);
        Unloaded -= OnUnloaded;
    }

    private void ClearPasswordBox(AccountManagerView recipient, ClearPasswordBoxMessage message)
    {
        PswBox?.Clear();
    }

    // Account Form Events
    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        ((AccountManagerViewModel)DataContext).PasswordInput = ((PasswordBox)sender).Password;
    }

    private void ShowAddAccountButton_Click(object sender, RoutedEventArgs e)
    {
        ShowAddAccountButton.Visibility = Visibility.Collapsed;
        AddAccountForm.Visibility = Visibility.Visible;
    }

    private void HideAddAccountButton_Click(object sender, RoutedEventArgs e)
    {
        ShowAddAccountButton.Visibility = Visibility.Visible;
        AddAccountForm.Visibility = Visibility.Collapsed;
    }

    // Tag Management
    private void AddTagsToSelected_Click(object sender, RoutedEventArgs e)
    {
        List<AccountItemViewModel> selectedAccounts = viewModel.Accounts.Where(a => a.UseCheck).ToList();
        if (!selectedAccounts.Any())
        {
            dialogService.ShowMessageBox("No accounts selected", "Add Tags");
            return;
        }

        InputDialogViewModel inputDialogViewModel = new(
            "Add Tags to Selected", "Enter tags (comma-separated)", "Tags", numericInputOnly: false);
        bool? result = dialogService.ShowDialog(inputDialogViewModel, "Add Tags");

        if (result == true)
        {
            List<string> tags = inputDialogViewModel.DialogTextInput.Split(',')
                .Select(t => t.Trim()).Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct().ToList();

            foreach (AccountItemViewModel account in selectedAccounts)
            {
                foreach (string tag in tags)
                {
                    if (!account.Tags.Contains(tag))
                        account.Tags.Add(tag);
                }
            }

            viewModel.SaveAccounts();
            viewModel.RefreshTagFilters();
            viewModel.ApplyTagFilter();
        }
    }

    public void EditAccountTags(AccountItemViewModel account)
    {
        string existingTags = string.Join(", ", account.Tags);
        InputDialogViewModel inputDialogViewModel = new(
            "Edit Tags", "Enter tags (comma-separated)", "Tags", numericInputOnly: false)
        {
            DialogTextInput = existingTags
        };

        bool? result = dialogService.ShowDialog(inputDialogViewModel, "Edit Tags");

        if (result == true)
        {
            List<string> newTags = inputDialogViewModel.DialogTextInput.Split(',')
                .Select(t => t.Trim()).Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct().ToList();

            account.Tags.Clear();
            foreach (string tag in newTags)
                account.Tags.Add(tag);

            viewModel.SaveAccounts();
            viewModel.RefreshTagFilters();
            viewModel.ApplyTagFilter();
        }
    }

    // Group Management
    private void AddSelectedToGroup_Click(object sender, RoutedEventArgs e)
    {
        List<AccountItemViewModel> selectedAccounts = viewModel.Accounts.Where(a => a.UseCheck).ToList();
        if (!selectedAccounts.Any())
        {
            dialogService.ShowMessageBox("No accounts selected", "Add to Group");
            return;
        }

        SelectGroupDialogViewModel dialogViewModel = new(viewModel.Groups);
        bool? result = dialogService.ShowDialog(dialogViewModel, "Add Selected to Group");

        if (result == true && dialogViewModel.SelectedGroup != null)
        {
            foreach (AccountItemViewModel account in selectedAccounts)
            {
                viewModel.AddAccountToGroup(account, dialogViewModel.SelectedGroup);
            }
        }
    }

    private void AddAccountToGroup(AccountItemViewModel account)
    {
        SelectGroupDialogViewModel dialogViewModel = new(viewModel.Groups);
        bool? result = dialogService.ShowDialog(dialogViewModel, "Add to Group");

        if (result == true && dialogViewModel.SelectedGroup != null)
        {
            viewModel.AddAccountToGroup(account, dialogViewModel.SelectedGroup);
        }
    }

    private void AddGroup_Click(object sender, RoutedEventArgs e)
    {
        InputDialogViewModel inputDialogViewModel = new(
            "Create Group", "Enter group name", "Group Name", numericInputOnly: false);
        bool? result = dialogService.ShowDialog(inputDialogViewModel, "Create Group");

        if (result == true && !string.IsNullOrWhiteSpace(inputDialogViewModel.DialogTextInput))
        {
            viewModel.AddGroup(inputDialogViewModel.DialogTextInput.Trim());
        }
    }

    // Grid View
    private void ColumnsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox comboBox && comboBox.SelectedIndex >= 0 && viewModel != null)
        {
            viewModel.Columns = comboBox.SelectedIndex + 1;
        }
    }
}
