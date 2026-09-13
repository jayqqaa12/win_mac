using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WinMac.Core.Win32;

namespace WinMac.Shell.Dock;

/// <summary>
/// 真实应用图标：从 exe 提取 HICON，经 32bpp 顶层 DIB 绘制后转为 BGRA 像素进 <see cref="WriteableBitmap"/>，
/// 并以 exe 路径为键做 LRU 缓存，避免重复提取与 HICON/位图句柄泄漏。
/// 提取失败（无图标资源/权限不足）返回 null，由调用方回退字母块。
/// </summary>
public sealed class IconCache
{
    // 渲染尺寸：固定高位图，Image 再按 Dock 实际尺寸缩放，确保缩放下不失真。
    private const int RenderSize = 64;
    private const int MaxEntries = 512;

    private readonly Dictionary<string, ImageSource> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _lru = new();

    /// <summary>按 exe 路径取图标；无则尝试提取并入缓存；提取失败返回 null。</summary>
    public ImageSource? Get(string exePath)
    {
        if (_cache.TryGetValue(exePath, out var hit))
        {
            _lru.Remove(exePath);
            _lru.AddFirst(exePath);
            return hit;
        }

        var src = Extract(exePath);
        if (src is null)
            return null;

        _cache[exePath] = src;
        _lru.AddFirst(exePath);
        Trim();

        return src;
    }

    private void Trim()
    {
        while (_lru.Count > MaxEntries)
        {
            var victim = _lru.Last;
            if (victim is null)
                break;
            _cache.Remove(victim.Value);
            _lru.RemoveLast();
        }
    }

    private static ImageSource? Extract(string exePath)
    {
        int count = NativeMethods.ExtractIconEx(exePath, 0, out nint large, out nint small, 1);
        if (count <= 0)
            return null;

        nint hIcon = large != nint.Zero ? large : small;
        if (hIcon == nint.Zero)
        {
            Cleanup(large, small);
            return null;
        }

        nint screenDc = nint.Zero, memDc = nint.Zero, dib = nint.Zero, oldSel = nint.Zero;
        nint bits = nint.Zero;
        try
        {
            var bmi = new NativeMethods.BITMAPINFO
            {
                bmiHeader = new NativeMethods.BITMAPINFOHEADER
                {
                    biSize = 40,
                    biWidth = RenderSize,
                    biHeight = -RenderSize, // 负值 = 顶层 DIB，第一行即图像顶部
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0,
                },
            };

            screenDc = NativeMethods.GetDC(nint.Zero);
            memDc = NativeMethods.CreateCompatibleDC(screenDc);
            dib = NativeMethods.CreateDIBSection(memDc, ref bmi, NativeMethods.DIB_RGB_COLORS, out bits, nint.Zero, 0);
            if (dib == nint.Zero || bits == nint.Zero)
                return null;

            oldSel = NativeMethods.SelectObject(memDc, dib);
            NativeMethods.DrawIconEx(memDc, 0, 0, hIcon, RenderSize, RenderSize, 0, nint.Zero, NativeMethods.DI_NORMAL);
            NativeMethods.SelectObject(memDc, oldSel);

            // 拷贝 BGRA 顶层像素并预乘 alpha（WriteableBitmap 期望 BGRA8+预乘）。
            var data = new byte[RenderSize * RenderSize * 4];
            Marshal.Copy(bits, data, 0, data.Length);
            PremultiplyAlpha(data);

            return ToWriteableBitmap(data);
        }
        finally
        {
            if (oldSel != nint.Zero) NativeMethods.SelectObject(memDc, oldSel);
            if (dib != nint.Zero) NativeMethods.DeleteObject(dib);
            if (memDc != nint.Zero) NativeMethods.DeleteDC(memDc);
            if (screenDc != nint.Zero) NativeMethods.ReleaseDC(nint.Zero, screenDc);
            Cleanup(large, small);
        }
    }

    private static void Cleanup(nint large, nint small)
    {
        if (large != nint.Zero) NativeMethods.DestroyIcon(large);
        if (small != nint.Zero) NativeMethods.DestroyIcon(small);
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

    private static ImageSource ToWriteableBitmap(byte[] data)
    {
        var wb = new WriteableBitmap(RenderSize, RenderSize);
        using var stream = wb.PixelBuffer.AsStream();
        stream.Write(data, 0, data.Length);
        return wb;
    }
}