using Microsoft.UI.Xaml;
using WinMac.Core.Configuration;
using WinMac.Core.Services;

namespace WinMac.UI.Settings;

/// <summary>
/// 应用当前主题到 <see cref="Application"/>。
/// “跟随系统”会读取 OS 的 AppsUseLightTheme，把 RequestedTheme 落到实际的浅/深。
/// </summary>
public static class ThemeManager
{
    public static void Apply(string theme)
    {
        if (Application.Current is not { } app)
            return;

        app.RequestedTheme = theme switch
        {
            "Light" => ApplicationTheme.Light,
            "Dark" => ApplicationTheme.Dark,
            _ => SystemTheme.Current == "Light" ? ApplicationTheme.Light : ApplicationTheme.Dark,
        };
    }

    /// <summary>是否需在启动时显式设置主题（System 交给默认跟随 OS）。</summary>
    public static bool ShouldForceOnStartup(AppConfig config)
        => config.Theme is "Light" or "Dark";
}