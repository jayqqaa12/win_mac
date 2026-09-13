using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Text;

namespace WinMac.Shell.Dock;

/// <summary>
/// Dock 中的单个图标项：真实应用图标 + 底部运行指示点 + 前台高亮边框。
/// 无真实图标时退化为“圆角色块 + 首字符”。
/// 按下并横向移动触发 <see cref="ReorderRequested"/>（拖拽换序）；原地抬起触发 <see cref="Clicked"/>。
/// 放大镜只改 <see cref="UserControl.Width/Height"/>，由 <see cref="DockWindow"/> 用 Canvas 布局驱动。
/// </summary>
public sealed class DockIconView : UserControl
{
    private const double DragThreshold = 8; // 判定为拖拽的最小水平位移(逻辑px)。

    private readonly Border _icon;
    private readonly TextBlock _initial;
    private readonly Border _dot;
    private readonly Border _highlight;
    private readonly Image _img;

    private Point _pressPoint;
    private bool _pressed;
    private bool _dragged;

    public DockIconView(string title, Color accent, ImageSource? icon)
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

        _img = new Image { Stretch = Stretch.Uniform };

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
        grid.Children.Add(new Border { Child = _img, Margin = new Thickness(8) });
        grid.Children.Add(_dot);
        Content = grid;

        Icon = icon; // 同步可见性与初始源

        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += (_, _) => { _pressed = false; _dragged = false; };
    }

    /// <summary>点击（原地抬起）触发。</summary>
    public event EventHandler<DockIconView>? Clicked;

    /// <summary>拖拽换序：<see cref="DockIconView"/> 与横向位移 dx（逻辑px）。</summary>
    public event Action<DockIconView, double>? ReorderRequested;

    /// <summary>当前真实图标源；null 表示字母块兜底。</summary>
    public ImageSource? Icon
    {
        get => _img.Source;
        set => apply(value);
    }

    private void apply(ImageSource? source)
    {
        _img.Source = source;
        bool hasIcon = source is not null;
        _img.Visibility = hasIcon ? Visibility.Visible : Visibility.Collapsed;
        _icon.Visibility = hasIcon ? Visibility.Collapsed : Visibility.Visible;
        _initial.Visibility = hasIcon ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _pressPoint = e.GetCurrentPoint(this).Position;
        _pressed = true;
        _dragged = false;
        CapturePointer(e.Pointer);
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_pressed)
            return;
        double dx = e.GetCurrentPoint(this).Position.X - _pressPoint.X;
        if (Math.Abs(dx) > DragThreshold && !_dragged)
            _dragged = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        ReleasePointerCaptures();
        if (!_pressed)
            return;
        _pressed = false;

        double dx = e.GetCurrentPoint(this).Position.X - _pressPoint.X;
        if (_dragged)
            ReorderRequested?.Invoke(this, dx);
        else
            Clicked?.Invoke(this, this);
    }

    /// <summary>高亮活跃窗口（前台）。</summary>
    public void SetActive(bool active) => _highlight.Opacity = active ? 1 : 0;
}