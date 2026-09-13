using Microsoft.UI.Xaml;
using WinMac.Core.Configuration;
using WinMac.Core.Services;
using WinMac.Core.Win32;
using WinMac.Shell.Dock;
using WinMac.Shell.Launchpad;
using WinMac.Shell.Settings;
using WinMac.Shell.Widgets;

namespace WinMac.Shell;

/// <summary>
/// 应用引导/生命周期入口：载配置 → 应用主题 → 懒加载各叠加层模块 → 创建主窗口。
/// 未启用模块不占窗口与线程（如 Dock 开启才创建 DockWindow）。
/// </summary>
public sealed class MainController
{
    private readonly AppConfig _config;
    private readonly List<WidgetWindow> _widgets = new();
    private DockWindow? _dock;
    private LaunchpadWindow? _launchpad;
    private bool _launchpadOpen;

    public MainController(AppConfig config) => _config = config;

    public Window MainWindow { get; private set; } = null!;

    public void Launch()
    {
        // 仅 浅/深 才需强设请求主题；System 交由框架默认跟随 OS。
        if (ThemeManager.ShouldForceOnStartup(_config))
            ThemeManager.Apply(_config.Theme);

        // 维持开机自启配置与注册表一致。
        AutoStartService.SetEnabled(_config.AutoStart);

        // 懒加载 Dock（叠加层），不启用则不创建。
        if (_config.DockEnabled)
        {
            _dock = new DockWindow(_config);
            _dock.LaunchpadRequested += ToggleLaunchpad;
            _dock.Run();
        }

        // 桌面小组件（可关闭）。
        if (_config.WidgetsEnabled)
            LaunchWidgets();

        MainWindow = new MainWindow(_config);
        MainWindow.Activate();
    }

    /// <summary>在主屏右上角纵向排布四个小组件。</summary>
    private void LaunchWidgets()
    {
        int screenW = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
        int x = screenW - 280;
        int y = 32;

        foreach (WidgetKind kind in Enum.GetValues<WidgetKind>())
        {
            var w = new WidgetWindow(kind);
            w.ShowAt(x, y);
            _widgets.Add(w);
            y += 148;
        }
    }

    /// <summary>由 Dock 的“启动台”按钮触发，切换全屏应用网格的显隐。</summary>
    private void ToggleLaunchpad()
    {
        _launchpad ??= new LaunchpadWindow();
        if (_launchpadOpen)
        {
            _launchpad.HideOverlay();
            _launchpadOpen = false;
        }
        else
        {
            _launchpad.ShowOverlay();
            _launchpadOpen = true;
        }
    }

    public void OpenSettings() => new SettingsWindow(_config).Activate();
}