using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Dispatching;
using Windows.UI;
using WinMac.Core.Services;
using WinMac.Core.Win32;

namespace WinMac.Shell.Widgets;

public enum WidgetKind { Clock, Cpu, Mem, Calendar, Stock }

/// <summary>
/// 桌面小组件窗口：无边框、可拖拽、深色半透明卡片。
/// 用户级小组件（时钟/CPU/内存/日历）；CPU/内存由低频 <see cref="SystemSampler"/> 刷新。
/// 因 WinUI 窗口默认不透明，MVP 采用实心卡片（后续可接每像素 alpha 层叠）。
/// </summary>
public sealed class WidgetWindow : Window
{
    private const int GWL_STYLE = -16;
    private const long WS_CAPTION = 0x00C00000;
    private const long WS_THICKFRAME = 0x00040000;

    private readonly Border _root;
    private nint _hwnd;
    private bool _moving;
    private (int x, int y) _windowStart;
    private (int x, int y) _cursorStart;

    public WidgetWindow(WidgetKind kind)
    {
        var content = BuildContent(kind, out double w, out double h);

        _root = new Border
        {
            Width = w,
            Height = h,
            Background = new SolidColorBrush(Color.FromArgb(255, 34, 34, 38)),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(18, 16, 18, 16),
            Child = content,
        };
        Content = _root;
        Title = "WinMac " + kind;

        _root.PointerPressed += OnPressed;
        _root.PointerMoved += OnMoved;
        _root.PointerReleased += OnReleased;
        _root.PointerCaptureLost += OnReleased;
        Activated += OnActivated;
    }

