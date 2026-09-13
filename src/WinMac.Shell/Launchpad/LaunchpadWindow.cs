using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI;
using Windows.UI.Text;
using WinMac.Core.Services;
using WinMac.Core.Win32;

namespace WinMac.Shell.Launchpad;

/// <summary>
/// 全屏启动台：半透明遮罩 + 顶部搜索框 + 应用网格。
/// 纯代码构造；点击图标即用 ShellExecute 启动其快捷方式。
/// </summary>
public sealed class LaunchpadWindow : Window
{
    private const int GWL_STYLE = -16;
    private const long WS_CAPTION = 0x00C00000;
    private const long WS_THICKFRAME = 0x00040000;
    private const int SW_SHOW = 5;
    private const int SW_HIDE = 0;

    private readonly List<AppEntry> _all;
    private readonly TextBox _search;
    private readonly StackPanel _rows;
    private readonly Grid _root;

    private nint _hwnd;

    public LaunchpadWindow()
    {
        _all = AppRegistryService.EnumerateApps().ToList();

        _search = new TextBox
        {
            PlaceholderText = "搜索应用…",
            FontSize = 16,
            Width = 420,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _search.TextChanged += (_, _) => Rebuild();

        _rows = new StackPanel { Spacing = 16, HorizontalAlignment = HorizontalAlignment.Center };

        var scroller = new ScrollViewer
        {
            Content = _rows,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        var header = new StackPanel { Spacing = 12, Margin = new Thickness(0, 48, 0, 28) };
        header.Children.Add(new TextBlock
        {
            Text = "启动台",
            FontSize = 36,
            FontWeight = new Windows.UI.Text.FontWeight { Weight = 600 },
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        header.Children.Add(_search);

        _root = new Grid { Background = new SolidColorBrush(Color.FromArgb(235, 16, 16, 20)) };
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(header, 0);
        Grid.SetRow(scroller, 1);
        _root.Children.Add(header);
        _root.Children.Add(scroller);
        Content = _root;

        _root.KeyDown += OnRootKeyDown;
        Activated += OnActivated;

        Rebuild();
    }

    public void ShowOverlay()
    {
        if (_hwnd == nint.Zero)
            Activate(); // 触发 Activated → 样式/定位
        else
        {
            StyleAndPlace();
            NativeMethods.ShowWindow(_hwnd, SW_SHOW);
            Activate();
        }
        _search.Text = string.Empty;
        Rebuild();
    }

    public void HideOverlay()
    {
        if (_hwnd != nint.Zero)
            NativeMethods.ShowWindow(_hwnd, SW_HIDE);
    }

    private void OnActivated(object? sender, WindowActivatedEventArgs e)
    {
        if (_hwnd == nint.Zero)
            StyleAndPlace();
    }

    private void StyleAndPlace()
    {
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        long style = NativeMethods.GetWindowLongPtr(_hwnd, GWL_STYLE).ToInt64();
        style &= ~(WS_CAPTION | WS_THICKFRAME);
        NativeMethods.SetWindowLongPtr(_hwnd, GWL_STYLE, (nint)style);

        long ex = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        ex |= NativeMethods.WS_EX_TOOLWINDOW;
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, (nint)ex);

        var area = DisplayArea.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(_hwnd), DisplayAreaFallback.Primary);
        var wa = area.WorkArea;
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST,
            wa.X, wa.Y, wa.Width, wa.Height, NativeMethods.SWP_NOACTIVATE);
    }

    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
            HideOverlay();
    }

    private void Rebuild()
    {
        _rows.Children.Clear();
        string q = _search.Text?.Trim() ?? string.Empty;

        var filtered = string.IsNullOrEmpty(q)
            ? _all
            : _all.Where(a => a.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();

        if (filtered.Count == 0)
        {
            _rows.Children.Add(new TextBlock
            {
                Text = "没有匹配的应用",
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 48, 0, 0),
            });
            return;
        }

        var row = new StackPanel { Spacing = 12, Orientation = Orientation.Horizontal };
        foreach (var entry in filtered)
        {
            row.Children.Add(BuildTile(entry));
            if (row.Children.Count >= 8)
            {
                _rows.Children.Add(row);
                row = new StackPanel { Spacing = 12, Orientation = Orientation.Horizontal };
            }
        }
        if (row.Children.Count > 0)
            _rows.Children.Add(row);
    }

    private Button BuildTile(AppEntry entry)
    {
        var name = new TextBlock
        {
            Text = entry.Name,
            FontSize = 12,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
            MaxWidth = 96,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var iconBox = new Border
        {
            Width = 56,
            Height = 56,
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(MicroColor(entry.Name)),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new TextBlock
            {
                Text = entry.Name.Length > 0 ? entry.Name[0].ToString().ToUpperInvariant() : "?",
                FontSize = 22,
                FontWeight = new Windows.UI.Text.FontWeight { Weight = 600 },
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        var content = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Stretch };
        content.Children.Add(iconBox);
        content.Children.Add(name);

        var tile = new Button
        {
            Width = 108,
            Height = 116,
            Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(16),
            Content = content,
        };
        tile.Click += (_, _) =>
        {
            entry.Launch();
            HideOverlay();
        };
        return tile;
    }

    private static Color MicroColor(string name)
    {
        uint h = 2166136261u;
        foreach (char c in name)
        {
            h ^= c;
            h *= 16777619;
        }
        // 稳定色相，中等饱和/亮度，白色字母可读。
        double hue = h % 360;
        double sat = 0.55;
        double x = sat * (1 - Math.Abs((hue / 60) % 2 - 1));
        double r, g, b;
        var rgb = hue switch
        {
            < 60 => (sat, x, 0d),
            < 120 => (x, sat, 0d),
            < 180 => (0d, sat, x),
            < 240 => (0d, x, sat),
            < 300 => (x, 0d, sat),
            _ => (sat, 0d, x),
        };
        (r, g, b) = rgb;
        double m = 0.18;
        return Color.FromArgb(255,
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }
}