using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.Text;

namespace WinMac.Shell.Dock;

/// <summary>
/// Dock 中的单个图标项：圆角色块 + 首字符 + 底部运行指示点。
/// 纯代码构造（避免额外 XAML 资源），放大镜只改 <see cref="UserControl.Width/Height"/>，
/// 由 <see cref="DockWindow"/> 用 Canvas 布局驱动。
/// </summary>
public sealed class DockIconView : UserControl
{
    private readonly Border _icon;
    private readonly TextBlock _initial;
    private readonly Border _dot;
    private readonly Border _highlight;

    public DockIconView(string title, Color accent)
    {
        _icon = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(accent),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        _initial = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(title) ? "?" : title.Trim().Substring(0, 1).ToUpperInvariant(),
            Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)),
            FontSize = 22,
            FontWeight = new FontWeight { Weight = 600 },
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };

        _dot = new Border
        {
            Width = 7,
            Height = 7,
            CornerRadius = new CornerRadius(3.5),
            Background = new SolidColorBrush(Color.FromArgb(255, 240, 41, 108)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 4),
            IsHitTestVisible = false,
            Opacity = 1,
        };

        _highlight = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(12),
            Margin = new Thickness(4),
            IsHitTestVisible = false,
            Opacity = 0,
        };

        var grid = new Grid();
        grid.Children.Add(_icon);
        grid.Children.Add(_highlight);
        grid.Children.Add(_initial);
        grid.Children.Add(_dot);
        Content = grid;

        PointerPressed += (_, _) => Clicked?.Invoke(this, this);
    }

    /// <summary>点击（PointerPressed）触发。</summary>
    public event EventHandler<DockIconView>? Clicked;

    /// <summary>高亮活跃窗口（前台）。</summary>
    public void SetActive(bool active) => _highlight.Opacity = active ? 1 : 0;
}