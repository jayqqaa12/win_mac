namespace WinMac.Text;

/// <summary>
/// macOS 风格字渲染参数（初版：灰度 AA + gamma/contrast/softness 曲线）。
/// 由 GPU/Visual 合成层的 ShaderEffect 消费，避免 CPU 逐像素开销。
/// </summary>
public sealed record MacTextRenderingParams
{
    /// <summary>gamma 曲线。macOS 观感偏高，默认 ~1.8。</summary>
    public float Gamma { get; init; } = 1.8f;

    /// <summary>对比度。较低→更柔和的灰度过渡。</summary>
    public float Contrast { get; init; } = 1.0f;

    /// <summary>柔化半径（像素级）。轻微提升边缘平滑，接近 Core Text。</summary>
    public float Softness { get; init; } = 0.0f;

    /// <summary>是否关闭 ClearType 亚像素彩边，改用灰度 AA。</summary>
    public bool UseGrayscaleAntialiasing { get; init; } = true;

    /// <summary>深色背景下对文本亮度的微调。</summary>
    public float DarkModeLuminanceLift { get; init; } = 0.03f;

    public static MacTextRenderingParams Default => new();

    public MacTextRenderingParams With(Action<Builder> configure)
    {
        var b = new Builder(this);
        configure(b);
        return b.ToRecord();
    }

    public sealed class Builder
    {
        private readonly MacTextRenderingParams _src;
        private float _gamma, _contrast, _softness, _darkLift;
        private bool _grayscale;

        public Builder(MacTextRenderingParams src)
        {
            _src = src;
            _gamma = src.Gamma;
            _contrast = src.Contrast;
            _softness = src.Softness;
            _grayscale = src.UseGrayscaleAntialiasing;
            _darkLift = src.DarkModeLuminanceLift;
        }

        public Builder Gamma(float v) { _gamma = v; return this; }
        public Builder Contrast(float v) { _contrast = v; return this; }
        public Builder Softness(float v) { _softness = v; return this; }
        public Builder Grayscale(bool v) { _grayscale = v; return this; }
        public Builder DarkModeLuminanceLift(float v) { _darkLift = v; return this; }

        public MacTextRenderingParams ToRecord() => new()
        {
            Gamma = _gamma,
            Contrast = _contrast,
            Softness = _softness,
            UseGrayscaleAntialiasing = _grayscale,
            DarkModeLuminanceLift = _darkLift,
        };
    }
}