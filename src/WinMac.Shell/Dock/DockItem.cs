using Microsoft.UI.Xaml.Media;

namespace WinMac.Shell.Dock;

/// <summary>
/// Dock 中的一格：要么是“固定的常驻启动项”（PinPath，点击启动），
/// 要么是“正在运行的窗口”（Running），或二者合并（固定项 + 其运行实例，显示运行指示）。
/// 排序由 <see cref="DockWindow"/> 维护并写回配置。
/// </summary>
public sealed class DockItem
{
    /// <summary>新建固定的常驻启动项。</summary>
    public DockItem(string pinPath, ImageSource? icon)
    {
        PinPath = pinPath;
        Icon = icon;
    }

    /// <summary>新建运行中的窗口项（未对应任何固定项）。</summary>
    public DockItem(TaskWindow running, ImageSource? icon)
    {
        Running = running;
        Icon = icon;
    }

    /// <summary>固定启动项的 exe 全路径；null 表示纯运行窗口。</summary>
    public string? PinPath { get; }

    /// <summary>运行窗口实例；可被固定项注入，也可为 null。</summary>
    public TaskWindow? Running { get; set; }

    public ImageSource? Icon { get; set; }

    public bool IsRunningWindow => Running != null;

    public string Title => Running is not null
        ? Running.Title
        : (PinPath is not null ? System.IO.Path.GetFileNameWithoutExtension(PinPath) : "?");

    /// <summary>用于排序/持久化的稳定键：固定项用 exe 路径，运行项用 exe 路径（缺失则退回标题）。</summary>
    public string OrderKey => Running?.ProcessPath ?? PinPath ?? Title;

    public nint Hwnd => Running?.Hwnd ?? nint.Zero;
}