using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinMac.Core.Win32;

namespace WinMac.Shell.Dock;

/// <summary>
/// DWM 实时窗口预览：悬停 Dock 图标时在图标上方弹出一个透明置顶小窗，
/// 用 <see cref="NativeMethods.DwmRegisterThumbnail"/> 把目标窗口的实时缩略图挂进去。
/// 单实例复用，切换来源时先注销旧缩略图。
/// </summary>
public sealed class ThumbnailHost : Window
{
    private const int GWL_STYLE = -16;
    private const long WS_CAPTION = 0x00C00000;
    private const long WS_THICKFRAME = 0x00040000;
    private const long WS_POPUP = 0x80000000;
    private const int SWP_NOACTIVATE = 0x0010;
    private const int SW_SHOW = 5;
    private const int SW_HIDE = 0;

    private const double PreviewWidth = 320;
    private const double PreviewHeight = 200;

    private nint _hwnd;
    private nint _thumb;
    private nint _source;
    private bool _shown;

    public void ShowFor(TaskWindow task, int dockBottomScreenY, double iconCenterScreenX)
    {
        EnsureWindow();

        if (_thumb != 0 && _source != task.Hwnd)
        {
            NativeMethods.DwmUnregisterThumbnail(_thumb);
            _thumb = 0;
        }
        if (_thumb == 0)
        {
            NativeMethods.DwmRegisterThumbnail(_hwnd, task.Hwnd, out _thumb);
            _source = task.Hwnd;
        }
        if (_thumb == 0)
            return;

        int w = (int)PreviewWidth, h = (int)PreviewHeight;
        var props = new NativeMethods.DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = NativeMethods.DWM_TNP_RECTDESTINATION
                    | NativeMethods.DWM_TNP_VISIBLE
                    | NativeMethods.DWM_TNP_SOURCECLIENTAREAONLY,
            rcDestination = new NativeMethods.RECT { Left = 0, Top = 0, Right = w, Bottom = h },
            fVisible = 1,
            fSourceClientAreaOnly = 1,
        };
        NativeMethods.DwmUpdateThumbnailProperties(_thumb, ref props);

        int x = (int)Math.Round(iconCenterScreenX - PreviewWidth / 2);
        int y = dockBottomScreenY - h - 10;
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST,
            x, y, w, h, (uint)SWP_NOACTIVATE);

        if (!_shown)
        {
            NativeMethods.ShowWindow(_hwnd, SW_SHOW);
            _shown = true;
        }
    }

    public void Hide()
    {
        if (!_shown)
            return;
        if (_thumb != 0)
        {
            var props = new NativeMethods.DWM_THUMBNAIL_PROPERTIES
            {
                dwFlags = NativeMethods.DWM_TNP_VISIBLE,
                fVisible = 0,
            };
            NativeMethods.DwmUpdateThumbnailProperties(_thumb, ref props);
        }
        NativeMethods.ShowWindow(_hwnd, SW_HIDE);
        _shown = false;
    }

    public void Release()
    {
        if (_thumb != 0)
        {
            NativeMethods.DwmUnregisterThumbnail(_thumb);
            _thumb = 0;
            _source = 0;
        }
    }

    private void EnsureWindow()
    {
        if (_hwnd != nint.Zero)
            return;

        // 透明背景：DWM 缩略图需透过 XAML 画布显示在窗口表面。
        var grid = new Grid
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };
        Content = grid;

        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        long style = NativeMethods.GetWindowLongPtr(_hwnd, GWL_STYLE).ToInt64();
        style &= ~(WS_CAPTION | WS_THICKFRAME);
        style |= WS_POPUP;
        NativeMethods.SetWindowLongPtr(_hwnd, GWL_STYLE, (nint)style);

        long ex = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        ex |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, (nint)ex);
    }
}