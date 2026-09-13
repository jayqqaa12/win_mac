using System.Diagnostics;

namespace WinMac.Core.Services;

/// <summary>启动台/搜索用的一条已安装应用。</summary>
public sealed record AppEntry(string Name, string ShortcutPath)
{
    public void Launch()
    {
        // 以 ShellExecute 打开 .lnk 会按其目标启动；请求时再显式传播，避免在 Core 里触发额外引用。
        Process.Start(new ProcessStartInfo { FileName = ShortcutPath, UseShellExecute = true });
    }
}

/// <summary>
/// 枚举“开始菜单 → 程序”下的快捷方式作为已安装应用清单（用户级 + 全局级），按名去重排序。
/// MVP 不做 .lnk 目标解析/图标提取，名称即文件名；后续可接 IShellLink 换真实目标与图标。
/// </summary>
public static class AppRegistryService
{
    public static IReadOnlyList<AppEntry> EnumerateApps()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<AppEntry>();

        foreach (string root in StartMenuRoots())
        {
            if (!Directory.Exists(root))
                continue;
            try
            {
                foreach (string lnk in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
                {
                    string name = Path.GetFileNameWithoutExtension(lnk);
                    if (string.IsNullOrWhiteSpace(name) || set.Contains(name))
                        continue;
                    set.Add(name);
                    entries.Add(new AppEntry(name, lnk));
                }
            }
            catch
            {
                // 个别快捷方式不可读不影响整体。
            }
        }

        entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return entries;
    }

    private static IEnumerable<string> StartMenuRoots()
    {
        // 用户级
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Windows\Start Menu\Programs");
        // 全局级
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            @"Microsoft\Windows\Start Menu\Programs");
    }
}