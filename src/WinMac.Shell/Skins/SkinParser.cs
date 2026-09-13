using System.Text.Json;
using System.Text.Json.Serialization;
using WinMac.Core.Configuration;

namespace WinMac.Shell.Skins;

/// <summary>
/// Rainmeter 式皮肤 JSON 解析器：把形如
/// <c>{"components":[{"kind":"Clock","monitor":0,"x":40,"y":40,"w":230,"h":150}]}</c> 的串
/// 解析为 <see cref="SkinLayoutEntry"/> 列表；解析失败或无数据时回退内置默认布局。
/// </summary>
public static class SkinParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed class SkinFile
    {
        public List<SkinLayoutEntry>? Components { get; set; }
    }

    /// <summary>解析皮肤 JSON 字符串；返回 null 表示解析失败（调用方可用默认布局兜底）。</summary>
    public static List<SkinLayoutEntry>? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            var file = JsonSerializer.Deserialize<SkinFile>(json, Options);
            if (file?.Components is not { Count: > 0 })
                return null;
            return file.Components;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>内置默认皮肤：主屏右上角放时钟/CPU/内存各一。</summary>
    public static List<SkinLayoutEntry> Defaults()
    {
        return new List<SkinLayoutEntry>
        {
            new() { Kind = "Clock", Monitor = 0, X = 40, Y = 40, W = 230, H = 150 },
            new() { Kind = "Cpu", Monitor = 0, X = 40, Y = 200, W = 230, H = 110 },
            new() { Kind = "Mem", Monitor = 0, X = 40, Y = 320, W = 230, H = 110 },
        };
    }
}