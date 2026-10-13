using System.Drawing.Drawing2D;

namespace CODMControllerProbe;

/// <summary>一个可视化摇杆控件：显示当前 X/Y 轴位置（-32768..32767）。</summary>
public class StickControl : Control
{
    public short AxisX;
    public short AxisY;

    public StickControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        Size = new Size(150, 150);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        float cx = Width / 2f;
        float cy = Height / 2f;
        float r = Math.Min(Width, Height) / 2f - 8f;

        using var bg = new SolidBrush(Color.FromArgb(245, 247, 250));
        g.FillEllipse(bg, cx - r, cy - r, r * 2, r * 2);
        using var rim = new Pen(Color.FromArgb(203, 210, 218), 2f);
        g.DrawEllipse(rim, cx - r, cy - r, r * 2, r * 2);
        g.DrawLine(Pens.Silver, cx - r, cy, cx + r, cy);
        g.DrawLine(Pens.Silver, cx, cy - r, cx, cy + r);

        float nx = AxisX / 32767f;
        float ny = AxisY / 32767f;
        if (nx < -1) nx = -1; if (nx > 1) nx = 1;
        if (ny < -1) ny = -1; if (ny > 1) ny = 1;

        float px = cx + nx * r;
        float py = cy - ny * r;

        using var shadow = new SolidBrush(Color.FromArgb(60, 0, 0, 0));
        g.FillEllipse(shadow, px - 11, py - 9, 22, 22);
        using var knob = new SolidBrush(Color.FromArgb(37, 99, 235));
        g.FillEllipse(knob, px - 13, py - 13, 26, 26);
        using var knobRim = new Pen(Color.White, 2f);
        g.DrawEllipse(knobRim, px - 13, py - 13, 26, 26);
    }

    public void UpdateValues(short x, short y)
    {
        AxisX = x;
        AxisY = y;
        Invalidate();
    }
}
