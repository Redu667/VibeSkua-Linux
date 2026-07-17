using Microsoft.Xaml.Behaviors;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System;

namespace Skua.WPF;

public class ListBoxScrollToCaretBehavior : Behavior<ListBox>
{
    private ScrollViewer? _scrollViewer;
    private bool _autoScroll = true;
    private INotifyCollectionChanged? _collectionSource;

    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.Loaded += OnLoaded;
        AssociatedObject.Unloaded += OnUnloaded;
    }

    protected override void OnDetaching()
    {
        AssociatedObject.Loaded -= OnLoaded;
        AssociatedObject.Unloaded -= OnUnloaded;

        if (_collectionSource != null)
        {
            _collectionSource.CollectionChanged -= OnCollectionChanged;
            _collectionSource = null;
        }

        if (_scrollViewer != null)
        {
            _scrollViewer.ScrollChanged -= ScrollViewer_ScrollChanged;
            _scrollViewer = null;
        }
        
        base.OnDetaching();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (AssociatedObject.ItemsSource is INotifyCollectionChanged incc)
        {
            _collectionSource = incc;
            _collectionSource.CollectionChanged += OnCollectionChanged;
        }

        if (VisualTreeHelper.GetChildrenCount(AssociatedObject) > 0)
        {
            Border border = (Border)VisualTreeHelper.GetChild(AssociatedObject, 0);
            _scrollViewer = (ScrollViewer)VisualTreeHelper.GetChild(border, 0);
            if (_scrollViewer != null)
            {
                _scrollViewer.ScrollChanged += ScrollViewer_ScrollChanged;
            }
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_collectionSource != null)
        {
            _collectionSource.CollectionChanged -= OnCollectionChanged;
            _collectionSource = null;
        }

        if (_scrollViewer != null)
        {
            _scrollViewer.ScrollChanged -= ScrollViewer_ScrollChanged;
            _scrollViewer = null;
        }
    }

    private void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentHeightChange == 0)
        {
            if (_scrollViewer!.VerticalOffset == _scrollViewer.ScrollableHeight)
            {
                _autoScroll = true;
            }
            else
            {
                _autoScroll = false;
            }
        }

        if (_autoScroll && e.ExtentHeightChange != 0)
        {
            _scrollViewer!.ScrollToVerticalOffset(_scrollViewer.ExtentHeight);
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_scrollViewer is null)
            return;

        if (e.Action == NotifyCollectionChangedAction.Add && _autoScroll)
        {
            _scrollViewer.Dispatcher.BeginInvoke(new Action(() =>
            {
                _scrollViewer.ScrollToVerticalOffset(_scrollViewer.ExtentHeight);
            }));
        }
    }
}

//using System.Collections.Specialized;
//using System.Windows;
//using System.Windows.Automation.Peers;
//using System.Windows.Automation.Provider;
//using System.Windows.Controls;
//using Microsoft.Xaml.Behaviors;

//namespace Skua.WPF;
//public class ListBoxScrollToCaretBehavior : Behavior<ListBox>
//{
//    private IScrollProvider? _scrollInterface;

//    protected override void OnAttached()
//    {
//        base.OnAttached();
//        AssociatedObject.Loaded += OnLoaded;
//        AssociatedObject.Unloaded += OnUnLoaded;
//    }

//    protected override void OnDetaching()
//    {
//        AssociatedObject.Loaded -= OnLoaded;
//        AssociatedObject.Unloaded -= OnUnLoaded;
//        base.OnDetaching();
//    }

//    private void OnLoaded(object sender, RoutedEventArgs e)
//    {
//        if (AssociatedObject.ItemsSource is not INotifyCollectionChanged incc)
//            return;
//        incc.CollectionChanged += OnCollectionChanged;

//        ListBoxAutomationPeer svAutomation = (ListBoxAutomationPeer)UIElementAutomationPeer.CreatePeerForElement(AssociatedObject);
//        _scrollInterface = (IScrollProvider)svAutomation.GetPattern(PatternInterface.Scroll);
//    }

//    private void OnUnLoaded(object? sender, RoutedEventArgs e)
//    {
//        if (AssociatedObject.ItemsSource is not INotifyCollectionChanged incc)
//            return;

//        incc.CollectionChanged -= OnCollectionChanged;
//    }

//    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
//    {
//        if (_scrollInterface is null)
//            return;

//        System.Windows.Automation.ScrollAmount scrollVertical = System.Windows.Automation.ScrollAmount.LargeIncrement;
//        System.Windows.Automation.ScrollAmount scrollHorizontal = System.Windows.Automation.ScrollAmount.NoAmount;
//        //If the vertical scroller is not available, the operation cannot be performed, which will raise an exception.
//        if (_scrollInterface.VerticallyScrollable)
//            _scrollInterface.Scroll(scrollHorizontal, scrollVertical);
//    }
//}