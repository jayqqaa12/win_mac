using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WinMac.Core.Win32;

namespace WinMac.Shell.Dock;

/// <summary>
/// 用 <see cref="NativeMethods.PrintWindow"/> 抓取任意窗口为 32bpp BGRA 位图，
/// 供最小化 genie 动画使用。
/// </summary>
public static class WindowSnapshot
{
    public static ImageSource? Capture(nint hwnd)
    {
        NativeMethods.GetWindowRect(hwnd, out var r);
        int w = Math.Max(1, r.Right - r.Left);
        int h = Math.Max(1, r.Bottom - r.Top);
        if (w > 4096 || h > 4096)
            return null; // 异常大窗口跳过，避免浪费大位图。

        nint screenDc = nint.Zero, memDc = nint.Zero, hbm = nint.Zero, old = nint.Zero;
        try
        {
            screenDc = NativeMethods.GetDC(nint.Zero);
            memDc = NativeMethods.CreateCompatibleDC(screenDc);
            hbm = NativeMethods.CreateCompatibleBitmap(screenDc, w, h);
            old = NativeMethods.SelectObject(memDc, hbm);

            if (!NativeMethods.PrintWindow(hwnd, memDc, NativeMethods.PW_RENDERFULLCONTENT))
                return null;
            NativeMethods.SelectObject(memDc, old);

            var bmi = new NativeMethods.BITMAPINFO
            {
                bmiHeader = new NativeMethods.BITMAPINFOHEADER
                {
                    biSize = 40,
                    biWidth = w,
                    biHeight = -h, // 顶层 DIB
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0,
                },
            };
            var data = new byte[w * h * 4];
            NativeMethods.GetDIBits(memDc, hbm, 0, (uint)h, data, ref bmi, 0);
            PremultiplyAlpha(data);

            var wb = new WriteableBitmap(w, h);
            using var stream = wb.PixelBuffer.AsStream();
            stream.Write(data, 0, data.Length);
            return wb;
        }
        finally
        {
            if (old != nint.Zero) NativeMethods.SelectObject(memDc, old);
            if (hbm != nint.Zero) NativeMethods.DeleteObject(hbm);
            if (memDc != nint.Zero) NativeMethods.DeleteDC(memDc);
            if (screenDc != nint.Zero) NativeMethods.ReleaseDC(nint.Zero, screenDc);
        }
    }

    private static void PremultiplyAlpha(byte[] data)
    {
        for (int i = 0; i < data.Length; i += 4)
        {
            ref byte b = ref data[i];
            ref byte g = ref data[i + 1];
            ref byte r = ref data[i + 2];
            ref byte a = ref data[i + 3];
            if (a == 0)
            {
                b = g = r = 0;
            }
            else if (a != 255)
            {
                b = (byte)(b * a / 255);
                g = (byte)(g * a / 255);
                r = (byte)(r * a / 255);
            }
        }
    }
}