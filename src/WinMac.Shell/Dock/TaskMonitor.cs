using System.Text;
using Microsoft.UI.Dispatching;
using WinMac.Core.Win32;

namespace WinMac.Shell.Dock;

/// <summary>
/// 通过 <see cref="NativeMethods.EnumWindows"/> 枚举所有顶层可见应用窗口作为 Dock 图标数据源，
/// 并采集每个窗口所属进程的 exe 路径（供真实图标缓存作键）。
/// 以 <see cref="NativeMethods.SetWinEventHook"/> 事件驱动（前台切换/最小化/窗口销毁）增量刷新，
/// 在专用后台线程泵送消息；保留低频定时器兜底，避免个别事件漏报导致图标残留。
/// </summary>
public sealed class TaskMonitor : IDisposable
{
    private const uint WM_WINMAC_WAKE = 0x8000; // 自定义消息，用于唤醒钩子线程退出消息泵。

    private static readonly string[] IgnoredClasses =
    {
        "Shell_TrayWnd", "Progman", "WorkerW", "Windows.UI.Composition.DesktopWindowContentBridge",
    };

    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _timer;
    private nint _hookForeground;
    private nint _hookDestroy;
    private Thread? _hookThread;
    private uint _hookThreadId;
    private volatile bool _running;
    private int _revision;

    public event Action? Changed;

    public TaskMonitor(DispatcherQueue dispatcher, TimeSpan fallbackInterval)
    {
        _dispatcher = dispatcher;
        _timer = dispatcher.CreateTimer();
        _timer.Interval = fallbackInterval;
        _timer.Tick += (_, _) => Refresh();
    }

    public IReadOnlyList<TaskWindow> Items { get; private set; } = Array.Empty<TaskWindow>();

    public void Start()
    {
        Refresh();
        _timer.Start();

        _running = true;
        _hookThread = new Thread(HookThreadProc);
        _hookThread.IsBackground = true;
        _hookThread.SetApartmentState(ApartmentState.MTA);
        _hookThread.Start();
    }

    // ---- 事件钩子：后台线程安装，泵送消息以接收 OUTOFCONTEXT 回调 ----
    private void HookThreadProc()
    {
        _hookThreadId = NativeMethods.GetCurrentThreadId();

        _hookForeground = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_MINIMIZEEND,
            nint.Zero, OnWinEvent, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
        _hookDestroy = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_OBJECT_DESTROY, NativeMethods.EVENT_OBJECT_DESTROY,
            nint.Zero, OnWinEvent, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);

        // 消息泵：阻塞等待事件/唤醒消息，直到被 Dispose 停掉。
        while (_running)
        {
            NativeMethods.GetMessage(out _, nint.Zero, 0, 0);
            if (!_running)
                break;
        }

        if (_hookForeground != nint.Zero) NativeMethods.UnhookWinEvent(_hookForeground);
        if (_hookDestroy != nint.Zero) NativeMethods.UnhookWinEvent(_hookDestroy);
    }

    private void OnWinEvent(nint hWinEventHook, uint eventType, nint hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        // 只关心顶层窗口对象（OBJID_WINDOW=0）；忽略子对象/菜单等各类细粒度事件。
        if (idObject != NativeMethods.OBJID_WINDOW)
            return;

        // 销毁事件需排除已不存在的句柄；其余事件驱动一次快速刷新即可。
        if (eventType == NativeMethods.EVENT_OBJECT_DESTROY && !NativeMethods.IsWindow(hwnd))
            return;

        // 回调在钩子线程触发，切换回 UI 线程执行刷新；同刻多事件自动合并为一次。
        _dispatcher.TryEnqueue(Refresh);
    }

    public void Refresh()
    {
        var list = new List<TaskWindow>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            TryCollect(hwnd, list);
            return true;
        }, nint.Zero);

        // 仅当内容变化才广播（窗口增/减/标题改），避免无效重排。
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

        // 解析进程 exe 路径作为真实图标缓存键；失败为 null（届时回退字母块）。
        string? processPath = NativeMethods.GetProcessPathFromWindow(hwnd);

        list.Add(new TaskWindow(hwnd, pid, title, processPath));
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
        _running = false;
        // 发消息唤出钩子线程的阻塞消息泵，使其及时退出。
        if (_hookThreadId != 0)
            NativeMethods.PostThreadMessage(_hookThreadId, WM_WINMAC_WAKE, nint.Zero, nint.Zero);
    }
}