using WinMac.Core.Win32;

namespace WinMac.Core.Services;

/// <summary>封装开机自启的注册表开关（HKCU\...\Run，无需管理员）。</summary>
public static class AutoStartService
{
    private const string RunValueName = "WinMac";

    public static void SetEnabled(bool enabled)
        => NativeMethods.SetRunAtStartup(RunValueName, enabled ? Environment.ProcessPath : null);
}