using System;
using System.Collections.Generic;
using System.Text;

namespace GroundControl.Native;

/// <summary>
/// Produces an "Alt+Tab style" list of real, visible, top-level application windows,
/// applying the usual heuristics (tool windows, cloaked windows, shell surfaces and
/// minimized windows are excluded — the latter because DWM cannot render a live
/// thumbnail of a window that isn't being composited).
/// </summary>
public static class WindowEnumerator
{
    private static readonly HashSet<string> ExcludedClasses = new()
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Windows.UI.Core.CoreWindow",
        "Microsoft.UI.Content.DesktopChildSiteBridge"
    };

    public static List<WindowInfo> GetAltTabWindows(params IntPtr[] exclude)
    {
        var result = new List<WindowInfo>();
        var excludeSet = new HashSet<IntPtr>(exclude);
        var classBuffer = new StringBuilder(256);

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (excludeSet.Contains(hwnd)) return true;
            if (!NativeMethods.IsWindowVisible(hwnd)) return true;
            if (NativeMethods.IsIconic(hwnd)) return true;                       // minimized: no live thumbnail
            if (NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT) != hwnd) return true; // child/owned

            int len = NativeMethods.GetWindowTextLength(hwnd);
            if (len == 0) return true;

            int ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            if ((ex & NativeMethods.WS_EX_TOOLWINDOW) != 0) return true;

            // Cloaked windows are suspended UWP apps or live on another virtual desktop.
            if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0
                && cloaked != 0)
                return true;

            if (!NativeMethods.GetWindowRect(hwnd, out var rect)) return true;
            if (rect.Width <= 0 || rect.Height <= 0) return true;

            classBuffer.Clear();
            NativeMethods.GetClassName(hwnd, classBuffer, classBuffer.Capacity);
            if (ExcludedClasses.Contains(classBuffer.ToString())) return true;

            var titleBuffer = new StringBuilder(len + 1);
            NativeMethods.GetWindowText(hwnd, titleBuffer, titleBuffer.Capacity);
            string title = titleBuffer.ToString();
            if (string.IsNullOrWhiteSpace(title)) return true;

            result.Add(new WindowInfo { Handle = hwnd, Title = title, Rect = rect });
            return true;
        }, IntPtr.Zero);

        return result;
    }
}
