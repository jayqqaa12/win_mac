using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinMac.Core.Configuration;
using WinMac.UI.Controls;
using WinMac.UI.Settings;

namespace WinMac.Shell;

public sealed partial class MainWindow : Window
{
    private readonly AppConfig _config;

    public MainWindow(AppConfig config)
    {
        InitializeComponent();
        _config = config;

        AppWindow.Resize(new Windows.Graphics.SizeInt32(560, 420));
        WindowPositioner.CenterOnPrimary(this);

        // 用已保存的文字参数初始化预览 A，直观反映当前配置。
        SampleA.Gamma = config.TextGamma;
        SampleA.Contrast = config.TextContrast;
        SampleA.Softness = config.TextSoftness;
    }

    private void OnOpenSettings(object sender, RoutedEventArgs e)
        => new SettingsWindow(_config).Activate();
}