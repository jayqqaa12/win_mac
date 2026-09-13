namespace WinMac.Text;

/// <summary>
/// 目标：把 macOS 观感的三参数曲线(Gamma/Contrast/Softness)映射为 XAML 控件能真正设置的项。
/// 说明（重要边界）：WinUI 的 DirectWrite 文本在光栅化阶段即已定色，无法在字形级别重算灰度 gamma；
/// 这里将三参数映射到「字重 + 前景向次级色混合 + 透明度 / 柔化模糊」三个可控维度，形成可观察、可调的
/// “mac 质感近似”。真·逐字形灰度 AA + gamma 属 M5 原生注入器范畴，不在 M1 文本引擎内。
/// </summary>
public sealed record MacTextStyle
{
    /// <summary>默认字重（semibold 起点）。对比度越高越实。</summary>
    public int FontWeight { get; init; } = 500;

    /// <summary>前景色向次级色的混合比例(0~1)。对比度越低、gamma 越高 → 越浅。</summary>
    public double ForegroundBlend { get; init; }

    /// <summary>整体透明度(0~1)。柔化度越高 → 略微变轻。</summary>
    public double Opacity { get; init; } = 1.0;

    /// <summary>柔化模糊强度(逻辑像素，≤2)。柔化度越高越大。</summary>
    public double SoftnessBlur { get; init; }
}

/// <summary>纯函数：由三参数计算 <see cref="MacTextStyle"/>。</summary>
public static class MacTextRenderer
{
    public static MacTextStyle Compute(double gamma, double contrast, double softness)
    {
        // 对比度 → 字重：0.0≈400, 1.5≈650。
        int weight = (int)Math.Round(400 + contrast * 166.67);
        weight = Math.Clamp(weight, 400, 650);

        // gamma 越高 = 越亮越淡 → 前景向次级色(灰)混合比例增大。
        // 对比度越低也会变淡。合成一个 0~1 的混合率。
        double blend = (gamma - 1.0) / 2.0;          // gamma 1.0→0, 3.0→1
        blend = blend * 0.7 + (1.0 - Math.Clamp(contrast / 1.5, 0, 1)) * 0.3;
        blend = Math.Clamp(blend, 0.0, 0.45);        // 最多混 45% 灰，不至于过浅

        // 柔化 → 透明度略微下降 + 轻微模糊。
        double opacity = 1.0 - 0.12 * softness;
        double blur = softness * 2.0;                // ≤2 px

        return new MacTextStyle
        {
            FontWeight = weight,
            ForegroundBlend = blend,
            Opacity = opacity,
            SoftnessBlur = blur,
        };
    }
}