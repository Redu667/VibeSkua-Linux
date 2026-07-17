using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Skua.Core.ViewModels;

namespace Skua.Avalonia.Views;

public partial class AssignHotKeyDialogView : UserControl
{
    public AssignHotKeyDialogView()
    {
        InitializeComponent();
    }

    private void BtnSave_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AssignHotKeyDialogViewModel vm)
            return;

        // Same guards the WPF dialog applies before accepting a gesture.
        if (string.IsNullOrWhiteSpace(vm.KeyInput))
        {
            vm.InputHint = "Enter a non-modifier key before saving.";
            return;
        }
        if (IsModifierKey(vm.KeyInput))
        {
            vm.InputHint = "Modifier keys cannot be used alone. Enter another key.";
            return;
        }
        if (vm.UsedGestures.Contains(vm.KeyGesture, StringComparer.OrdinalIgnoreCase))
        {
            vm.InputHint = "This hotkey is already assigned to another action.";
            return;
        }

        vm.InputHint = string.Empty;
        HostDialogWindow.CloseWithResult(this, true);
    }

    private void BtnCancel_Click(object? sender, RoutedEventArgs e)
        => HostDialogWindow.CloseWithResult(this, false);

    private static bool IsModifierKey(string key) => key.Trim() switch
    {
        "Ctrl" or "Control" or "LeftCtrl" or "RightCtrl" or
        "Alt" or "LeftAlt" or "RightAlt" or
        "Shift" or "LeftShift" or "RightShift" or
        "Win" or "LWin" or "RWin" => true,
        _ => false,
    };
}
