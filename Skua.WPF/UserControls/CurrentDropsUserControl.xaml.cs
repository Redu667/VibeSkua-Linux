using CommunityToolkit.Mvvm.DependencyInjection;
using Skua.Core.Models.Items;
using Skua.Core.ViewModels;
using System;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace Skua.WPF.UserControls;

/// <summary>
/// Interaction logic for CurrentDropsUserControl.xaml
/// </summary>
public partial class CurrentDropsUserControl : UserControl
{
    private ICollectionView? _collectionView;
    private CurrentDropsViewModel? _viewModel;
    private DispatcherTimer? _searchDebounceTimer;

    public CurrentDropsUserControl()
    {
        InitializeComponent();
        _viewModel = Ioc.Default.GetRequiredService<CurrentDropsViewModel>();
        DataContext = _viewModel;
        Loaded += OnLoaded;

        // Setup debounce timer for search (300ms delay)
        _searchDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _searchDebounceTimer.Tick += (s, e) =>
        {
            _searchDebounceTimer?.Stop();
            _collectionView?.Refresh();
        };

        // Add Enter key handler for immediate search
        SearchBox.PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter)
            {
                _searchDebounceTimer?.Stop();
                _collectionView?.Refresh();
                e.Handled = true;
            }
        };
    }

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        // Get the collection view from the ListBox's Items collection
        _collectionView = CollectionViewSource.GetDefaultView(CurrentDropsListBox.Items);
        if (_collectionView != null)
            _collectionView.Filter = Search;
    }

    private bool Search(object obj)
    {
        return string.IsNullOrEmpty(SearchBox.Text) || (obj is ItemBase item && item.ToString().Contains(SearchBox.Text, StringComparison.OrdinalIgnoreCase));
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Restart the debounce timer on each keystroke
        _searchDebounceTimer?.Stop();
        _searchDebounceTimer?.Start();
    }
}
