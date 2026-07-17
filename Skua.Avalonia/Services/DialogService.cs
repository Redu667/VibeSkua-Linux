using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Threading;
using Skua.Core.Interfaces;
using Skua.Core.Models;

namespace Skua.Avalonia.Services;

/// <summary>
/// Linux <see cref="IDialogService"/>. Message boxes are real MODAL dialogs
/// owned by (and centered on) the client window, so they no longer scatter as
/// free-floating top-level windows the way <c>Window.Show</c> produced. The
/// Skua.Core contract is synchronous and scripts call it from the script thread,
/// so each call marshals to the UI thread, shows the dialog modally, and blocks
/// the caller until a button is clicked — returning the actual choice (the
/// multi-button overload previously ignored its buttons and always returned
/// Cancelled, so a script's "Yes/No" prompt could never be answered).
/// </summary>
public sealed class DialogService : IDialogService
{
    // Typed dialogs are not ported yet; returning null means "cancelled/none".
    public bool? ShowDialog<TViewModel>(TViewModel viewModel) where TViewModel : class => null;
    public bool? ShowDialog<TViewModel>(TViewModel viewModel, string title) where TViewModel : class => null;
    public bool? ShowDialog<TViewModel>(TViewModel viewModel, Action<TViewModel> callback) where TViewModel : class => null;

    public void ShowMessageBox(string message, string caption)
        => ShowModal(message, caption, new[] { "OK" });

    public bool? ShowMessageBox(string message, string caption, bool yesAndNo)
    {
        if (!yesAndNo)
        {
            ShowModal(message, caption, new[] { "OK" });
            return null;
        }
        DialogResult result = ShowModal(message, caption, new[] { "Yes", "No" });
        // Index 0 = Yes, 1 = No; -1 (window closed) = no choice.
        return result.Value switch { 0 => true, 1 => false, _ => (bool?)null };
    }

    public DialogResult ShowMessageBox(string message, string caption, params string[] buttons)
        => ShowModal(message, caption, buttons.Length > 0 ? buttons : new[] { "OK" });

    private static DialogResult ShowModal(string message, string caption, string[] buttons)
        => RunSync(() => ShowModalAsync(message, caption, buttons));

    private static async Task<DialogResult> ShowModalAsync(string message, string caption, string[] buttons)
    {
        Window? owner = ActiveWindow();

        DialogResult result = DialogResult.Cancelled;

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };

        Window dialog = new()
        {
            Title = caption,
            Width = 440,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = owner is null
                ? WindowStartupLocation.CenterScreen
                : WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text = message,
                        TextWrapping = global::Avalonia.Media.TextWrapping.Wrap,
                    },
                    buttonRow,
                },
            },
        };

        for (int i = 0; i < buttons.Length; i++)
        {
            int index = i;
            string text = buttons[i];
            Button button = new()
            {
                Content = text,
                MinWidth = 72,
                IsDefault = i == 0,                  // Enter = primary button
                IsCancel = i == buttons.Length - 1,  // Esc  = last button
            };
            button.Click += (_, _) =>
            {
                result = new DialogResult(text, index);
                dialog.Close();
            };
            buttonRow.Children.Add(button);
        }

        if (owner is not null)
            await dialog.ShowDialog(owner);
        else
            dialog.Show(); // no owner (very early startup) — better than swallowing it

        return result;
    }

    private static Window? ActiveWindow()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return null;
        return desktop.Windows.FirstOrDefault(w => w.IsActive)
               ?? desktop.MainWindow
               ?? desktop.Windows.FirstOrDefault();
    }

    /// <summary>
    /// Run an async UI operation and block until it completes, returning its
    /// result. Mirrors <c>FileDialogService.RunSync</c>: on the UI thread it
    /// pumps a nested dispatcher frame (so the modal stays responsive without
    /// deadlocking); off it, it marshals onto the UI thread and waits.
    /// </summary>
    private static DialogResult RunSync(Func<Task<DialogResult>> func)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            DialogResult result = DialogResult.Cancelled;
            var frame = new DispatcherFrame();
            _ = Dispatcher.UIThread.InvokeAsync(async () =>
            {
                try { result = await func(); }
                catch (Exception ex) { Console.Error.WriteLine($"dialog failed: {ex}"); }
                finally { frame.Continue = false; }
            });
            Dispatcher.UIThread.PushFrame(frame);
            return result;
        }

        try
        {
            return Dispatcher.UIThread.InvokeAsync(func).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"dialog failed: {ex}");
            return DialogResult.Cancelled;
        }
    }
}
