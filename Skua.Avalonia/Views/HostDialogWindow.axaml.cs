using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Skua.Avalonia.Views;

/// <summary>
/// Modal host for the dialog ViewModels (the Linux twin of Skua.WPF's
/// <c>HostDialog</c>). <c>DialogService</c> sets the ViewModel as
/// DataContext and the app-level <see cref="ViewLocator"/> materializes the
/// dialog view inside. Dialog views report their outcome through
/// <see cref="CloseWithResult"/> — the equivalent of WPF's
/// <c>Window.GetWindow(this).DialogResult = ...</c>.
/// </summary>
public partial class HostDialogWindow : Window
{
    public HostDialogWindow()
    {
        InitializeComponent();
    }

    private static readonly List<HostDialogWindow> s_open = new();

    /// <summary>Host dialogs currently on screen (also usable where no
    /// desktop lifetime exists to enumerate windows, e.g. headless tests).</summary>
    public static IReadOnlyList<HostDialogWindow> OpenDialogs => s_open;

    /// <summary>The dialog outcome; stays null when the window is closed
    /// without a choice (title-bar close), matching WPF's ShowDialog.</summary>
    public bool? Result { get; private set; }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        s_open.Add(this);
    }

    protected override void OnClosed(EventArgs e)
    {
        s_open.Remove(this);
        base.OnClosed(e);
    }

    /// <summary>
    /// Close the host dialog containing <paramref name="source"/> with the
    /// given result. Safe to call from any control inside a dialog view; does
    /// nothing when the view is hosted outside a HostDialogWindow (e.g. in a
    /// headless view-resolution test).
    /// </summary>
    public static void CloseWithResult(Control source, bool? result)
    {
        if (source.GetVisualRoot() is HostDialogWindow host)
        {
            host.Result = result;
            host.Close();
        }
    }
}
