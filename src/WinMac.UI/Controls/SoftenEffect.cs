using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace WinMac.UI.Controls;

/// <summary>
/// “柔化”的本质杠杆：把元素连同其子树栅格化为位图缓存(grayscale skyline AA)，
/// 从而去掉 DirectWrite 的 ClearType 亚像素彩边——这是不引入原生注入器前提下，
/// 最接近 macOS 灰度 AA 的一个真实手段。模糊强度越大越彻底，代价是轻微位图开销。
/// </summary>
public static class SoftenEffect
{
    /// <summary>按柔化强度开关目标元素的位图缓存。0 则不缓存。</summary>
    public static void Apply(FrameworkElement target, double softnessBlur)
    {
        if (target == null)
            return;

        // softnessBlur ≤ 2px：只要非零即启用位图缓存。
        target.CacheMode = softnessBlur > 0.01 ? new BitmapCache() : null;
    }
}