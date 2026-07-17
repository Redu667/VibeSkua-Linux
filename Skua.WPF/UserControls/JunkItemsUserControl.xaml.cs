using CommunityToolkit.Mvvm.DependencyInjection;
using Skua.Core.ViewModels;
using System;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Data;

namespace Skua.WPF.UserControls;

public partial class JunkItemsUserControl : UserControl
{
    private ICollectionView? _view;

    public JunkItemsUserControl()
    {
        InitializeComponent();
        DataContext = Ioc.Default.GetRequiredService<JunkItemsViewModel>();
        Loaded += JunkItemsUserControl_Loaded;
    }

    private void JunkItemsUserControl_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (ItemsList.ItemsSource is not null)
        {
            _view = CollectionViewSource.GetDefaultView(ItemsList.ItemsSource);
            if (_view != null)
                _view.Filter = Filter;
        }
    }

    private bool Filter(object obj)
    {
        if (string.IsNullOrWhiteSpace(SearchBox.Text))
            return true;

        string search = SearchBox.Text.Trim();
        string searchLower = search.ToLowerInvariant();

        if (obj is JunkItemEntry entry)
        {
            // Match by ID, name or category (case-insensitive)
            if (entry.ID.ToString().Contains(search, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrEmpty(entry.Name) && entry.Name.ToLowerInvariant().Contains(searchLower))
                return true;

            if (!string.IsNullOrEmpty(entry.Category) && entry.Category.ToLowerInvariant().Contains(searchLower))
                return true;

            return false;
        }

        string? text = obj?.ToString();
        return text != null && text.ToLowerInvariant().Contains(searchLower);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _view?.Refresh();
    }
}
