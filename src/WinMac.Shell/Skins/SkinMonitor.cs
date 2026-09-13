using WinMac.Core.Win32;

namespace WinMac.Shell.Skins;

/// <summary>一块显示器的几何信息（工作区，坐标已归一为全局虚拟屏）。</summary>
public readonly record struct SkinMonitor(int Index, NativeMethods.RECT WorkArea)
{
    public int Left => WorkArea.Left;
    public int Top => WorkArea.Top;
    public int Width => WorkArea.Right - WorkArea.Left;
    public int Height => WorkArea.Bottom - WorkArea.Top;
}

/// <summary>枚举所有显示器，供 Dock/皮肤跨屏定位。</summary>
public static class SkinMonitors
{
    public static List<SkinMonitor> GetMonitors()
    {
        var list = new List<SkinMonitor>();
        int index = 0;
        NativeMethods.EnumDisplayMonitors(nint.Zero, nint.Zero, (_, _, monitor, _) =>
        {
            list.Add(new SkinMonitor(index, monitor));
            index++;
            return true;
        }, nint.Zero);
        if (list.Count == 0)
        {
            // 兜底：退化为主屏虚拟 1920x1080，避免空列表导致皮肤无宿主窗口。
            list.Add(new SkinMonitor(0, new NativeMethods.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 }));
        }
        return list;
    }
}