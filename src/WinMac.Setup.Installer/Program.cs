using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace WinMac.Setup.Installer;

/// <summary>
/// MacType 式全局字体灰度渲染 —— 提权安装器 / 注入器。
/// 架构：
///  - 安装：把 MacTypeHook.dll 复制到 System32（全局钩子需在任意进程可解析）+ Program Files\WinMac，
///           写 Run 注册表项实现登录自启；随后以 --inject 模式后台拉起注入常驻进程。
///  - 注入：LoadLibrary(MacTypeHook.dll) 后 SetWindowsHookEx(WH_GETMESSAGE, proc, hMod, 0)
///           把钩子 DLL 注入到所有 GUI 进程；DLL 内部已在 DllMain 装好 GDI 灰度字体复写。
///           注入进程需常驻（全局钩子随钩线程存活），借隐藏窗口 + 计时器监听"卸载事件"实现优雅退出。
///  - 卸载：删除 Run 项，并通过命名事件通知常驻注入进程退出。
/// </summary>
internal static class Program
{
    // ---- 路径 / 命名 ----
    private const string Name = "WinMacFontHook";
    private static readonly string InjectorExe = Environment.ProcessPath!;

    private static readonly string InstallDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WinMac");

    private static readonly string SystemDll =
        Path.Combine(Environment.SystemDirectory, "MacTypeHook.dll");

    private static readonly string StopEventName = @"Global\WinMac.FontHook.Stop";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>隐藏窗口过程委托（需常驻引用，防止被 GC 回收后回调野指针）。</summary>
    private static readonly NativeMethods.WndProcDelegate WndProc = WndProcImpl;

