namespace CODMControllerProbe;

/// <summary>
/// DPI 适配帮助：布局像素一律乘 Dpi.Px()，随窗口所在显示器实际 DPI 等比缩放。
/// 字体由 WinForms 按窗口 DPI 自动渲染（PerMonitorV2），控件尺寸同步放大 → 任何屏幕（
/// 96/144/192 DPI、虚拟显示器、串流副屏）都不会文字溢出/遮挡。
/// </summary>
internal static class Dpi
{
    /// <summary>当前窗口 DPI / 96。默认 1（96 DPI 基准）；窗口移动后由 Form 在 DPIChanged 中刷新。</summary>
    public static float Scale = 1f;

    public static int Px(int v) => (int)MathF.Round(v * Scale);
}
