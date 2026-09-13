using Microsoft.UI.Xaml;
using WinMac.Core.Configuration;

namespace WinMac.Shell;

public partial class App : Application
{
    private const string SingleInstanceName = @"Local\WinMac.Shell.SingleInstance";
    private static Mutex? _mutex;

    public App()
    {
        InitializeComponent();
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

        // 载配置 → 引导(M1：应用主题并创建主窗口)。
        AppConfig config = ConfigStore.Load();
        new MainController(config).Launch();
    }
}