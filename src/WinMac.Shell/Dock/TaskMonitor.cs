using System.Text;
using Microsoft.UI.Dispatching;
using WinMac.Core.Win32;

namespace WinMac.Shell.Dock;

/// <summary>
/// 通过 <see cref="NativeMethods.EnumWindows"/> 枚举所有顶层可见应用窗口，
/// 作为 Dock 的图标数据源。用空闲级定时器增量刷新（M1 轮询，成本可忽略，
/// 逐一调用的都是极快 user32 调用）；后续 M5 可平滑替换为 EVENT_OBJECT_* 事件驱动。
/// </summary>
public sealed class TaskMonitor : IDisposable
{
    private static readonly string[] IgnoredClasses =
    {
        "Shell_TrayWnd", "Progman", "WorkerW", "Windows.UI.Composition.DesktopWindowContentBridge",
    };

    private readonly DispatcherQueueTimer _timer;
    private readonly TimeSpan _interval;
    private int _revision;

    public event Action? Changed;

    public TaskMonitor(DispatcherQueue dispatcher, TimeSpan interval)
    {
        _interval = interval;
        _timer = dispatcher.CreateTimer();
        _timer.Interval = interval;
        _timer.Tick += (_, _) => Refresh();
    }

    public IReadOnlyList<TaskWindow> Items { get; private set; } = Array.Empty<TaskWindow>();

    public void Start()
    {
        Refresh();
        _timer.Start();
    }

    private void Refresh()
    {
        var list = new List<TaskWindow>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            TryCollect(hwnd, list);
            return true;
        }, nint.Zero);

        // 仅当内容变化才广播（标题改、增、减），避免无效重排。
        if (list.Count != Items.Count || _revision == 0 || !SameTitles(list))
        {
            Items = list;
            _revision++;
            Changed?.Invoke();
        }
    }

    private static void TryCollect(nint hwnd, List<TaskWindow> list)
    {
        // 顶层可见、可用。
        if (!NativeMethods.IsWindowVisible(hwnd) || !NativeMethods.IsWindowEnabled(hwnd))
            return;

        uint pid;
        NativeMethods.GetWindowThreadProcessId(hwnd, out pid);
        if (pid == 0 || pid == Environment.ProcessId)
            return; // 排除系统空闲进程与本进程(自身窗口/设置窗口)。

        // 排除带 owner 的弹出/popup/浮层（菜单、提示），只收真实顶层主窗口。
        if (NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOTOWNER) != hwnd)
            return;

        var cls = GetClassName(hwnd);
        for (int i = 0; i < IgnoredClasses.Length; i++)
        {
            if (cls == IgnoredClasses[i])
                return;
        }

        string title = GetWindowTitle(hwnd);
        if (string.IsNullOrWhiteSpace(title))
            return;

        list.Add(new TaskWindow(hwnd, pid, title));
    }

    private static string GetClassName(nint hwnd)
    {
        var sb = new StringBuilder(256);
        NativeMethods.GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string GetWindowTitle(nint hwnd)
    {
        int len = NativeMethods.GetWindowTextLength(hwnd);
        if (len <= 0)
            return string.Empty;
        var sb = new StringBuilder(len + 1);
        NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private bool SameTitles(List<TaskWindow> list)
    {
        if (list.Count != Items.Count)
            return false;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Hwnd != Items[i].Hwnd || !string.Equals(list[i].Title, Items[i].Title, StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    public void Dispose()
    {
        _timer.Stop();
    }
}