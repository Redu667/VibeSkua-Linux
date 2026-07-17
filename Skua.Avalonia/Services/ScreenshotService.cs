using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Skua.Core.Interfaces;

namespace Skua.Avalonia.Services;

/// <summary>
/// Linux <see cref="IScreenshotService"/>: renders the app window (game surface
/// included — it's a regular Avalonia visual fed by the offscreen wgpu render)
/// into a bitmap and returns PNG bytes, mirroring what the WPF service captures
/// via GDI for Discord-webhook screenshots.
/// </summary>
public sealed class ScreenshotService : IScreenshotService
{
    public async Task<byte[]> TakeScreenshotAsync()
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                Window? window = ActiveWindow();
                if (window is null)
                    return Array.Empty<byte>();

                PixelSize size = new(
                    Math.Max(1, (int)window.Bounds.Width),
                    Math.Max(1, (int)window.Bounds.Height));

                using RenderTargetBitmap bitmap = new(size, new Vector(96, 96));
                bitmap.Render(window);

                using MemoryStream stream = new();
                bitmap.Save(stream);
                return stream.ToArray();
            }
            catch
            {
                return Array.Empty<byte>();
            }
        });
    }

    private static Window? ActiveWindow()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return null;
        foreach (Window w in desktop.Windows)
            if (w.IsActive)
                return w;
        return desktop.MainWindow ?? (desktop.Windows.Count > 0 ? desktop.Windows[0] : null);
    }
}
