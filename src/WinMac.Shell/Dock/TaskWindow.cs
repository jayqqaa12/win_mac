using WinMac.Core.Win32;

namespace WinMac.Shell.Dock;

/// <summary>Dock 中的一条“正在运行的窗口”。</summary>
public sealed class TaskWindow
{
    public TaskWindow(nint hwnd, uint pid, string title)
    {
        Hwnd = hwnd;
        Pid = pid;
        Title = title;
    }

    public nint Hwnd { get; }
    public uint Pid { get; }
    public string Title { get; }

    public bool IsForeground => NativeMethods.GetForegroundWindow() == Hwnd;
}