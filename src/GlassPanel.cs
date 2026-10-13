using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace CODMControllerProbe;

/// <summary>
/// 毛玻璃面板：截取父窗体背景图对应区域 → 快速模糊（降采样放大）→ 叠加半透明白 → 圆角裁剪。
/// 子控件（Label 等）保持 Transparent 背景即可透出玻璃效果，替代白色实底卡片，消除"方框套方框"。
/// </summary>
public class GlassPanel : Panel
{
    /// <summary>白色玻璃不透明度 0-255（越大越白实）。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int GlassAlpha { get; set; } = 185;

    /// <summary>玻璃叠加色（默认白；致谢条可传淡橙）。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color GlassTint { get; set; } = Color.White;

    /// <summary>圆角半径。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Radius { get; set; } = 14;

    private Bitmap? _cache;
    private Size _cacheSize;

    public GlassPanel()
    {
        DoubleBuffered = true;
        BackColor = Color.Transparent;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        _cache?.Dispose();
        _cache = null;
        Invalidate();
    }

    /// <summary>子控件透明背景透出的就是这里画的玻璃（不裁剪，保证 label 底下有玻璃）。</summary>
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var f = FindForm();
        if (f is not { BackgroundImage: not null } || Width < 2 || Height < 2)
        {
            e.Graphics.Clear(Color.White);
            return;
        }
        var bmp = GetGlass(f);
        e.Graphics.DrawImageUnscaled(bmp, 0, 0);
    }

    /// <summary>圆角外露出原背景图（四个角的三角区），圆角内保持玻璃。</summary>
    protected override void OnPaint(PaintEventArgs e)
    {
        var f = FindForm();
        if (f is not { BackgroundImage: not null })
        {
            base.OnPaint(e);
            return;
        }
        using var path = RoundPath(new Rectangle(0, 0, Width - 1, Height - 1), Radius);
        var region = new Region(path);
        var client = new Rectangle(0, 0, Width, Height);
        region.Complement(client);
        using (region)
        using (var guard = new Region())
        {
            e.Graphics.SetClip(region, CombineMode.Replace);
            DrawParentBackground(e.Graphics, f);
            e.Graphics.ResetClip();
        }
    }

    private Bitmap GetGlass(Form f)
    {
        if (_cache != null && _cacheSize == Size) return _cache;
        _cache?.Dispose();
        var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            DrawParentBackground(g, f);
        }
        // 快速模糊：降采样到 1/8 再放大回来（配合中央素净背景足够"磨砂"）
        using (var small = new Bitmap(bmp, new Size(Math.Max(1, Width / 8), Math.Max(1, Height / 8))))
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(small, 0, 0, Width, Height);
        }
        using (var g = Graphics.FromImage(bmp))
        using (var brush = new SolidBrush(Color.FromArgb(GlassAlpha, GlassTint)))
        {
            g.FillRectangle(brush, 0, 0, Width, Height);
        }
        _cache = bmp;
        _cacheSize = Size;
        return bmp;
    }

    /// <summary>把父窗体背景图（Zoom 布局）在本控件区域的画面画到 g 的控件坐标下。</summary>
    private void DrawParentBackground(Graphics g, Form form)
    {
        var img = form.BackgroundImage;
        var ctlRect = form.RectangleToClient(RectangleToScreen(new Rectangle(0, 0, Width, Height)));
        var imgRect = ZoomRect(img.Width, img.Height, form.ClientSize.Width, form.ClientSize.Height);
        var inter = Rectangle.Intersect(ctlRect, imgRect);
        if (inter.IsEmpty)
        {
            g.Clear(Color.White);
            return;
        }
        var src = new Rectangle(
            (inter.X - imgRect.X) * img.Width / imgRect.Width,
            (inter.Y - imgRect.Y) * img.Height / imgRect.Height,
            inter.Width * img.Width / imgRect.Width,
            inter.Height * img.Height / imgRect.Height);
        g.DrawImage(img,
            new Rectangle(inter.X - ctlRect.X, inter.Y - ctlRect.Y, inter.Width, inter.Height),
            src, GraphicsUnit.Pixel);
    }

    /// <summary>ImageLayout.Zoom 等价：图片完整显示、居中、四周留边。</summary>
    private static Rectangle ZoomRect(int iw, int ih, int ww, int wh)
    {
        float s = Math.Min((float)ww / iw, (float)wh / ih);
        int dw = (int)(iw * s), dh = (int)(ih * s);
        return new Rectangle((ww - dw) / 2, (wh - dh) / 2, dw, dh);
    }

    private static GraphicsPath RoundPath(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        int d = Math.Max(1, radius * 2);
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