    private static string LogPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                     "WinMac", "font-hook.log");

    private static int Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0].TrimStart('-').ToLowerInvariant() : "install";
        try
        {
            return mode switch
            {
                "inject" => RunInjector(),
                "install" => Install(),
                "uninstall" => Uninstall(),
                _ => Fail($"未知模式: {mode}（可用: install / inject / uninstall）"),
            };
        }
        catch (Exception ex)
        {
            string msg = $"发生异常: {ex}";
            Log(msg);
            MessageBox(msg);
            return 2;
        }
    }

    private static int Install()
    {
        string dll = Path.Combine(AppContext.BaseDirectory, "MacTypeHook.dll");
        if (!File.Exists(dll))
        {
            string m = $"未找到 MacTypeHook.dll（应位于安装器同目录）: {dll}";
            Log(m);
            MessageBox(m);
            return 2;
        }

        Directory.CreateDirectory(InstallDir);
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);

        // 1) 复制 DLL —— System32 保证任意进程经全局钩子都能按绝对路径加载。
        File.Copy(dll, InstalledDllPath(), true);
        // 2) 复制到 System32。
        CopyToSystem32(dll);
        // 3) 写 Run 注册表，登录自启为常驻注入进程。
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)!)
        {
            key.SetValue(Name, $"\"{InjectorExe}\" --inject", RegistryValueKind.String);
        }
        // 4) 确保旧的注入进程退出，再后台拉起新的。
        SignalStop();
        Thread.Sleep(200);
        Process.Start(new ProcessStartInfo(InjectorExe, "--inject") { UseShellExecute = false, CreateNoWindow = true });

        string ok = "WinMac 字体钩子已安装。\n\n已复制 MacTypeHook.dll 到 System32，并注册登录自启。";
        Log(ok);
        MessageBox(ok);
        return 0;
    }

    private static int Uninstall()
    {
        // 删除 Run 项（当前用户）
        using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true))
        {
            key?.DeleteValue(Name, throwOnMissingValue: false);
        }
        // 通知常驻注入进程退出
        bool signaled = SignalStop();
        Log($"已卸载字体钩子注册项；已通知注入进程退出: {signaled}");
        MessageBox("已卸载 WinMac 字体钩子（自启项）。\n已运行的进程里生效的钩子会在这些进程退出后自然失效。");
        return 0;
    }

    /// <summary>常驻注入进程：设置全局钩子并进入消息循环，直到“卸载事件”被触发。</summary>
    private static int RunInjector()
    {
        if (!File.Exists(SystemDll))
        {
            Log($"未安装 MacTypeHook.dll（{SystemDll}）");
            return 2;
        }

        nint hMod = NativeMethods.LoadLibraryW(SystemDll);
        if (hMod == 0)
        {
            Log($"LoadLibrary 失败: {Marshal.GetLastWin32Error()}");
            return 2;
        }

        nint proc = NativeMethods.GetProcAddress(hMod, "MacTypeHookProc");
        if (proc == 0)
        {
            Log("未找到导出 MacTypeHookProc");
            return 2;
        }

        // 全局钩子：threadId = 0 → DLL 被映射进所有 GUI 进程并触发其 DllMain 安装字体复写。
        nint hhk = NativeMethods.SetWindowsHookEx(NativeMethods.WH_GETMESSAGE, proc, hMod, 0);
        if (hhk == 0)
        {
            Log($"SetWindowsHookEx 失败: {Marshal.GetLastWin32Error()}");
            return 2;
        }

        Log($"全局字体钩子已生效 (hhk=0x{hhk:x})");
        PumpUntilStop(hhk);
        NativeMethods.UnhookWindowsHookEx(hhk);
        NativeMethods.FreeLibrary(hMod);
        return 0;
    }

    /// <summary>注册一个隐藏窗口 + 1s 计时器，在消息循环中监听“卸载事件”。</summary>
    private static void PumpUntilStop(nint hhk)
    {
        // 注册隐藏窗口
        var className = "WinMacFontHookWnd";
        NativeMethods.WNDCLASS wc = new()
        {
            hInstance = NativeMethods.GetModuleHandleW(null),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(WndProc),
            lpszClassName = className,
        };
        if (NativeMethods.RegisterClass(ref wc) == 0 && Marshal.GetLastWin32Error() != 1410)
        {
            Log($"RegisterClass 失败: {Marshal.GetLastWin32Error()}");
            return;
        }
        nint hWnd = NativeMethods.CreateWindowEx(0, className, Name, 0, 0, 0, 0, 0,
            IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
        if (hWnd == 0)
        {
            Log($"CreateWindowEx 失败: {Marshal.GetLastWin32Error()}");
            return;
        }
        NativeMethods.SetTimer(hWnd, 1, 1000, IntPtr.Zero);

        // 消息泵
        while (NativeMethods.GetMessage(out NativeMethods.MSG msg, IntPtr.Zero, 0, 0))
        {
            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessage(ref msg);
            if (msg.message == NativeMethods.WM_QUIT)
                break;
        }

        NativeMethods.KillTimer(hWnd, 1);
        NativeMethods.DestroyWindow(hWnd);
    }

    /// <summary>私有 WndProc：WM_TIMER 时检查卸载事件，命中则关闭消息泵。</summary>
    private static IntPtr WndProcImpl(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == NativeMethods.WM_TIMER)
        {
            if (EventWaitHandle.TryOpenExisting(StopEventName, out var stop))
            {
                using (stop)
                {
                    if (stop.WaitOne(0))
                    {
                        NativeMethods.PostQuitMessage(0);
                        return IntPtr.Zero;
                    }
                }
            }
        }
        return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private static bool SignalStop()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(StopEventName, out var stop))
            {
                using (stop) { stop.Set(); return true; }
            }
        }
        catch { /* 权限或不存在则忽略 */ }
        return false;
    }

    private static string InstalledDllPath() => Path.Combine(InstallDir, "MacTypeHook.dll");

    private static void CopyToSystem32(string dll)
    {
        try { File.Copy(dll, SystemDll, overwrite: true); }
        catch (Exception ex) { Log($"复制到 System32 失败: {ex.Message}"); }
    }

    private static int Fail(string msg) { Log(msg); MessageBox(msg); return 2; }

    private static void Log(string message)
    {
        try
        {
            File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch { /* 不阻碍主流程 */ }
    }

    private static void MessageBox(string text) =>
        NativeMethods.MessageBoxW(IntPtr.Zero, text, "WinMac Font Hook", 0x00000040 /* MB_ICONINFORMATION */);

    private static class NativeMethods
    {
        // ---- 类型 ----
        public const int WH_GETMESSAGE = 3;
        public const uint WM_TIMER = 0x0113;
        public const uint WM_QUIT = 0x0012;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG
        {
            public nint hwnd;
            public uint message;
            public nint wParam;
            public nint lParam;
            public uint time;
            public POINT pt;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WNDCLASS
        {
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string lpszMenuName;
            public string lpszClassName;
        }

        public delegate nint WndProcDelegate(nint hWnd, uint msg, nint wParam, nint lParam);

        // ---- user32 ----
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern nint LoadLibraryW(string lpFileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        public static extern nint GetProcAddress(nint hModule, string procName);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool FreeLibrary(nint hLibModule);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern nint SetWindowsHookEx(int idHook, nint lpfn, nint hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(nint hhk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern nint GetModuleHandleW(string? lpModuleName);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern ushort RegisterClass(ref WNDCLASS lpWndClass);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern nint CreateWindowEx(uint dwExStyle, string lpClassName, string lpWindowName,
            uint dwStyle, int x, int y, int nWidth, int nHeight,
            nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern nint SetTimer(nint hWnd, nint nIDEvent, uint uElapse, nint lpTimerFunc);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern nint KillTimer(nint hWnd, nint nIDEvent);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyWindow(nint hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern nint DispatchMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        public static extern void PostQuitMessage(int nExitCode);

        [DllImport("user32.dll")]
        public static extern nint DefWindowProc(nint hWnd, uint msg, nint wParam, nint lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int MessageBoxW(nint hWnd, string text, string caption, uint type);
    }
}