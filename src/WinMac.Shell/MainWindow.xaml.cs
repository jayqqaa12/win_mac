using Microsoft.UI.Xaml;
using WinMac.Core.Configuration;

namespace WinMac.Shell;

public sealed partial class MainWindow : Window
{
    public MainWindow(AppConfig config)
    {
        InitializeComponent();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(520, 360));
        StatusText.Text = $"Dock 启用: {config.DockEnabled} · 主题: {config.Theme} · 配置目录: {ConfigStore.ConfigDir}";
    }
}