using Microsoft.Win32;

namespace WinMac.Core.Services;

/// <summary>读取 Windows 的系统主题偏好（应用浅/深色），供“跟随系统”主题选项使用。</summary>
public static class SystemTheme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string ValueName = "AppsUseLightTheme";

    /// <summary>当前应用主题："Light" 或 "Dark"。</summary>
    public static string Current
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
                if (key?.GetValue(ValueName) is int v)
                    return v == 1 ? "Light" : "Dark";
            }
            catch
            {
                // 读失败退回深色默认，不崩溃。
            }
            return "Dark";
        }
    }
}