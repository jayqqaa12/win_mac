using Microsoft.UI.Xaml;
using WinMac.Core.Configuration;

namespace WinMac.Shell;

public partial class App : Application
{
    private const string SingleInstanceName = @"Local\WinMac.Shell.SingleInstance";
    private static Mutex? _mutex;
    private Window? _window;

    public App()
    {
        InitializeComponent();

        // 非打包模式下，让内容能跟随 Windows 亮/暗设置。
        RequestedTheme = ApplicationTheme.Dark;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // 单实例：已有一个实例则直接退出，由既有实例承担。
        _mutex = new Mutex(true, SingleInstanceName, out bool createdNew);
        if (!createdNew)
        {
            Environment.Exit(0);
            return;
        }

        // 加载配置（M0 先确保 Core 集成可用，Dock 为此读取）。
        AppConfig config = ConfigStore.Load();

        _window = new MainWindow(config);
        _window.Activate();
    }
}