    private void OnActivated(object? sender, WindowActivatedEventArgs e)
    {
        if (_hwnd == nint.Zero)
        {
            _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            long style = NativeMethods.GetWindowLongPtr(_hwnd, GWL_STYLE).ToInt64();
            style &= ~(WS_CAPTION | WS_THICKFRAME);
            NativeMethods.SetWindowLongPtr(_hwnd, GWL_STYLE, (nint)style);

            long ex = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            ex |= NativeMethods.WS_EX_TOOLWINDOW;
            NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, (nint)ex);
        }
        _root.Visibility = Visibility.Visible;
    }

    public void ShowAt(int x, int y)
    {
        Activate();
        if (_hwnd != nint.Zero)
            NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_NOTOPMOST, x, y, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    // ---- 拖拽 ----
    private void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(_root).Properties.IsLeftButtonPressed)
            return;
        _moving = true;
        _cursorStart = GetCursor();
        NativeMethods.GetWindowRect(_hwnd, out var r);
        _windowStart = (r.Left, r.Top);
        _root.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_moving)
            return;
        var c = GetCursor();
        int dx = c.x - _cursorStart.x;
        int dy = c.y - _cursorStart.y;
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_NOTOPMOST,
            _windowStart.x + dx, _windowStart.y + dy, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        e.Handled = true;
    }

    private void OnReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_moving)
            return;
        _moving = false;
        _root.ReleasePointerCapture(e.Pointer);
    }

    private static (int x, int y) GetCursor()
    {
        NativeMethods.GetCursorPos(out var p);
        return (p.X, p.Y);
    }

    // ---- 内容 ----
    private static FrameworkElement BuildContent(WidgetKind kind, out double width, out double height)
    {
        return kind switch
        {
            WidgetKind.Clock => BuildClock(out width, out height),
            WidgetKind.Cpu => BuildCpu(out width, out height),
            WidgetKind.Mem => BuildMem(out width, out height),
            WidgetKind.Calendar => BuildCalendar(out width, out height),
            WidgetKind.Stock => BuildStock(out width, out height),
            _ => (BuildClock(out width, out height)),
        };
    }

    private static FrameworkElement BuildClock(out double w, out double h)
    {
        w = 230; h = 150;
        var time = new TextBlock
        {
            Text = DateTime.Now.ToString("HH:mm"),
            FontSize = 56,
            FontWeight = new Windows.UI.Text.FontWeight { Weight = 600 },
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
        };
        var date = new TextBlock
        {
            Text = DateTime.Now.ToString("yyyy年M月d日 ddd"),
            FontSize = 16,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 182)),
        };
        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(time);
        stack.Children.Add(date);

        var timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(1);
        timer.Tick += (_, _) =>
        {
            time.Text = DateTime.Now.ToString("HH:mm");
            date.Text = DateTime.Now.ToString("yyyy年M月d日 ddd");
        };
        timer.Start();

        return stack;
    }

    private static FrameworkElement BuildCpu(out double w, out double h)
    {
        w = 230; h = 130;
        var percent = new TextBlock
        {
            FontSize = 42,
            FontWeight = new Windows.UI.Text.FontWeight { Weight = 600 },
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            Text = "0%",
        };
        var bar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 8, Width = 170 };
        var label = new TextBlock
        {
            Text = "CPU",
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 182)),
        };
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(label);
        stack.Children.Add(percent);
        stack.Children.Add(bar);

        var sampler = new SystemSampler();
        var timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(2);
        timer.Tick += (_, _) =>
        {
            var s = sampler.Sample();
            percent.Text = ((int)Math.Round(s.CpuPercent)).ToString() + "%";
            bar.Value = s.CpuPercent;
            _ = timer;
        };
        timer.Start();

        return stack;
    }

    private static FrameworkElement BuildMem(out double w, out double h)
    {
        w = 230; h = 130;
        var percent = new TextBlock
        {
            FontSize = 42,
            FontWeight = new Windows.UI.Text.FontWeight { Weight = 600 },
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            Text = "0%",
        };
        var detail = new TextBlock
        {
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 182)),
        };
        var bar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 8, Width = 170 };
        var label = new TextBlock { Text = "内存", FontSize = 14, Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 182)) };

        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(label);
        stack.Children.Add(percent);
        stack.Children.Add(detail);
        stack.Children.Add(bar);

        var sampler = new SystemSampler();
        var timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(2);
        timer.Tick += (_, _) =>
        {
            var s = sampler.Sample();
            percent.Text = ((int)Math.Round(s.UsedMemoryPercent)).ToString() + "%";
            detail.Text = $"{s.UsedMib} / {s.TotalMib} MB";
            bar.Value = s.UsedMemoryPercent;
            _ = timer;
        };
        timer.Start();

        return stack;
    }

    private static FrameworkElement BuildCalendar(out double w, out double h)
    {
        w = 260; h = 240;
        var now = DateTime.Now;

        var header = new TextBlock
        {
            Text = now.ToString("yyyy年M月"),
            FontSize = 16,
            FontWeight = new Windows.UI.Text.FontWeight { Weight = 600 },
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
        };

        var grid = new Grid { Margin = new Thickness(0, 8, 0, 0), ColumnSpacing = 4, RowSpacing = 4 };
        for (int c = 0; c < 7; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int rr = 0; rr <= 6; rr++)
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        string[] week = { "日", "一", "二", "三", "四", "五", "六" };
        for (int c = 0; c < 7; c++)
        {
            grid.Children.Add(new TextBlock
            {
                Text = week[c],
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 156)),
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            Grid.SetColumn((FrameworkElement)grid.Children[^1], c);
        }

        int firstDay = (int)new DateTime(now.Year, now.Month, 1).DayOfWeek;
        int days = DateTime.DaysInMonth(now.Year, now.Month);
        int row = 1;
        for (int day = 1; day <= days; day++)
        {
            int col = (firstDay + day - 1) % 7;
            if (col == 0 && day > 1)
            {
                row++;
            }
            var tb = new TextBlock
            {
                Text = day.ToString(),
                FontSize = 13,
                Foreground = day == now.Day
                    ? new SolidColorBrush(Microsoft.UI.Colors.White)
                    : new SolidColorBrush(Color.FromArgb(255, 200, 200, 204)),
                HorizontalAlignment = HorizontalAlignment.Center,
            };

            FrameworkElement cell = tb;
            if (day == now.Day)
            {
                cell = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 0, 122, 255)),
                    CornerRadius = new CornerRadius(9),
                    MinWidth = 22,
                    MinHeight = 22,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = tb,
                };
            }

            grid.Children.Add(cell);
            Grid.SetColumn(cell, (firstDay + day - 1) % 7);
            Grid.SetRow(cell, row);
        }

        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(grid);

        var root = new Grid();
        root.Children.Add(stack);
        return root;
    }

    private static FrameworkElement BuildStock(out double w, out double h)
    {
        w = 236; h = 214;
        var dq = DispatcherQueue.GetForCurrentThread();
        var svc = new StockQuoteService();

        var header = new TextBlock
        {
            Text = "股市",
            FontSize = 14,
            FontWeight = new Windows.UI.Text.FontWeight { Weight = 600 },
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
        };

        var panel = new StackPanel { Spacing = 2, Margin = new Thickness(0, 8, 0, 0) };
        // 每行保存价格/涨跌两个文本，刷新时按代码回填。
        var rows = new List<(TextBlock Price, TextBlock Change)>();

        foreach (var (code, name) in StockQuoteService.DefaultWatchlist)
        {
            (TextBlock Price, TextBlock Change) tb = MakeRow(name, code, panel);
            rows.Add(tb);
        }

        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(panel);

        // 每 10s 拉取一次实时行情；跨线程回填到 UI。
        var timer = dq.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(10);
        timer.Tick += async (_, _) =>
        {
            var quotes = await svc.GetQuotesAsync();
            dq.TryEnqueue(() => ApplyQuotes(rows, quotes));
        };
        timer.Start();

        var root = new Grid();
        root.Children.Add(stack);
        return root;
    }

    private static (TextBlock Price, TextBlock Change) MakeRow(string name, string code, StackPanel panel)
    {
        var nameTb = new TextBlock
        {
            Text = name,
            FontSize = 14,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var codeTb = new TextBlock
        {
            Text = code.ToUpperInvariant(),
            FontSize = 10.5,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 156)),
        };
        var left = new StackPanel { Spacing = 0 };
        left.Children.Add(nameTb);
        left.Children.Add(codeTb);

        var priceTb = new TextBlock
        {
            Text = "--",
            FontSize = 15,
            FontWeight = new Windows.UI.Text.FontWeight { Weight = 600 },
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var changeTb = new TextBlock { Text = "—", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right };
        var right = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(priceTb);
        right.Children.Add(changeTb);

        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(left);
        Grid.SetColumn(left, 0);
        row.Children.Add(right);
        Grid.SetColumn(right, 1);

        panel.Children.Add(row);
        return (priceTb, changeTb);
    }

    private static void ApplyQuotes(IReadOnlyList<(TextBlock Price, TextBlock Change)> rows,
        List<StockQuote> quotes)
    {
        var byCode = quotes.ToDictionary(q => q.Code);
        int n = Math.Min(rows.Count, StockQuoteService.DefaultWatchlist.Count);
        for (int i = 0; i < n; i++)
        {
            var (priceTb, changeTb) = rows[i];
            string code = StockQuoteService.DefaultWatchlist[i].Code;
            if (!byCode.TryGetValue(code, out var q))
                continue;

            priceTb.Text = q.Price.ToString("F2");

            string sign = q.Change > 0 ? "+" : (q.Change < 0 ? "-" : "");
            changeTb.Text = $"{sign}{Math.Abs(q.Change):F2}  {sign}{Math.Abs(q.ChangePct):F2}%";
            changeTb.Foreground = new SolidColorBrush(q.Change switch
            {
                > 0 => Color.FromArgb(255, 224, 49, 51),   // 红涨
                < 0 => Color.FromArgb(255, 0, 168, 84),    // 绿跌
                _ => Color.FromArgb(255, 190, 190, 196),   // 平
            });
        }
    }