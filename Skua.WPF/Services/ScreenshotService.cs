using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Skua.Core.Interfaces;

namespace Skua.WPF.Services;

public class ScreenshotService : IScreenshotService
{
    public async Task<byte[]> TakeScreenshotAsync()
    {
        return await System.Windows.Application.Current.Dispatcher.Invoke(async () =>
        {
            try
            {
                var mainWindow = System.Windows.Application.Current.MainWindow;
                if (mainWindow == null) return new byte[0];

                bool wasMinimized = mainWindow.WindowState == System.Windows.WindowState.Minimized;
                if (wasMinimized)
                {
                    mainWindow.WindowState = System.Windows.WindowState.Normal;
                    await Task.Delay(500); // Wait for the window to visually restore and Flash to render
                }

                var point = mainWindow.PointToScreen(new System.Windows.Point(0, 0));
                int x = (int)point.X;
                int y = (int)point.Y;
                int width = (int)(mainWindow.RenderSize.Width * (point.X == 0 && mainWindow.Left != 0 ? 1 : 1)); // approximate width
                int height = (int)(mainWindow.RenderSize.Height);

                // For exact WPF scaling:
                var source = System.Windows.PresentationSource.FromVisual(mainWindow);
                if (source != null)
                {
                    width = (int)(mainWindow.RenderSize.Width * source.CompositionTarget.TransformToDevice.M11);
                    height = (int)(mainWindow.RenderSize.Height * source.CompositionTarget.TransformToDevice.M22);
                }

                using var bmp = new Bitmap(width, height);
                using var g = Graphics.FromImage(bmp);
                g.CopyFromScreen(x, y, 0, 0, bmp.Size, CopyPixelOperation.SourceCopy);

                if (wasMinimized)
                {
                    mainWindow.WindowState = System.Windows.WindowState.Minimized;
                }

                using var ms = new MemoryStream();
                bmp.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
            catch
            {
                return new byte[0];
            }
        });
    }
}
