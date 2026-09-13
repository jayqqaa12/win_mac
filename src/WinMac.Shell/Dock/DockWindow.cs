using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Storage;
using Windows.UI;
using WinMac.Core.Configuration;
using WinMac.Core.Win32;

namespace WinMac.Shell.Dock;

/// <summary>
/// Dock 停靠条：无边框、置顶、贴屏幕底部的常驻叠加层。
/// 由「固定的常驻启动项」+「正在运行的窗口」共同组成（后者按其 exe 合并进对应固定项）。
/// 支持：macOS 放大镜、真实图标、悬停 DWM 预览、tooltip、右键菜单、Auto/Smart-Hide、
/// 拖拽换序（持久化）、拖入 exe/快捷方式/文件夹入 Dock、点击启动或恢复/最小化。
/// </summary>
public sealed class DockWindow : Window
{
    // 无标题栏窗口样式（Dock 专用）。
    private const int GWL_STYLE = -16;
    private const long WS_CAPTION = 0x00C00000;
    private const long WS_THICKFRAME = 0x00040000;

    private const int SW_MINIMIZE = 6;
    private const int SW_SHOW = 5;
    private const int SW_HIDE = 0;
    private const double DockHeight = 72;
    private const double IconGap = 6;
    private const int EdgeRevealPad = 6; // 屏幕底端触发热区高度(px)。

    private readonly AppConfig _config;
    private readonly TaskMonitor _monitor;
    private readonly IconCache _icons = new();
    private readonly ThumbnailHost _preview = new();
    private readonly Canvas _root;
    private readonly List<DockIconView> _views = new();
    private readonly Dictionary<DockIconView, DockItem> _map = new();
    private readonly List<(DockIconView view, double x1, double x2, double top)> _placed = new();
    private readonly Dictionary<nint, bool> _prevIconic = new();

    private readonly Border _tooltip;
    private readonly TextBlock _tooltipText;
    private readonly Button _launchButton;
    private DispatcherQueueTimer? _hideTimer;
    private bool _visible = true;

    private nint _hwnd;
    private double _dockWidth;

