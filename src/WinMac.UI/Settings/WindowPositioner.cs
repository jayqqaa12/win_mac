using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace WinMac.UI.Settings;

/// <summary>把窗口居中到主显示区域(工作区)。</summary>
public static class WindowPositioner
{
    public static void CenterOnPrimary(Window window)
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            var area = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            var wa = area.WorkArea;
            var aw = window.AppWindow;

            int cw = aw.ClientSize.Width;
            int ch = aw.ClientSize.Height;
            if (cw <= 0) cw = 760;
            if (ch <= 0) ch = 560;

            int x = wa.X + (wa.Width - cw) / 2;
            int y = wa.Y + Math.Max(0, (wa.Height - ch) / 2);
            aw.Move(new PointInt32(x, y));
        }
        catch
        {
            // 定位失败不致命，用默认位置。
        }
    }
}