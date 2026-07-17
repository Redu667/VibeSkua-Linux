using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Skua.Core.Interfaces;

namespace Skua.Avalonia.Services;

/// <summary>
/// Linux <see cref="IClipboardService"/> over Avalonia's <see cref="IClipboard"/>.
/// The synchronous interface is bridged to Avalonia's async clipboard; all calls
/// are null-safe (the clipboard is unavailable in headless runs before a window
/// exists).
/// </summary>
public sealed class ClipboardService : IClipboardService
{
    private static IClipboard? Clipboard =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow?.Clipboard;

    public void SetText(string text)
    {
        try
        {
            Clipboard?.SetTextAsync(text ?? string.Empty).GetAwaiter().GetResult();
        }
        catch { /* no clipboard backend — ignore */ }
    }

    public string GetText()
    {
        try
        {
            return Clipboard?.GetTextAsync().GetAwaiter().GetResult() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public void SetData(string format, object data)
    {
        // Avalonia's typed clipboard focuses on text; custom formats fall back to
        // stringified text so callers relying on round-tripping still function.
        if (data is not null)
            SetText(data.ToString() ?? string.Empty);
    }

    public object GetData(string format) => GetText();
}
