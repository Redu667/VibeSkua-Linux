using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Skua.Avalonia.Services;
using Skua.Avalonia.Views;
using Skua.Core.ViewModels;
using Xunit;

namespace Skua.Avalonia.Tests;

/// <summary>
/// The modal dialog host chain: dialog ViewModel → <see cref="HostDialogWindow"/>
/// (ViewLocator supplies the view) → buttons close the host with a result →
/// the synchronous <see cref="DialogService.ShowDialog{TViewModel}(TViewModel)"/>
/// hands that result back to the Skua.Core caller.
/// </summary>
public class DialogHostTests
{
    private static Button FindButton(Visual root, string content)
        => root.GetVisualDescendants().OfType<Button>()
            .First(b => (string?)b.Content == content);

    private static void Click(Button button)
        => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    /// <summary>
    /// Post a "user" that acts on the host dialog once it opens, while the
    /// service pumps its nested frame. Runs at Background priority (so layout
    /// gets a turn), forces a layout pass before looking for buttons, and is
    /// bounded — on exhaustion it closes the dialog (or gives up if none ever
    /// opened) instead of spinning the dispatcher forever.
    /// </summary>
    private static void PostUser(System.Action<HostDialogWindow> act, int attemptsLeft = 500)
    {
        Dispatcher.UIThread.Post(() =>
        {
            HostDialogWindow? host = HostDialogWindow.OpenDialogs.FirstOrDefault();
            if (host is null)
            {
                if (attemptsLeft > 0)
                    PostUser(act, attemptsLeft - 1);
                return;
            }
            host.UpdateLayout();
            try
            {
                act(host);
            }
            catch when (attemptsLeft > 0)
            {
                PostUser(act, attemptsLeft - 1);
            }
            catch
            {
                host.Close(); // fail the assertions instead of hanging the frame
            }
        }, DispatcherPriority.Background);
    }

    [AvaloniaFact]
    public void Confirm_button_closes_the_host_with_true()
    {
        InputDialogViewModel vm = new("Save Loadout", "Enter Loadout Name:", false);
        HostDialogWindow host = new() { DataContext = vm };
        host.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(host, HostDialogWindow.OpenDialogs);
        Click(FindButton(host, "Confirm"));
        Dispatcher.UIThread.RunJobs();

        Assert.True(host.Result);
        Assert.DoesNotContain(host, HostDialogWindow.OpenDialogs);
    }

    [AvaloniaFact]
    public void Cancel_button_closes_the_host_with_false()
    {
        InputDialogViewModel vm = new("Save Loadout", "Enter Loadout Name:", false);
        HostDialogWindow host = new() { DataContext = vm };
        host.Show();
        Dispatcher.UIThread.RunJobs();

        Click(FindButton(host, "Cancel"));
        Dispatcher.UIThread.RunJobs();

        Assert.False(host.Result);
    }

    [AvaloniaFact]
    public void Custom_dialog_button_records_the_choice()
    {
        CustomDialogViewModel vm = new("Pick one", "Choose", new[] { "Alpha", "Beta" });
        HostDialogWindow host = new() { DataContext = vm };
        host.Show();
        Dispatcher.UIThread.RunJobs();

        Click(FindButton(host, "Beta"));
        Dispatcher.UIThread.RunJobs();

        Assert.True(host.Result);
        Assert.NotNull(vm.Result);
        Assert.Equal("Beta", vm.Result!.Text);
        Assert.Equal(1, vm.Result!.Value);
    }

    [AvaloniaFact]
    public void DialogService_ShowDialog_blocks_until_the_user_answers()
    {
        // The Skua.Core contract is synchronous: ShowDialog must not return
        // until a button is clicked. Pump a posted "user" that clicks Confirm
        // once the host appears, while ShowDialog blocks on its nested frame.
        InputDialogViewModel vm = new("Getting 5 Map Items", "Quantity:");

        PostUser(host =>
        {
            vm.DialogTextInput = "5";
            Click(FindButton(host, "Confirm"));
        });

        bool? result = new DialogService().ShowDialog(vm);

        Assert.True(result);
        Assert.Equal("5", vm.DialogTextInput);
        Assert.Empty(HostDialogWindow.OpenDialogs);
    }

    [AvaloniaFact]
    public void DialogService_callback_overload_runs_the_callback_on_close()
    {
        InputDialogViewModel vm = new("Rename Group", false);
        InputDialogViewModel? seen = null;

        PostUser(host => Click(FindButton(host, "Cancel")));

        bool? result = new DialogService().ShowDialog(vm, x => seen = x);

        Assert.False(result);
        Assert.Same(vm, seen);
    }
}
