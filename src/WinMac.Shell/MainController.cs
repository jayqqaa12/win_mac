using Microsoft.UI.Xaml;
using WinMac.Core.Configuration;
using WinMac.UI.Settings;

namespace WinMac.Shell;

/// <summary>
/// 应用引导/生命周期入口(M1)：载配置 → 应用主题 → 创建主窗口。
/// 后续 M2 起在此懒加载 Dock / Launchpad / Widgets，未启用模块不占窗口与线程。
/// </summary>
public sealed class MainController
{
    private readonly AppConfig _config;

    public MainController(AppConfig config) => _config = config;

    public Window MainWindow { get; private set; } = null!;

    public void Launch()
    {
        // 仅 浅/深 才需强设请求主题；System 交由框架默认跟随 OS。
        if (ThemeManager.ShouldForceOnStartup(_config))
            ThemeManager.Apply(_config.Theme);

        MainWindow = new MainWindow(_config);
        MainWindow.Activate();
    }

    public void OpenSettings() => new SettingsWindow(_config).Activate();
}