    /// <summary>用户点击 Dock 左侧“启动台”按钮时触发。</summary>
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
            AllowDrop = true, // 外部拖入 exe/快捷方式/文件夹
        };
        _root.PointerMoved += OnPointerMoved;
        _root.PointerExited += OnRootPointerExited;
        _root.DragOver += OnDragOver;
        _root.Drop += OnDrop;
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
        OnTasksChanged(); // 立即构建固定项
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

    // ================= 数据构建：固定项 + 运行窗口 =================

    /// <summary>稳定视图身份键：固定项用 "pin:path"（与运行实例合并），纯运行项用 "win:hwnd"。</summary>
    private static string ViewKey(DockItem item) =>
        item.PinPath is not null ? "pin:" + item.PinPath : "win:" + item.Hwnd;

    private void OnTasksChanged()
    {
        _items = BuildItems();
        SyncViews();
        ReLayout(double.NegativeInfinity);

        // 最小化 → 播放 genie 收纳动画（仅在状态翻转时刻触发一次）。
        foreach (var item in _items)
        {
            var running = item.Running;
            if (running is null)
                continue;
            bool iconic = running.IsIconic;
            bool was = _prevIconic.GetValueOrDefault(running.Hwnd);
            _prevIconic[running.Hwnd] = iconic;
            if (iconic && !was)
                PlayGenie(running);
        }
    }

    private List<DockItem> _items = new();

    private List<DockItem> BuildItems()
    {
        var result = new List<DockItem>();

        // 1) 固定启动项（支持拖入 exe/快捷方式/文件夹）。
        foreach (var pin in _config.DockPinnedItems)
        {
            if (string.IsNullOrWhiteSpace(pin))
                continue;
            result.Add(new DockItem(pin, _icons.Get(pin)));
        }

        // 2) 运行窗口：exe 命中的并入固定项；未命中的追加为纯运行项。
        foreach (var t in _monitor.Items)
        {
            DockItem? match = null;
            if (!string.IsNullOrEmpty(t.ProcessPath))
                match = result.FirstOrDefault(it =>
                    it.PinPath is not null && string.Equals(it.PinPath, t.ProcessPath, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                match.Running = t;
                match.Icon ??= t.ProcessPath is null ? null : _icons.Get(t.ProcessPath);
                continue;
            }
            result.Add(new DockItem(t, t.ProcessPath is null ? null : _icons.Get(t.ProcessPath)));
        }

        // 3) 按上次持久化的顺序重排。
        if (_config.DockOrder is { Count: > 0 })
        {
            var order = _config.DockOrder;
            result.Sort((a, b) =>
            {
                int ia = order.IndexOf(a.OrderKey);
                int ib = order.IndexOf(b.OrderKey);
                ia = ia < 0 ? int.MaxValue : ia;
                ib = ib < 0 ? int.MaxValue : ib;
                int cmp = ia.CompareTo(ib);
                return cmp == 0 ? string.Compare(a.OrderKey, b.OrderKey, StringComparison.OrdinalIgnoreCase) : cmp;
            });
        }

        return result;
    }

    /// <summary>按 ViewKey 增量同步 _views 与 _items，尽量复用旧视图以保留放大/焦点状态。</summary>
    private void SyncViews()
    {
        var reused = new HashSet<DockIconView>();
        var keyToItem = new Dictionary<string, DockItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in _items)
            keyToItem[ViewKey(item)] = item;

        var newViews = new List<DockIconView>();
        _map.Clear();
        foreach (var item in _items)
        {
            string key = ViewKey(item);
            var view = _views.FirstOrDefault(v => ViewKeyOf(v) == key && reused.Add(v));
            if (view is null)
            {
                view = CreateView(item);
                _root.Children.Add(view);
                view.Width = _config.DockIconSize;
                view.Height = _config.DockIconSize;
            }
            reused.Add(view);
            newViews.Add(view);
            _map[view] = item;

            // 运行状态变化时更新图标：纯运行项随 Running 改变指向真实图标。
            if (item.Icon is not null)
                view.Icon = item.Icon;
        }

        // 移除未被复用的旧视图。
        foreach (var v in _views)
        {
            if (!reused.Contains(v))
                _root.Children.Remove(v);
        }

        _views.Clear();
        _views.AddRange(newViews);
    }

    private string ViewKeyOf(DockIconView v) =>
        _viewKey.TryGetValue(v, out var k) ? k : string.Empty;

    private readonly Dictionary<DockIconView, string> _viewKey = new();

    private DockIconView CreateView(DockItem item)
    {
        var icon = item.Icon ?? _icons.Get(item.OrderKey);
        var view = new DockIconView(item.Title, Accent(item.OrderKey), icon);
        view.Clicked += OnIconClicked;
        view.RightTapped += OnIconRightTapped;
        view.ReorderRequested += OnReorderRequested;
        _viewKey[view] = ViewKey(item);
        return view;
    }

    // ================= 真实图标属性 =================

    private static Color Accent(string seed)
    {
        uint h = 2166136261u;
        foreach (char c in seed)
        {
            h ^= c;
            h *= 16777619;
        }
        var hsl = new HslState(h % 360, 0.55, 0.42);
        var (r, g, b) = hsl.ToRgb();
        return Windows.UI.Color.FromArgb(255, r, g, b);
    }

    private readonly record struct HslState(double H, double S, double L)
    {
        public (byte r, byte g, byte b) ToRgb()
        {
            double c = (1 - Math.Abs(2 * L - 1)) * S;
            double x = c * (1 - Math.Abs((H / 60) % 2 - 1));
            double m = L - c / 2;
            (double r, double g, double b) = H switch
            {
                < 60 => (c, x, 0d),
                < 120 => (x, c, 0d),
                < 180 => (0d, c, x),
                < 240 => (0d, x, c),
                < 300 => (x, 0d, c),
                _ => (c, 0d, x),
            };
            return ((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
        }
    }

    // ================= 点击：启动 / 恢复 / 最小化 =================

    private void OnIconClicked(object sender, DockIconView view)
    {
        if (!_map.TryGetValue(view, out var item))
            return;

        if (item.Running is not null)
        {
            if (item.Running.IsForeground)
                NativeMethods.ShowWindow(item.Hwnd, SW_MINIMIZE);
            else
            {
                NativeMethods.ShowWindow(item.Hwnd, NativeMethods.SW_RESTORE);
                NativeMethods.SetForegroundWindow(item.Hwnd);
            }
        }
        else if (item.PinPath is not null)
        {
            TryLaunch(item.PinPath);
        }
    }

    private static void TryLaunch(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
            // 启动失败静默，避免崩溃。
        }
    }

    // ================= 拖拽换序 =================

    private void OnReorderRequested(DockIconView view, double dx)
    {
        if (_views.Count < 2)
            return;
        int i = _views.IndexOf(view);
        if (i < 0)
            return;
        double unit = Math.Max(1, _config.DockIconSize + IconGap);
        int j = Math.Clamp(i + (int)Math.Round(dx / unit), 0, _views.Count - 1);
        if (j == i)
            return;

        var item = _items[i];
        var v = _views[i];
        _items.RemoveAt(i);
        _views.RemoveAt(i);
        _items.Insert(j, item);
        _views.Insert(j, v);

        PersistOrder();
        ReLayout(double.NegativeInfinity);
    }

    private void PersistOrder()
    {
        _config.DockOrder = _items
            .Select(it => it.OrderKey)
            .Where(k => !string.IsNullOrEmpty(k))
            .ToList();
        ConfigStore.Save(_config);
    }

    // ================= 外部拖入：把 exe/快捷方式/文件夹 加入 Dock =================

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "添加到 Dock";
        }
        else
        {
            e.AcceptedOperation = DataPackageOperation.None;
        }
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            return;
        var items = await e.DataView.GetStorageItemsAsync();
        bool changed = false;
        foreach (var it in items)
        {
            var p = it.Path;
            if (string.IsNullOrWhiteSpace(p))
                continue;
            if (_config.DockPinnedItems.Contains(p, StringComparer.OrdinalIgnoreCase))
                continue;
            _config.DockPinnedItems.Add(p);
            changed = true;
        }
        if (changed)
        {
            ConfigStore.Save(_config);
            OnTasksChanged();
        }
    }

    // ================= 悬停 / 预览 =================

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
        if (!_map.TryGetValue(view, out var item))
        {
            HideTooltip();
            return;
        }
        _tooltipText.Text = item.Title;
        _tooltip.Visibility = Visibility.Visible;
        _tooltip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double w = _tooltip.DesiredSize.Width;
        double h = _tooltip.DesiredSize.Height;
        double width = Math.Max(1, _root.ActualWidth > 0 ? _root.ActualWidth : _dockWidth);
        double left = Math.Clamp(x1 + (x2 - x1) / 2 - w / 2, 4, Math.Max(4, width - w - 4));
        double top2 = Math.Max(2, top - h - 4);
        Canvas.SetLeft(_tooltip, left);
        Canvas.SetTop(_tooltip, top2);

        // 悬停出 DWM 实时预览（最小化窗口无内容预览，跳过；纯固定项无窗口也不预览）。
        var running = item.Running;
        if (running is not null && !running.IsIconic && _visible && _hwnd != nint.Zero)
        {
            NativeMethods.GetWindowRect(_hwnd, out var wr);
            double cxScreen = wr.Left + (x1 + x2) / 2;
            _preview.ShowFor(running, wr.Bottom, cxScreen);
        }
    }

    private void HideTooltip()
    {
        _tooltip.Visibility = Visibility.Collapsed;
        _preview.Hide();
    }

    // ---- 右键菜单 ----
    private void OnIconRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var view = sender as DockIconView;
        if (view is null || !_map.TryGetValue(view, out var item))
            return;

        var menu = new MenuFlyout();
        if (item.Running is not null)
        {
            var toggle = new MenuFlyoutItem { Text = item.Running.IsForeground ? "最小化窗口" : "恢复并前置" };
            toggle.Click += (_, _) => OnIconClicked(view, view);
            menu.Items.Add(toggle);
        }
        else if (item.PinPath is not null)
        {
            var open = new MenuFlyoutItem { Text = "启动" };
            open.Click += (_, _) => OnIconClicked(view, view);
            menu.Items.Add(open);
        }

        var remove = new MenuFlyoutItem { Text = "从 Dock 移除" };
        remove.Click += (_, _) => RemoveIcon(view);
        menu.Items.Add(remove);
        menu.ShowAt(view);
    }

    private void RemoveIcon(DockIconView view)
    {
        // 纯运行窗口直接隐藏；固定项从配置移除。
        if (_map.TryGetValue(view, out var item) && item.PinPath is not null)
        {
            _config.DockPinnedItems.RemoveAll(p => string.Equals(p, item.PinPath, StringComparison.OrdinalIgnoreCase));
            ConfigStore.Save(_config);
        }

        _root.Children.Remove(view);
        _views.Remove(view);
        _map.Remove(view);
        _viewKey.Remove(view);
        HideTooltip();
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

        var sizes = new double[n];
        for (int i = 0; i < n; i++)
        {
            double center = leftSlot + usableW / 2 + (i - (n - 1) / 2d) * unit;
            double d = Math.Abs(center - pointerX);
            double t = Math.Clamp(1 - d / radius, 0, 1);
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
            view.SetActive(_items[i].Running is { } r && r.IsForeground);
            Canvas.SetLeft(view, x);
            Canvas.SetTop(view, centerY - sizes[i] / 2);
            _placed.Add((view, x, x + sizes[i], centerY - sizes[i] / 2));
            x += sizes[i] + IconGap;
        }
    }

    // ---- M6：最小化 genie 收纳动画 ----
    private void PlayGenie(TaskWindow task)
    {
        var view = _views.FirstOrDefault(v => _map[v].Running == task);
        if (view is null)
            return;

        var snap = WindowSnapshot.Capture(task.Hwnd);
        if (snap is null)
            return;

        var slot = _placed.FirstOrDefault(p => p.view == view);
        double cw = _root.ActualWidth > 0 ? _root.ActualWidth : _dockWidth;
        double ch = _root.ActualHeight > 0 ? _root.ActualHeight : DockHeight;
        double cx = slot.view is not null ? (slot.x1 + slot.x2) / 2 : cw / 2;
        double cy = ch / 2;

        double bw = Math.Max(16, view.Width);
        double bh = Math.Max(16, view.Height);

        var overlay = new Border
        {
            Child = new Image { Source = snap, Stretch = Stretch.Fill },
            Width = bw,
            Height = bh,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform { ScaleX = 1, ScaleY = 1 },
        };
        Canvas.SetZIndex(overlay, 10000);
        Canvas.SetLeft(overlay, cx - bw / 2);
        Canvas.SetTop(overlay, cy - bh / 2);
        _root.Children.Add(overlay);

        var t = TimeSpan.FromMilliseconds(350);
        var scale = (ScaleTransform)overlay.RenderTransform;
        var sb = new Storyboard();
        sb.Children.Add(MakeAnim(nameof(scale.ScaleX), scale, 1, 0.1, t));
        sb.Children.Add(MakeAnim(nameof(scale.ScaleY), scale, 1, 0.1, t));
        sb.Children.Add(MakeAnim(nameof(overlay.Opacity), overlay, 1, 0, t));
        sb.Completed += (_, _) => _root.Children.Remove(overlay);
        sb.Begin();
    }

    private static DoubleAnimation MakeAnim(string path, DependencyObject target, double from, double to, TimeSpan duration)
    {
        var a = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = duration,
            EnableDependentAnimation = true,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        };
        Storyboard.SetTarget(a, target);
        Storyboard.SetTargetProperty(a, path);
        return a;
    }
}