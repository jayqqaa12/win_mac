using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinMac.Core.Configuration;

/// <summary>WinMac 用户可见配置。不随模块增删而膨胀，新增功能时在此加默认字段。</summary>
public sealed class AppConfig
{
    /// <summary>Dock 是否启用。</summary>
    public bool DockEnabled { get; set; } = true;

    /// <summary>图标基大小（逻辑像素）。</summary>
    public double DockIconSize { get; set; } = 56;

    /// <summary>放大镜触摸到的最大图标尺寸。</summary>
    public double DockMaxIconSize { get; set; } = 96;

    /// <summary>放大镜影响半径（逻辑像素）。</summary>
    public double MagnifyRadius { get; set; } = 160;

    /// <summary>隐藏模式：Always / AutoHide / SmartHide。</summary>
    public string DockHideMode { get; set; } = "Always";

    /// <summary>浅色 / 深色 / 跟随系统。</summary>
    public string Theme { get; set; } = "System";

    /// <summary>CPU/内存采样间隔（秒），0 关闭。</summary>
    public int SensorIntervalSeconds { get; set; } = 2;

    /// <summary>开机自启。</summary>
    public bool AutoStart { get; set; } = true;
}

/// <summary>将 <see cref="AppConfig"/> 持久化为 JSON。</summary>
public static class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    /// <summary>配置目录：%LocalAppData%\WinMac。</summary>
    public static string ConfigDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WinMac");

    private static string ConfigFile => Path.Combine(ConfigDir, "config.json");

    public static AppConfig Load()
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            if (File.Exists(ConfigFile))
                return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigFile), Options)
                       ?? new AppConfig();
        }
        catch
        {
            // 配置损坏时回退默认，不崩溃。
        }
        return new AppConfig();
    }

    public static void Save(AppConfig config)
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            File.WriteAllText(ConfigFile, JsonSerializer.Serialize(config, Options));
        }
        catch
        {
            // 写失败不致命。
        }
    }
}