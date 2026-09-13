using WinMac.Core.Win32;

namespace WinMac.Shell.Dock;

/// <summary>Dock 中的一条“正在运行的窗口”。</summary>
public sealed class TaskWindow
{
    public TaskWindow(nint hwnd, uint pid, string title, string? processPath = null)
    {
        Hwnd = hwnd;
        Pid = pid;
        Title = title;
        ProcessPath = processPath;
    }

    public nint Hwnd { get; }
    public uint Pid { get; }
    public string Title { get; }

    /// <summary>所属进程 exe 完整路径（真实图标缓存键）；解析失败为 null。</summary>
    public string? ProcessPath { get; }

    public bool IsForeground => NativeMethods.GetForegroundWindow() == Hwnd;
}