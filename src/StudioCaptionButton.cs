using System.Drawing.Drawing2D;

namespace CODMControllerProbe;

internal enum StudioCaptionAction { Minimize, Maximize, Close }

// Draw vectors rather than font glyphs so strokes, scale and optical alignment agree.
internal sealed class StudioCaptionButton : Button
{
    private readonly StudioCaptionAction _action;
    private bool _hover, _pressed;
    public StudioCaptionButton(StudioCaptionAction action)
    {
        _action=action;Text="";AutoSize=false;TabStop=true;Cursor=Cursors.Default;
        SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.ResizeRedraw|ControlStyles.Opaque,true);
        Size=new Size(Dpi.Px(38),Dpi.Px(36));FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;
        AccessibleRole=AccessibleRole.PushButton;
        AccessibleName=action switch {StudioCaptionAction.Minimize=>Lang.T("最小化","Minimize"),StudioCaptionAction.Close=>Lang.T("关闭","Close"),_=>Lang.T("最大化","Maximize")};
    }
    protected override void OnMouseEnter(EventArgs e){_hover=true;Invalidate();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){_hover=false;_pressed=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnMouseDown(MouseEventArgs e){if(e.Button==MouseButtons.Left)_pressed=true;Invalidate();base.OnMouseDown(e);}
    protected override void OnMouseUp(MouseEventArgs e){_pressed=false;Invalidate();base.OnMouseUp(e);}
    protected override void OnGotFocus(EventArgs e){Invalidate();base.OnGotFocus(e);}
    protected override void OnLostFocus(EventArgs e){Invalidate();base.OnLostFocus(e);}
    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics;g.Clear(Parent?.BackColor??StudioTheme.Sidebar);if(Width<5||Height<5)return;
        g.SmoothingMode=SmoothingMode.AntiAlias;g.PixelOffsetMode=PixelOffsetMode.HighQuality;
        bool close=_action==StudioCaptionAction.Close;
        var fill=_hover?(close?(_pressed?Color.FromArgb(130,43,42):Color.FromArgb(169,54,49))
            :_pressed?Color.FromArgb(55,59,65):Color.FromArgb(43,47,52)):StudioTheme.Surface;
        using var path=StudioTheme.SoftRect(new RectangleF(1.5f,1.5f,Width-3f,Height-3f),Dpi.Px(8));
        using(var brush=new SolidBrush(fill))g.FillPath(brush,path);
        using(var edge=new Pen(Focused?StudioTheme.Accent:_hover?Color.FromArgb(87,92,100):StudioTheme.Border,1))g.DrawPath(edge,path);
        var color=!Enabled?StudioTheme.Muted:_hover?StudioTheme.Text:Color.FromArgb(197,203,211);
        float scale=Dpi.Scale,cx=Width/2f,cy=Height/2f;
        using var pen=new Pen(color,1.2f*scale){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round};
        if(_action==StudioCaptionAction.Minimize)g.DrawLine(pen,cx-5*scale,cy,cx+5*scale,cy);
        else if(close)
        {
            g.DrawLine(pen,cx-4.25f*scale,cy-4.25f*scale,cx+4.25f*scale,cy+4.25f*scale);
            g.DrawLine(pen,cx+4.25f*scale,cy-4.25f*scale,cx-4.25f*scale,cy+4.25f*scale);
        }
        else if(FindForm()?.WindowState==FormWindowState.Maximized)
        {
            AccessibleName=Lang.T("还原窗口","Restore window");
            g.DrawRectangle(pen,cx-2.5f*scale,cy-5*scale,8*scale,8*scale);
            var front=new RectangleF(cx-5*scale,cy-2.5f*scale,8*scale,8*scale);
            using(var brush=new SolidBrush(fill))g.FillRectangle(brush,front);
            g.DrawRectangle(pen,front.X,front.Y,front.Width,front.Height);
        }
        else
        {
            AccessibleName=Lang.T("最大化","Maximize");
            g.DrawRectangle(pen,cx-4.5f*scale,cy-4.5f*scale,9*scale,9*scale);
        }
    }
}
