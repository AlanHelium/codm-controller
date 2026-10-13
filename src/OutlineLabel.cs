using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CODMControllerProbe;

/// <summary>
/// 带描边文字标签：在复杂背景（游戏战场图）上保证文字清晰可读。
/// 实现方式：GraphicsPath.AddString 沿字形轮廓描细线 + 实色填充 ——
/// 正文不偏移、不重影，描边是干净的轮廓线（比 8 方向偏移方案清晰得多）。
/// </summary>
public class OutlineLabel : Label
{
    /// <summary>描边颜色（默认深色，适合浅色正文）。</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color OutlineColor { get; set; } = Color.FromArgb(190, 6, 10, 16);

    /// <summary>描边宽度（像素，细线即可，过宽会糊）。</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float OutlineWidth { get; set; } = 1.8f;

    /// <summary>是否启用描边（关闭时回退为标准 Label 绘制）。</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool OutlineEnabled { get; set; } = true;

    protected override void OnPaint(PaintEventArgs e)
    {
        if (string.IsNullOrEmpty(Text) || !OutlineEnabled)
        {
            base.OnPaint(e);
            return;
        }
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var sf = new StringFormat { LineAlignment = StringAlignment.Center };
        switch (TextAlign)
        {
            case ContentAlignment.MiddleCenter: sf.Alignment = StringAlignment.Center; break;
            case ContentAlignment.MiddleRight: sf.Alignment = StringAlignment.Far; break;
            default: sf.Alignment = StringAlignment.Near; break;
        }

        var rc = new RectangleF(0f, 0f, Width - Padding.Horizontal, Height - Padding.Vertical);
        using var gp = new GraphicsPath();
        float emSize = Font.SizeInPoints * g.DpiY / 72f;
        gp.AddString(Text, Font.FontFamily, (int)Font.Style, emSize, rc, sf);

        using var outlinePen = new Pen(OutlineColor, OutlineWidth) { LineJoin = LineJoin.Round };
        using var fill = new SolidBrush(ForeColor);
        g.DrawPath(outlinePen, gp);   // 先描轮廓
        g.FillPath(fill, gp);         // 再填正文
    }
}
