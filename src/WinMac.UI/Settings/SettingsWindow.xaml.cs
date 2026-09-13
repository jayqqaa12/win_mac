using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using WinMac.Core.Configuration;

namespace WinMac.UI.Settings;

public sealed partial class SettingsWindow : Window
{
    private readonly AppConfig _config;

    public SettingsWindow(AppConfig config)
    {
        InitializeComponent();
        _config = config;
        LoadFromConfig();

        // 居中且置中显示（比默认更接近系统设置窗口的开窗位置）。
        AppWindow.Resize(new Windows.Graphics.SizeInt32(760, 560));
        WindowPositioner.CenterOnPrimary(this);
    }

    private void LoadFromConfig()
    {
        ThemeCombo.SelectedValue = _config.Theme switch
        {
            "Light" => "Light",
            "Dark" => "Dark",
            _ => "System",
        };
        GammaSlider.Value = _config.TextGamma;
        ContrastSlider.Value = _config.TextContrast;
        SoftnessSlider.Value = _config.TextSoftness;
        UpdatePreview();
    }

    private void OnMacParamChanged(object sender, RangeBaseValueChangedEventArgs e)
        => UpdatePreview();

    private void UpdatePreview()
    {
        PreviewLabel.Gamma = GammaSlider.Value;
        PreviewLabel.Contrast = ContrastSlider.Value;
        PreviewLabel.Softness = SoftnessSlider.Value;
        GammaValue.Text = GammaSlider.Value.ToString("0.00");
        ContrastValue.Text = ContrastSlider.Value.ToString("0.00");
        SoftnessValue.Text = SoftnessSlider.Value.ToString("0.00");
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        var def = new AppConfig();
        ThemeCombo.SelectedValue = "System";
        GammaSlider.Value = def.TextGamma;
        ContrastSlider.Value = def.TextContrast;
        SoftnessSlider.Value = def.TextSoftness;
        UpdatePreview();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        _config.Theme = (string)(ThemeCombo.SelectedValue ?? "System");
        _config.TextGamma = GammaSlider.Value;
        _config.TextContrast = ContrastSlider.Value;
        _config.TextSoftness = SoftnessSlider.Value;

        ConfigStore.Save(_config);
        ThemeManager.Apply(_config.Theme);
        Close();
    }
}