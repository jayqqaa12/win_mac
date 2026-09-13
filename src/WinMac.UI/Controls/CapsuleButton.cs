using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WinMac.UI.Controls;

/// <summary>macOS 风格的胶囊(圆角)按钮：纯代码实现，外观沿用默认按钮模板(自动跟随亮/暗主题)。</summary>
public sealed class CapsuleButton : Button
{
    public CapsuleButton()
    {
        CornerRadius = new CornerRadius(14, 14, 14, 14);
        Padding = new Thickness(20, 10, 20, 10);
        MinHeight = 40;
        FontSize = 15;
        HorizontalAlignment = HorizontalAlignment.Stretch;
    }
}