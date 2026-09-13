using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinMac.Text;
using Windows.UI.Text;

namespace WinMac.UI.Controls;

/// <summary>
/// 应用 macOS 质感文字参数的文本控件封装。
/// 将 Gamma/Contrast/Softness 三参数交给 <see cref="MacTextRenderer"/> 映射为
/// 「字重 + 透明度 + 位图缓存(灰度AA)」三个可观察维度；参数变化会实时重刷。
/// </summary>
public sealed partial class MacTextBlock : UserControl
{
    public MacTextBlock()
    {
        InitializeComponent();
        Apply();
    }

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(MacTextBlock),
            new PropertyMetadata(string.Empty, (d, _) => ((MacTextBlock)d).Apply()));

    public static readonly DependencyProperty FontSizeProperty =
        DependencyProperty.Register(nameof(FontSize), typeof(double), typeof(MacTextBlock),
            new PropertyMetadata(14d, (d, _) => ((MacTextBlock)d).Apply()));

    public static readonly DependencyProperty GammaProperty =
        DependencyProperty.Register(nameof(Gamma), typeof(double), typeof(MacTextBlock),
            new PropertyMetadata(1.8, (d, _) => ((MacTextBlock)d).Apply()));

    public static readonly DependencyProperty ContrastProperty =
        DependencyProperty.Register(nameof(Contrast), typeof(double), typeof(MacTextBlock),
            new PropertyMetadata(0.9, (d, _) => ((MacTextBlock)d).Apply()));

    public static readonly DependencyProperty SoftnessProperty =
        DependencyProperty.Register(nameof(Softness), typeof(double), typeof(MacTextBlock),
            new PropertyMetadata(0.35, (d, _) => ((MacTextBlock)d).Apply()));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public double Gamma
    {
        get => (double)GetValue(GammaProperty);
        set => SetValue(GammaProperty, value);
    }

    public double Contrast
    {
        get => (double)GetValue(ContrastProperty);
        set => SetValue(ContrastProperty, value);
    }

    public double Softness
    {
        get => (double)GetValue(SoftnessProperty);
        set => SetValue(SoftnessProperty, value);
    }

    private void Apply()
    {
        if (Label == null)
            return;

        var style = MacTextRenderer.Compute(Gamma, Contrast, Softness);
        Label.Text = Text;
        Label.FontSize = FontSize;
        Label.FontWeight = new FontWeight { Weight = (ushort)style.FontWeight };
        // 透明度：揉合了柔化度(“变轻”观感)。
        Label.Opacity = style.Opacity;
        // 柔化化 → 位图缓存：去掉 ClearType 亚像素彩边，更接近 macOS 灰度 AA。
        SoftenEffect.Apply(Label, style.SoftnessBlur);
    }
}