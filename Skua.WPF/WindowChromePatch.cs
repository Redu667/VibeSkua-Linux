using System;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Skua.WPF;

public static class WindowChromePatch
{
    public static IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case 0x0024:
                WmGetMinMaxInfo(hwnd, lParam);
                handled = false;
                break;
        }
        return IntPtr.Zero;
    }

    private static unsafe void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
    {
        ref MINMAXINFO mmi = ref *(MINMAXINFO*)lParam;
        HMONITOR monitor = PInvoke.MonitorFromWindow((Windows.Win32.Foundation.HWND)hwnd, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        if (!monitor.IsNull)
        {
            MONITORINFO monitorInfo = default;
            monitorInfo.cbSize = (uint)sizeof(MONITORINFO);
            if (PInvoke.GetMonitorInfo(monitor, ref monitorInfo))
            {
                RECT rcWorkArea = monitorInfo.rcWork;
                RECT rcMonitorArea = monitorInfo.rcMonitor;
                mmi.ptMaxPosition.X = Math.Abs(rcWorkArea.left - rcMonitorArea.left);
                mmi.ptMaxPosition.Y = Math.Abs(rcWorkArea.top - rcMonitorArea.top);
                mmi.ptMaxSize.X = Math.Abs(rcWorkArea.right - rcWorkArea.left);
                mmi.ptMaxSize.Y = Math.Abs(rcWorkArea.bottom - rcWorkArea.top);
            }
        }
    }
}
