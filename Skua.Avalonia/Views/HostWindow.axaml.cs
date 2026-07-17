using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia.Controls;
using Skua.Core.Interfaces;

namespace Skua.Avalonia.Views;

/// <summary>
/// Pop-out host for bot-control panels — the Linux twin of Skua.WPF's
/// <c>HostWindow</c>. The panel ViewModel is the DataContext (ViewLocator
/// supplies the view); title, size, and resizability come from
/// <see cref="IManagedWindow"/> when the ViewModel implements it (0 width or
/// height means size-to-content, as on WPF). Managed windows hide on close
/// (WPF's <c>HideWindow="True"</c>) so they can be reopened.
/// </summary>
public partial class HostWindow : Window
{
    public HostWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>When true, closing the window hides it instead (managed
    /// windows stay registered and reopenable).</summary>
    public bool HideOnClose { get; set; }

    private static readonly List<HostWindow> s_open = new();

    /// <summary>Host windows currently open (usable where no desktop lifetime
    /// exists to enumerate windows, e.g. headless tests).</summary>
    public static IReadOnlyList<HostWindow> Open => s_open;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (!s_open.Contains(this))
            s_open.Add(this);
    }

    protected override void OnClosed(EventArgs e)
    {
        s_open.Remove(this);
        base.OnClosed(e);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (HideOnClose && !e.IsProgrammatic)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not IManagedWindow managed)
            return;

        Title = string.IsNullOrEmpty(managed.Title) ? "Skua" : managed.Title;
        CanResize = managed.CanResize;

        if (managed.Width > 0)
            Width = managed.Width;
        if (managed.Height > 0)
            Height = managed.Height;
        if (managed.Width <= 0 && managed.Height <= 0)
            SizeToContent = SizeToContent.WidthAndHeight;
        else if (managed.Width <= 0)
            SizeToContent = SizeToContent.Width;
        else if (managed.Height <= 0)
            SizeToContent = SizeToContent.Height;
    }
}
