using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI;
using WinMac.Core.Configuration;
using WinMac.Core.Win32;

namespace WinMac.Shell.Dock;

/// <summary>
/// Dock 停靠条：无边框、置顶、贴屏幕底部的常驻叠加层。
/// 图标由 <see cref="TaskMonitor"/> 提供，鼠标在其上方移动时呈现 macOS 式放大镜；
/// 支持停靠提示、右键菜单与 Auto/Smart-Hide。
/// </summary>
public sealed class DockWindow : Window
{
    // 无标题栏窗口样式（Dock 专用）。
    private const int GWL_STYLE = -16;
    private const long WS_CAPTION = 0x00C00000;
    private const long WS_THICKFRAME = 0x00040000;
    private const long WS_VISIBLE = 0x10000000;

    private const int SW_MINIMIZE = 6;
    private const int SW_SHOW = 5;
    private const int SW_HIDE = 0;
    private const double DockHeight = 72;
    private const double IconGap = 6;
    private const int EdgeRevealPad = 6; // 屏幕底端触发热区高度(px)。

    private readonly AppConfig _config;
    private readonly TaskMonitor _monitor;
    private readonly Canvas _root;
    private readonly List<DockIconView> _views = new();
    private readonly Dictionary<DockIconView, TaskWindow> _map = new();
    private readonly List<(DockIconView view, double x1, double x2, double top)> _placed = new();

    private readonly Border _tooltip;
    private readonly TextBlock _tooltipText;
    private readonly Button _launchButton;
    private DispatcherQueueTimer? _hideTimer;
    private bool _visible = true;

    private nint _hwnd;
    private double _dockWidth;

    /// <summary>用户点击 Dcok 左侧“启动台”按钮时触发。</summary>
    public event Action? LaunchpadRequested;

    public DockWindow(AppConfig config)
    {
        _config = config;
        _monitor = new TaskMonitor(DispatcherQueue, TimeSpan.FromSeconds(1.5));
        _monitor.Changed += OnTasksChanged;

        _root = new Canvas
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };
        _root.PointerMoved += OnPointerMoved;
        _root.PointerExited += OnRootPointerExited;
        Content = _root;

