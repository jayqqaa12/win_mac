using System.Runtime.InteropServices;
using System.Text;

namespace WinMac.Core.Win32;

/// <summary>本程序所需的 Win32 P/Invoke 全集。随功能新增持续补充。</summary>
internal static partial class NativeMethods
{
    // ---- 窗口枚举 / 信息 ----
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;
    public const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;
    public const uint EVENT_SYSTEM_CLOSE = 0x0018;
    public const uint EVENT_SYSTEM_DIALOGOPEN = 0x0019;
    public const uint EVENT_OBJECT_DESTROY = 0x8001;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;

    public delegate void WinEventDelegate(nint hWinEventHook, uint eventType, nint hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")]
    public static extern nint SetWinEventHook(uint eventMin, uint eventMax, nint hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    public static extern bool UnhookWinEvent(nint hWinEventHook);

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(nint hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern int GetWindowTextLength(nint hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(nint hWnd, int nCmdShow);

    public const int SW_RESTORE = 9;

    // ---- 未打包自启动注册表 ----
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int RegSetValueEx(nint hKey, string lpValueName, int reserved, int dwType, byte[] lpData, int cbData);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int RegCreateKeyEx(nint hKey, string lpSubKey, int reserved, string? lpClass, int dwOptions, int samDesired, nint lpSecurityAttributes, out nint phkResult, out int lpdwDisposition);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int RegDeleteValue(nint hKey, string lpValueName);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int RegCloseKey(nint hKey);

    private static readonly nint HKEY_CURRENT_USER = unchecked((nint)0x80000001UL);
    private const int REG_SZ = 1;
    private const int KEY_WRITE = 0x20006;
    private const int KEY_QUERY_VALUE = 0x1;
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>写/删当前用户 Run 键，控制开机自启。</summary>
    public static void SetRunAtStartup(string valueName, string? commandLine)
    {
        nint key = nint.Zero;
        try
        {
            RegCreateKeyEx(HKEY_CURRENT_USER, RunKeyPath, 0, null, 0, KEY_WRITE | KEY_QUERY_VALUE, nint.Zero, out key, out _);
            if (key == nint.Zero) return;

            if (commandLine is null)
            {
                RegDeleteValue(key, valueName);
            }
            else
            {
                byte[] bytes = Encoding.Unicode.GetBytes(commandLine + "\0");
                RegSetValueEx(key, valueName, 0, REG_SZ, bytes, bytes.Length);
            }
        }
        finally
        {
            if (key != nint.Zero) RegCloseKey(key);
        }
    }
}