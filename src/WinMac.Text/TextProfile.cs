using WinMac.Core.Configuration;

namespace WinMac.Text;

/// <summary>
/// 从 <see cref="AppConfig"/> 的三个文字参数(Gamma/Contrast/Softness)推导出的、可被 UI 消费的预设入口。
/// 本层刻意保持纯 .NET（不依赖 WinUI），便于单元测试与未来原生注入器复用曲线逻辑。
/// </summary>
public static class TextProfile
{
    /// <summary>拿一份带合理夹取范围的参数，出 UI 可用的样式。</summary>
    public static MacTextStyle Resolve(AppConfig config)
    {
        double gamma = Clamp(config.TextGamma, 1.0, 3.0);
        double contrast = Clamp(config.TextContrast, 0.0, 1.5);
        double softness = Clamp(config.TextSoftness, 0.0, 1.0);
        return MacTextRenderer.Compute(gamma, contrast, softness);
    }

    private static double Clamp(double v, double min, double max) => v < min ? min : (v > max ? max : v);
}