        _tooltipText = new TextBlock { FontSize = 12, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) };
        _tooltip = new Border
        {
            Child = _tooltipText,
            Background = new SolidColorBrush(Color.FromArgb(230, 48, 48, 48)),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(10, 5, 10, 5),
            MaxWidth = 420,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };
        Canvas.SetZIndex(_tooltip, 1000);
        _root.Children.Add(_tooltip);

        // 左侧固定的“启动台”触发按钮（不参与放大镜布局）。
        _launchButton = new Button
        {
            Width = 44,
            Height = 44,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(12),
            Content = new FontIcon { Glyph = "\uE71B", FontSize = 18 },
        };
        Canvas.SetLeft(_launchButton, 16);
        Canvas.SetTop(_launchButton, (DockHeight - 44) / 2);
        _launchButton.Click += (_, _) => LaunchpadRequested?.Invoke();
        _root.Children.Add(_launchButton);

        Activated += OnActivated;
    }

    /// <summary>显示 Dock 并启动任务监听。</summary>
    public void Run()
    {
        Activate();
        _monitor.Start();
        _root.LayoutUpdated += (_, _) => ReLayout(double.NegativeInfinity);
        StartHideWatcher();
    }

    private void StartHideWatcher()
    {
        // 非“常显”才需要后台热区监听。
        if (_config.DockHideMode == "Always")
            return;
        _hideTimer = DispatcherQueue.CreateTimer();
        _hideTimer.Interval = TimeSpan.FromMilliseconds(200);
        _hideTimer.Tick += (_, _) => HideTick();
        _hideTimer.Start();
    }

    private void OnActivated(object? sender, WindowActivatedEventArgs e)
    {
        if (_hwnd == nint.Zero)
            StyleAndPlace();
    }

    private void StyleAndPlace()
    {
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        // 去掉标题栏/可调边框 → 无边框。
        long style = NativeMethods.GetWindowLongPtr(_hwnd, GWL_STYLE).ToInt64();
        style &= ~(WS_CAPTION | WS_THICKFRAME);
        NativeMethods.SetWindowLongPtr(_hwnd, GWL_STYLE, (nint)style);

        // 不占任务栏 + 不抢焦点。
        long ex = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        ex |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, (nint)ex);

        // 贴主屏工作区底部，全宽。
        var area = DisplayArea.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(_hwnd), DisplayAreaFallback.Primary);
        var wa = area.WorkArea;
        _dockWidth = wa.Width;

        _root.Width = wa.Width;
        _root.Height = DockHeight;
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST,
            wa.X, wa.Y + wa.Height - (int)DockHeight, wa.Width, (int)DockHeight,
            NativeMethods.SWP_NOACTIVATE);
    }

    private void OnTasksChanged()
    {
        // 以“hWnd”为键做增量同步：增则加，减则删，避免全量重建导致放大动画/焦点状态丢失。
        var desired = new Dictionary<nint, TaskWindow>();
        foreach (var t in _monitor.Items)
            desired[t.Hwnd] = t;

        var current = _views.ToDictionary(v => _map[v].Hwnd);

        foreach (var (hwnd, view) in current)
        {
            if (!desired.ContainsKey(hwnd))
            {
                _root.Children.Remove(view);
                _views.Remove(view);
                _map.Remove(view);
            }
        }

        foreach (var (hwnd, task) in desired)
        {
            if (current.ContainsKey(hwnd))
                continue;
            var view = new DockIconView(task.Title, AccentColor(task));
            view.Width = _config.DockIconSize;
            view.Height = _config.DockIconSize;
            view.Clicked += OnIconClicked;
            view.RightTapped += OnIconRightTapped;
            _root.Children.Add(view);
            _views.Add(view);
            _map[view] = task;
        }

        ReLayout(double.NegativeInfinity);
    }

    private static Color AccentColor(TaskWindow task)
    {
        // 用窗口标题哈希出稳定色相，让各 app 图标颜色各不相同；亮度压低以便白色字母可读。
        uint h = 2166136261u;
        foreach (char c in task.Title)
        {
            h ^= c;
            h *= 16777619;
        }
        var hsl = new HslState(h % 360, 0.55, 0.42);
        var (r, g, b) = RgbToHsv(hsl);
        return Windows.UI.Color.FromArgb(255, r, g, b);
    }

    private static (byte r, byte g, byte b) RgbToHsv(HslState hsl)
    {
        (double r, double g, double b) = HslToRgb(hsl.H, hsl.S, hsl.L);
        return ((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
    }

    private static (double, double, double) HslToRgb(double h, double s, double l)
    {
        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
        double m = l - c / 2;
        double r, g, b;
        var rgb = h switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        (r, g, b) = rgb;
        return (r + m, g + m, b + m);
    }

    private readonly record struct HslState(double H, double S, double L);

    private void OnIconClicked(object sender, DockIconView view)
    {
        if (!_map.TryGetValue(view, out var task))
            return;

        if (task.IsForeground)
            NativeMethods.ShowWindow(task.Hwnd, SW_MINIMIZE);
        else
        {
            NativeMethods.ShowWindow(task.Hwnd, NativeMethods.SW_RESTORE);
            NativeMethods.SetForegroundWindow(task.Hwnd);
        }
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var pos = e.GetCurrentPoint(_root).Position;
        ReLayout(pos.X);
        UpdateTooltip(pos.X, pos.Y);
    }

    private void OnRootPointerExited(object sender, PointerRoutedEventArgs e)
    {
        ReLayout(double.NegativeInfinity);
        HideTooltip();
    }

    // ---- 停靠提示 ----
    private void UpdateTooltip(double x, double y)
    {
        foreach (var (view, x1, x2, top) in _placed)
        {
            if (x >= x1 && x <= x2 && y >= top)
            {
                ShowTooltip(view, x1, x2, top);
                return;
            }
        }
        HideTooltip();
    }

    private void ShowTooltip(DockIconView view, double x1, double x2, double top)
    {
        if (!_map.TryGetValue(view, out var task))
        {
            HideTooltip();
            return;
        }
        _tooltipText.Text = task.Title;
        _tooltip.Visibility = Visibility.Visible;
        _tooltip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double w = _tooltip.DesiredSize.Width;
        double h = _tooltip.DesiredSize.Height;
        double width = Math.Max(1, _root.ActualWidth > 0 ? _root.ActualWidth : _dockWidth);
        double left = Math.Clamp(x1 + (x2 - x1) / 2 - w / 2, 4, Math.Max(4, width - w - 4));
        double top2 = Math.Max(2, top - h - 4);
        Canvas.SetLeft(_tooltip, left);
        Canvas.SetTop(_tooltip, top2);
    }

    private void HideTooltip()
    {
        _tooltip.Visibility = Visibility.Collapsed;
    }

    // ---- 右键菜单 ----
    private void OnIconRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var view = sender as DockIconView;
        if (view is null || !_map.TryGetValue(view, out var task))
            return;

        var menu = new MenuFlyout();
        var toggle = new MenuFlyoutItem { Text = task.IsForeground ? "最小化窗口" : "恢复并前置" };
        toggle.Click += (_, _) => OnIconClicked(view, view);
        var remove = new MenuFlyoutItem { Text = "从 Dock 移除" };
        remove.Click += (_, _) => RemoveIcon(view);
        menu.Items.Add(toggle);
        menu.Items.Add(remove);
        menu.ShowAt(view);
    }

    private void RemoveIcon(DockIconView view)
    {
        _root.Children.Remove(view);
        _views.Remove(view);
        _map.Remove(view);
        _tooltip.Visibility = Visibility.Collapsed;
        ReLayout(double.NegativeInfinity);
    }

    // ---- Auto/Smart-Hide ----
    private void HideTick()
    {
        if (_hwnd == nint.Zero)
            return;

        NativeMethods.GetCursorPos(out var p);
        int screenH = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);
        int screenW = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);

        // 智能隐藏：前台窗口全屏铺满时强制隐藏。
        if (_config.DockHideMode == "SmartHide")
        {
            var fg = NativeMethods.GetForegroundWindow();
            if (fg != nint.Zero && fg != _hwnd)
            {
                NativeMethods.GetWindowRect(fg, out var r);
                if (r.Left <= 0 && r.Top <= 0 && r.Right >= screenW && r.Bottom >= screenH)
                {
                    if (_visible) HideDock();
                    return;
                }
            }
        }

        bool overHot = p.Y >= screenH - EdgeRevealPad;
        bool overDockBand = p.Y >= screenH - (int)DockHeight;

        if (overHot && !_visible)
            ShowDock();
        else if (!overDockBand && _visible)
            HideDock();
    }

    private void ShowDock()
    {
        if (_visible || _hwnd == nint.Zero)
            return;
        NativeMethods.ShowWindow(_hwnd, SW_SHOW);
        _visible = true;
        HideTooltip();
    }

    private void HideDock()
    {
        if (!_visible || _hwnd == nint.Zero)
            return;
        NativeMethods.ShowWindow(_hwnd, SW_HIDE);
        _visible = false;
        HideTooltip();
    }

    /// <summary>
    /// macOS 式放大镜 + 布局：每个图标以“无放大稳态中心”到鼠标的距离取放大系数，
    /// 依次从左到右摆放（放大图标顶开邻居），整体水平居中。
    /// </summary>
    private void ReLayout(double pointerX)
    {
        int n = _views.Count;
        if (n == 0)
        {
            _placed.Clear();
            return;
        }

        double baseSize = _config.DockIconSize;
        double maxSize = _config.DockMaxIconSize;
        double radius = Math.Max(1, _config.MagnifyRadius);
        double unit = baseSize + IconGap;
        double viewWidth = Math.Max(1, _root.ActualWidth > 0 ? _root.ActualWidth : _dockWidth);
        double viewHeight = Math.Max(1, _root.ActualHeight > 0 ? _root.ActualHeight : DockHeight);

        const double leftSlot = 84; // 为左侧“启动台”按钮预留，避免重叠。
        double usableW = Math.Max(1, viewWidth - leftSlot);

        // 稳态中心(相对窗口)，鼠标 x 同坐标系。
        var sizes = new double[n];
        for (int i = 0; i < n; i++)
        {
            double center = leftSlot + usableW / 2 + (i - (n - 1) / 2d) * unit;
            double d = Math.Abs(center - pointerX);
            double t = Math.Clamp(1 - d / radius, 0, 1);
            // 平滑衰减：近处放大更多。
            t = t * t * 0.7 + t * 0.3;
            sizes[i] = baseSize + (maxSize - baseSize) * t;
        }

        double total = sizes.Sum() + IconGap * (n - 1);
        double start = leftSlot + Math.Max(8, (usableW - total) / 2);
        double x = start;
        double centerY = viewHeight / 2;

        _placed.Clear();
        for (int i = 0; i < n; i++)
        {
            var view = _views[i];
            view.Width = sizes[i];
            view.Height = sizes[i];
            view.SetActive(_map[view].IsForeground);
            Canvas.SetLeft(view, x);
            Canvas.SetTop(view, centerY - sizes[i] / 2);
            _placed.Add((view, x, x + sizes[i], centerY - sizes[i] / 2));
            x += sizes[i] + IconGap;
        }
    }
}