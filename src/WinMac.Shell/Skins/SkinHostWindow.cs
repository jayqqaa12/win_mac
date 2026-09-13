using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;
using WinMac.Core.Configuration;
using WinMac.Core.Services;
using WinMac.Core.Win32;

namespace WinMac.Shell.Skins;

/// <summary>
/// 一块显示器上的皮肤宿主窗口：透明全工作区画布，承载多个可拖拽/缩放/持久化的皮肤卡片
/// （时钟/CPU/内存）。拖动改位置、右下角区域拖动改尺寸，改动写回
/// <see cref="AppConfig.SkinLayouts"/> 并触发持久化。
/// </summary>
public sealed class SkinHostWindow : Window
{
    private const int GWL_STYLE = -16;
    private const long WS_CAPTION = 0x00C00000;
    private const long WS_THICKFRAME = 0x00040000;
    private const double ResizeZone = 24; // 卡片右下角触发放大的方形区(px)。

    private readonly AppConfig _config;
    private readonly int _monitorIndex;
    private readonly Func<List<SkinLayoutEntry>> _entries;
    private readonly Action _persist;
    private readonly Canvas _root;
    private nint _hwnd;
    private int _originX;
    private int _originY;
    private readonly List<Card> _cards = new();

    private sealed class Card
    {
        public required SkinLayoutEntry Entry;
        public required Border Border;
        public bool Resizing;
        public double PressX;
        public double PressY;
        public double StartLeft;
        public double StartTop;
        public double StartW;
        public double StartH;
    }

    public SkinHostWindow(AppConfig config, int monitorIndex, SkinMonitor monitor,
        Func<List<SkinLayoutEntry>> entries, Action persist)
    {
        _config = config;
        _monitorIndex = monitorIndex;
        _entries = entries;
        _persist = persist;
        _originX = monitor.Left;
        _originY = monitor.Top;

        _root = new Canvas
        {
            Width = monitor.Width,
            Height = monitor.Height,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };
        Content = new Grid
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Children = { _root },
        };

        foreach (var entry in _entries())
        {
            if (entry.Monitor != _monitorIndex)
                continue;
            var card = BuildCard(entry);
            _cards.Add(card);
            _root.Children.Add(card.Border);
        }

        Activated += OnActivated;
    }

    private void OnActivated(object? sender, WindowActivatedEventArgs e)
    {
        if (_hwnd != nint.Zero)
            return;
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        long style = NativeMethods.GetWindowLongPtr(_hwnd, GWL_STYLE).ToInt64();
        style &= ~(WS_CAPTION | WS_THICKFRAME);
        NativeMethods.SetWindowLongPtr(_hwnd, GWL_STYLE, (nint)style);

        long ex = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        ex |= NativeMethods.WS_EX_TOOLWINDOW;
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, (nint)ex);

        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_NOTOPMOST,
            _originX, _originY, (int)_root.Width, (int)_root.Height,
            NativeMethods.SWP_NOACTIVATE);
    }

    // ---- 卡片 ----
    private Card BuildCard(SkinLayoutEntry entry)
    {
        var contentGrid = new Grid();
        contentGrid.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 34, 34, 38)),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(16, 14, 16, 14),
            Child = BuildContent(entry.Kind),
        });
        var border = new Border
        {
            Width = entry.W,
            Height = entry.H,
            Child = contentGrid,
        };
        Canvas.SetLeft(border, entry.X);
        Canvas.SetTop(border, entry.Y);

        var card = new Card { Entry = entry, Border = border };
        border.PointerPressed += (_, e) => OnPressed(card, e);
        border.PointerMoved += (_, e) => OnMoved(card, e);
        border.PointerReleased += (_, _) => OnReleased(card);
        border.PointerCaptureLost += (_, _) => OnReleased(card);
        return card;
    }

    private void OnPressed(Card card, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(card.Border).Properties.IsLeftButtonPressed)
            return;
        var p = e.GetCurrentPoint(card.Border).Position;
        bool nearResize = p.X >= card.Border.Width - ResizeZone && p.Y >= card.Border.Height - ResizeZone;
        card.Resizing = nearResize;
        card.PressX = p.X;
        card.PressY = p.Y;
        card.StartLeft = Canvas.GetLeft(card.Border);
        card.StartTop = Canvas.GetTop(card.Border);
        card.StartW = card.Border.Width;
        card.StartH = card.Border.Height;
        card.Border.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnMoved(Card card, PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(card.Border).Position;
        double dx = p.X - card.PressX;
        double dy = p.Y - card.PressY;

        if (card.Resizing)
        {
            card.Border.Width = Math.Max(120, card.StartW + dx);
            card.Border.Height = Math.Max(60, card.StartH + dy);
            card.Entry.W = card.Border.Width;
            card.Entry.H = card.Border.Height;
        }
        else
        {
            double x = Math.Clamp(card.StartLeft + dx, 0, Math.Max(0, _root.Width - card.Border.Width));
            double y = Math.Clamp(card.StartTop + dy, 0, Math.Max(0, _root.Height - card.Border.Height));
            Canvas.SetLeft(card.Border, x);
            Canvas.SetTop(card.Border, y);
            card.Entry.X = x;
            card.Entry.Y = y;
        }
        _persist();
        e.Handled = true;
    }

    private static void OnReleased(Card card)
    {
        card.Resizing = false;
    }

    // ---- 内容 ----
    private static FrameworkElement BuildContent(string kind) =>
        kind.ToUpperInvariant() switch
        {
            "CPU" => BuildSensor("CPU", s => ((int)Math.Round(s.CpuPercent)).ToString() + "%", s => s.CpuPercent),
            "MEM" => BuildSensor("内存", s => ((int)Math.Round(s.UsedMemoryPercent)).ToString() + "%", s => s.UsedMemoryPercent),
            _ => BuildClock(),
        };

    private static FrameworkElement BuildClock()
    {
        var time = new TextBlock
        {
            FontSize = 44,
            FontWeight = new Windows.UI.Text.FontWeight { Weight = 600 },
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            Text = DateTime.Now.ToString("HH:mm"),
        };
        var date = new TextBlock
        {
            FontSize = 15,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 182)),
            Text = DateTime.Now.ToString("yyyy年M月d日 ddd"),
        };
        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(time);
        stack.Children.Add(date);
        StartTimer(TimeSpan.FromSeconds(1), () =>
        {
            time.Text = DateTime.Now.ToString("HH:mm");
            date.Text = DateTime.Now.ToString("yyyy年M月d日 ddd");
        });
        return stack;
    }

    private static FrameworkElement BuildSensor(string label, Func<SystemSample, string> textOf, Func<SystemSample, double> valueOf)
    {
        var percent = new TextBlock
        {
            FontSize = 32,
            FontWeight = new Windows.UI.Text.FontWeight { Weight = 600 },
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            Text = "0%",
        };
        var bar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 7, Width = 150 };
        var lbl = new TextBlock { Text = label, FontSize = 13, Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 182)) };
        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(lbl);
        stack.Children.Add(percent);
        stack.Children.Add(bar);
        var sampler = new SystemSampler();
        StartTimer(TimeSpan.FromSeconds(2), () =>
        {
            var s = sampler.Sample();
            percent.Text = textOf(s);
            bar.Value = valueOf(s);
        });
        return stack;
    }

    private static void StartTimer(TimeSpan interval, Action tick)
    {
        var t = DispatcherQueue.GetForCurrentThread().CreateTimer();
        t.Interval = interval;
        t.Tick += (_, _) => tick();
        t.Start();
    }
}