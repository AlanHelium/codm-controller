using System.Drawing.Drawing2D;

namespace CODMControllerProbe;

internal static class StudioTheme
{
    public static readonly Color Background=Color.FromArgb(23,25,28), Sidebar=Color.FromArgb(26,28,31),
        Surface=Color.FromArgb(32,35,39), Border=Color.FromArgb(53,57,62),
        Text=Color.FromArgb(242,240,235), Muted=Color.FromArgb(160,164,169),
        Accent=Color.FromArgb(239,133,76), Selected=Color.FromArgb(57,42,34);
    public static Font Font(float size=10,FontStyle style=FontStyle.Regular)=>new("Microsoft YaHei UI",size,style);
    public static GraphicsPath SoftRect(RectangleF r,float radius)
    {
        var path=new GraphicsPath();float d=Math.Min(radius*2,Math.Min(r.Width,r.Height));
        path.AddArc(r.Left,r.Top,d,d,180,90);path.AddArc(r.Right-d,r.Top,d,d,270,90);
        path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);path.AddArc(r.Left,r.Bottom-d,d,d,90,90);path.CloseFigure();return path;
    }
    public static void PaintCard(Graphics g,Rectangle bounds,Color surface,Color background)
    {
        var saved=g.Save();try {
        g.SmoothingMode=SmoothingMode.AntiAlias;using(var b=new SolidBrush(background))g.FillRectangle(b,bounds);
        var face=new Rectangle(1,1,Math.Max(1,bounds.Width-3),Math.Max(1,bounds.Height-5));
        using var shadowPath=SoftRect(new RectangleF(face.X,face.Y+2,face.Width,face.Height),Dpi.Px(12));
        using(var shadow=new SolidBrush(Color.FromArgb(50,0,0,0)))g.FillPath(shadow,shadowPath);
        using var path=SoftRect(face,Dpi.Px(12));
        using var fill=new LinearGradientBrush(face,Color.FromArgb(Math.Min(255,surface.R+5),Math.Min(255,surface.G+5),Math.Min(255,surface.B+5)),surface,LinearGradientMode.Vertical);
        g.FillPath(fill,path);using var edge=new Pen(Border);g.DrawPath(edge,path);
        using var shine=new Pen(Color.FromArgb(60,100,106,111));g.DrawLine(shine,Dpi.Px(14),2,Math.Max(Dpi.Px(14),bounds.Width-Dpi.Px(14)),2);
        }finally{g.Restore(saved);}
    }
    public static void Apply(Control root)
    {
        if(root is StudioSlider or StudioBrand or StudioNav or StudioCurve) return;
        if(root is OutlineLabel outlined) outlined.OutlineEnabled=false;
        if(root is Button button) {
            bool active=button.BackColor==Assets.PrimaryOrange;
            if(active) Assets.StylePrimary(button); else Assets.StyleOutline(button);
        } else if(root is TextBoxBase or ListBox or ComboBox or NumericUpDown) {
            root.BackColor=Background;root.ForeColor=Text;
            if(root is TextBox box) box.BorderStyle=BorderStyle.FixedSingle;
            if(root is ListBox list)list.BorderStyle=BorderStyle.None;
            if(root is ComboBox combo){combo.FlatStyle=FlatStyle.Flat;combo.DrawMode=DrawMode.OwnerDrawFixed;combo.DrawItem+=(_,e)=>{e.DrawBackground();if(e.Index>=0)TextRenderer.DrawText(e.Graphics,combo.Items[e.Index]?.ToString(),combo.Font,e.Bounds,Text,TextFormatFlags.Left|TextFormatFlags.VerticalCenter);e.DrawFocusRectangle();};}
        } else if(root is Label label) {
            label.BackColor=Color.Transparent;
            if(label.ForeColor!=Assets.PrimaryOrange && label.ForeColor!=Assets.OkGreen && label.ForeColor!=Assets.ErrRed)
                label.ForeColor=label.Font.Bold?Text:Muted;
        } else if(root is CheckBox or RadioButton) {root.ForeColor=Text;root.BackColor=Color.Transparent;}
        else if(root is not PictureBox) {
            bool card=root.BackColor==Color.White || root.BackColor==Assets.CardWhite;
            root.BackColor=card?Surface:Background;root.ForeColor=Text;
        }
        if(root is TabControl tabs) {
            tabs.DrawMode=TabDrawMode.OwnerDrawFixed;tabs.SizeMode=TabSizeMode.Fixed;
            tabs.ItemSize=new Size(Dpi.Px(170),Dpi.Px(40));
            tabs.DrawItem+=(_,e)=>{
                var active=e.Index==tabs.SelectedIndex;
                using var brush=new SolidBrush(active?Selected:Sidebar);e.Graphics.FillRectangle(brush,e.Bounds);
                TextRenderer.DrawText(e.Graphics,tabs.TabPages[e.Index].Text,Font(10,FontStyle.Bold),e.Bounds,
                    active?Accent:Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
            };
        }
        foreach(Control child in root.Controls) Apply(child);
    }
}

internal class StudioButton : Button
{
    private bool _hover,_pressed;
    public StudioButton() {SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.ResizeRedraw|ControlStyles.Opaque,true);Cursor=Cursors.Hand;AutoSizeMode=AutoSizeMode.GrowAndShrink;}
    public override Size GetPreferredSize(Size proposedSize)
    {
        var text=TextRenderer.MeasureText(Text,Font,Size.Empty,TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix);
        return new Size(Math.Max(MinimumSize.Width,text.Width+Padding.Horizontal+Dpi.Px(20)),Math.Max(MinimumSize.Height,text.Height+Padding.Vertical+Dpi.Px(10)));
    }
    protected override void OnMouseEnter(EventArgs e){_hover=true;Invalidate();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){_hover=false;_pressed=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnMouseDown(MouseEventArgs e){_pressed=true;Invalidate();base.OnMouseDown(e);}
    protected override void OnMouseUp(MouseEventArgs e){_pressed=false;Invalidate();base.OnMouseUp(e);}
    protected override void OnGotFocus(EventArgs e){Invalidate();base.OnGotFocus(e);}
    protected override void OnLostFocus(EventArgs e){Invalidate();base.OnLostFocus(e);}
    protected override void OnPaintBackground(PaintEventArgs e)
    {Control? ancestor=Parent;while(ancestor!=null&&ancestor.BackColor.A<255)ancestor=ancestor.Parent;using var background=new SolidBrush(ancestor?.BackColor??StudioTheme.Background);e.Graphics.FillRectangle(background,ClientRectangle);}
    protected override void OnPaint(PaintEventArgs e)
    {
        // Repaint the entire rectangle on every frame, including pixels outside the curved face.
        // This also covers WM_PRINT and resize/focus paints that omit background erasure.
        OnPaintBackground(e);
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        bool primary=BackColor==Assets.PrimaryOrange;
        var fill=!Enabled?StudioTheme.Sidebar:_pressed?Color.FromArgb(65,52,43):_hover?(primary?Color.FromArgb(255,153,96):Color.FromArgb(44,47,51)):BackColor;
        if(Width<5||Height<5)return;
        using var path=StudioTheme.SoftRect(new RectangleF(1.5f,1.5f,Width-3f,Height-3f),Dpi.Px(8));
        using var brush=new SolidBrush(fill);e.Graphics.FillPath(brush,path);
        using var edge=new Pen(Focused?Color.FromArgb(126,132,140):primary?fill:StudioTheme.Border,1f){LineJoin=LineJoin.Round};e.Graphics.DrawPath(edge,path);
        TextFormatFlags align=TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix|TextFormatFlags.PreserveGraphicsClipping|TextFormatFlags.PreserveGraphicsTranslateTransform;
        align|=TextAlign is ContentAlignment.MiddleLeft or ContentAlignment.TopLeft?TextFormatFlags.Left:TextFormatFlags.HorizontalCenter;
        var bounds=new Rectangle(Padding.Left+2,1,Math.Max(1,Width-Padding.Horizontal-4),Height-2);
        TextRenderer.DrawText(e.Graphics,Text,Font,bounds,!Enabled?StudioTheme.Muted:primary?StudioTheme.Background:ForeColor,align);
    }
}

internal sealed class StudioNumber : UserControl
{
    private readonly TextBox _entry=new(){BorderStyle=BorderStyle.None,TextAlign=HorizontalAlignment.Center,BackColor=StudioTheme.Background,ForeColor=StudioTheme.Text};
    private readonly Label _suffix=new(){AutoSize=false,Dock=DockStyle.Right,TextAlign=ContentAlignment.MiddleLeft,ForeColor=StudioTheme.Muted,BackColor=StudioTheme.Background,Width=0};
    [System.ComponentModel.DefaultValue("")]public string DisplaySuffix{get=>_suffix.Text;set{_suffix.Text=value;_suffix.Font=_entry.Font;_suffix.Width=string.IsNullOrEmpty(value)?0:TextRenderer.MeasureText(value,_suffix.Font).Width;_entry.TextAlign=string.IsNullOrEmpty(value)?HorizontalAlignment.Center:HorizontalAlignment.Right;}}
    private int _min,_max=100,_value;
    [System.ComponentModel.DefaultValue(0)]public int Minimum{get=>_min;set{_min=value;}}
    [System.ComponentModel.DefaultValue(100)]public int Maximum{get=>_max;set{_max=value;}}
    [System.ComponentModel.DefaultValue(0)]public int Value{get=>_value;set{int v=Math.Clamp(value,Minimum,Maximum);if(v==_value){_entry.Text=v.ToString();return;}_value=v;_entry.Text=v.ToString();ValueChanged?.Invoke(this,EventArgs.Empty);}}
    public event EventHandler? ValueChanged;
    public StudioNumber()
    {
        Height=Dpi.Px(36);MinimumSize=new Size(Dpi.Px(150),Dpi.Px(36));
        var row=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=Padding.Empty};
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,Dpi.Px(34)));row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,Dpi.Px(34)));row.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var box=new Panel{Dock=DockStyle.Fill,BackColor=StudioTheme.Background,Padding=new Padding(Dpi.Px(6),Dpi.Px(7),Dpi.Px(6),Dpi.Px(6)),Margin=new Padding(0,0,Dpi.Px(4),0)};_entry.Dock=DockStyle.Fill;_entry.Font=StudioTheme.Font(11);box.Controls.Add(_entry);box.Controls.Add(_suffix);row.Controls.Add(box,0,0);
        foreach(var pair in new[]{("−",1),("+",2)}){int direction=pair.Item2==1?-1:1;var b=new StudioButton{Text=pair.Item1,Dock=DockStyle.Fill,Margin=new Padding(Dpi.Px(2),0,0,0),Font=StudioTheme.Font(12),AccessibleName=direction<0?"Decrease":"Increase"};Assets.StyleOutline(b);b.Click+=(_,_)=>Value+=direction;row.Controls.Add(b,pair.Item2,0);}
        Controls.Add(row);_entry.Validating+=(_,_)=>Commit();_entry.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Enter){Commit();e.SuppressKeyPress=true;}else if(e.KeyCode==Keys.Up){Value++;e.SuppressKeyPress=true;}else if(e.KeyCode==Keys.Down){Value--;e.SuppressKeyPress=true;}};
        box.Paint+=(_,e)=>{e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using var p=new Pen(StudioTheme.Border);using var path=Assets.RoundPath(new Rectangle(0,0,box.Width-1,box.Height-1),Dpi.Px(7));e.Graphics.DrawPath(p,path);};
    }
    private void Commit(){if(int.TryParse(_entry.Text,out int v))Value=v;else _entry.Text=Value.ToString();}
}

internal sealed class StudioCheckBox : CheckBox
{
    private static readonly TextFormatFlags TextFlags=TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding|TextFormatFlags.PreserveGraphicsClipping|TextFormatFlags.PreserveGraphicsTranslateTransform;
    public StudioCheckBox(){FlatStyle=FlatStyle.Flat;SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.ResizeRedraw,true);Cursor=Cursors.Hand;}
    public override Size GetPreferredSize(Size proposedSize)
    {
        int width=proposedSize.Width>1?proposedSize.Width:Math.Max(Dpi.Px(100),Width);
        if(MaximumSize.Width>0)width=Math.Min(width,MaximumSize.Width);
        var size=TextRenderer.MeasureText(Text,Font,new Size(Math.Max(1,width-Dpi.Px(28)),int.MaxValue),TextFlags);
        return new Size(width,Math.Max(Dpi.Px(28),size.Height+Dpi.Px(10)));
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        Control? ancestor=Parent;while(ancestor!=null&&ancestor.BackColor.A<255)ancestor=ancestor.Parent;
        using var background=new SolidBrush(ancestor?.BackColor??StudioTheme.Background);
        e.Graphics.FillRectangle(background,ClientRectangle);
    }
    protected override void OnPaint(PaintEventArgs e){OnPaintBackground(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;int side=Dpi.Px(16),y=(Height-side)/2;using var path=Assets.RoundPath(new Rectangle(1,y,side,side),Dpi.Px(3));using var fill=new SolidBrush(Checked?StudioTheme.Accent:StudioTheme.Background);e.Graphics.FillPath(fill,path);using var p=new Pen(Focused?StudioTheme.Accent:StudioTheme.Border,1);e.Graphics.DrawPath(p,path);
        if(Checked){using var tick=new Pen(StudioTheme.Background,2f*Dpi.Scale);e.Graphics.DrawLines(tick,new[]{new Point(Dpi.Px(4),y+Dpi.Px(8)),new Point(Dpi.Px(7),y+Dpi.Px(11)),new Point(Dpi.Px(12),y+Dpi.Px(5))});}
        using var textBrush=new SolidBrush(Enabled?StudioTheme.Text:StudioTheme.Muted);
        using var format=new StringFormat{Alignment=StringAlignment.Near,LineAlignment=StringAlignment.Center,Trimming=StringTrimming.None};
        e.Graphics.DrawString(Text,Font,textBrush,new RectangleF(Dpi.Px(26),Dpi.Px(3),Math.Max(1,Width-Dpi.Px(28)),Math.Max(1,Height-Dpi.Px(6))),format);
        if(Focused)ControlPaint.DrawFocusRectangle(e.Graphics,new Rectangle(0,0,Width-1,Height-1),StudioTheme.Accent,Parent?.BackColor??StudioTheme.Surface);
    }
}

internal sealed class StudioTabPage : Panel
{
    public StudioTabPage(string text){Text=text;}
}

internal sealed class StudioTabs : Panel
{
    private readonly FlowLayoutPanel _header=new(){Dock=DockStyle.Top,Height=Dpi.Px(44),WrapContents=false,Margin=Padding.Empty};
    private readonly Panel _body=new(){Dock=DockStyle.Fill,Margin=Padding.Empty};
    private readonly List<Button> _buttons=new();private int _index;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]public PageCollection TabPages{get;}
    [System.ComponentModel.DefaultValue(0)]public int SelectedIndex{get=>_index;set{_index=Math.Clamp(value,0,Math.Max(0,TabPages.Count-1));RefreshSelection();}}
    public StudioTabs(){TabPages=new PageCollection(this);Controls.Add(_body);Controls.Add(_header);}
    public sealed class PageCollection(StudioTabs owner):System.Collections.ObjectModel.Collection<StudioTabPage>
    {protected override void InsertItem(int index,StudioTabPage page){base.InsertItem(index,page);page.Dock=DockStyle.Fill;owner._body.Controls.Add(page);var b=new StudioButton{Text=page.Text,Size=new Size(Dpi.Px(160),Dpi.Px(38)),Margin=new Padding(0,0,Dpi.Px(8),0),Font=StudioTheme.Font(10,FontStyle.Bold)};b.Click+=(_,_)=>owner.SelectedIndex=owner.TabPages.IndexOf(page);owner._buttons.Add(b);owner._header.Controls.Add(b);owner.RefreshSelection();}}
    private void RefreshSelection(){for(int i=0;i<TabPages.Count;i++){TabPages[i].Visible=i==_index;Assets.StyleCapsule(_buttons[i],i==_index,false);}if(TabPages.Count>0)TabPages[_index].BringToFront();}
}

// A real slider with pointer capture, keyboard navigation and an accessible value.
// Its API matches the old UI sliders so all existing settings callbacks stay intact.
internal class StudioSlider : Control
{
    private int _min,_max=10,_value;private bool _dragging;
    [System.ComponentModel.DefaultValue(0)]public int Minimum {get=>_min;set{_min=value;if(_max<_min)_max=_min;Value=Math.Clamp(_value,_min,_max);Invalidate();}}
    [System.ComponentModel.DefaultValue(10)]public int Maximum {get=>_max;set{_max=Math.Max(_min,value);Value=Math.Clamp(_value,_min,_max);Invalidate();}}
    [System.ComponentModel.DefaultValue(0)]public int Value {get=>_value;set{int next=Math.Clamp(value,_min,_max);if(next==_value)return;_value=next;Invalidate();ValueChanged?.Invoke(this,EventArgs.Empty);}}
    [System.ComponentModel.DefaultValue(1)]public int SmallChange {get;set;}=1;
    [System.ComponentModel.DefaultValue(5)]public int LargeChange {get;set;}=5;
    [System.ComponentModel.DefaultValue(TickStyle.None)]public TickStyle TickStyle {get;set;}
    [System.ComponentModel.DefaultValue(1)]public int TickFrequency {get;set;}=1;
    public event EventHandler? ValueChanged;
    public StudioSlider(){SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.Selectable,true);Height=Dpi.Px(34);TabStop=true;Cursor=Cursors.Hand;AccessibleRole=AccessibleRole.Slider;}
    protected override AccessibleObject CreateAccessibilityInstance()=>new SliderAccessibility(this);
    private sealed class SliderAccessibility(StudioSlider owner):ControlAccessibleObject(owner)
    {public override string? Value {get=>owner.Value.ToString();set{if(int.TryParse(value,out int v))owner.Value=v;}}}
    private void PointValue(int x){int inset=Dpi.Px(10);Value=_min+(int)Math.Round(Math.Clamp((x-inset)/(double)Math.Max(1,Width-2*inset),0,1)*(_max-_min));}
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button!=MouseButtons.Left||!Enabled)return;Focus();_dragging=true;Capture=true;PointValue(e.X);}
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(_dragging)PointValue(e.X);}
    protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);_dragging=false;Capture=false;}
    protected override void OnMouseCaptureChanged(EventArgs e){base.OnMouseCaptureChanged(e);if(!Capture)_dragging=false;}
    protected override bool IsInputKey(Keys keyData)=>keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End ||base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);int next=e.KeyCode switch{Keys.Left or Keys.Down=>Value-SmallChange,Keys.Right or Keys.Up=>Value+SmallChange,Keys.PageDown=>Value-LargeChange,Keys.PageUp=>Value+LargeChange,Keys.Home=>Minimum,Keys.End=>Maximum,_=>Value};Value=next;e.Handled=e.KeyCode is Keys.Left or Keys.Down or Keys.Right or Keys.Up or Keys.PageDown or Keys.PageUp or Keys.Home or Keys.End;}
    protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;int inset=Dpi.Px(10),y=Height/2;float x=inset+(Width-2*inset)*(Value-Minimum)/(float)Math.Max(1,Maximum-Minimum);
        using var track=new Pen(StudioTheme.Border,Dpi.Px(4)){StartCap=LineCap.Round,EndCap=LineCap.Round};e.Graphics.DrawLine(track,inset,y,Math.Max(inset,Width-inset),y);
        using var active=new Pen(Enabled?StudioTheme.Accent:StudioTheme.Muted,Dpi.Px(4)){StartCap=LineCap.Round,EndCap=LineCap.Round};e.Graphics.DrawLine(active,inset,y,x,y);
        int r=Dpi.Px(6);using var thumb=new SolidBrush(Enabled?StudioTheme.Text:StudioTheme.Muted);e.Graphics.FillEllipse(thumb,x-r,y-r,r*2,r*2);
        using var outline=new Pen(StudioTheme.Accent,Focused?Dpi.Px(3):Dpi.Px(2));e.Graphics.DrawEllipse(outline,x-r,y-r,r*2,r*2);
    }
}

internal sealed class StudioBrand : Control
{
    public StudioBrand(){SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint,true);}
    protected override void OnPaint(PaintEventArgs e){e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;float s=Math.Min(Width,Height)/32f;e.Graphics.ScaleTransform(s,s);using var b=new SolidBrush(StudioTheme.Accent);
        e.Graphics.FillPolygon(b,new[]{new PointF(3,3),new PointF(24,3),new PointF(29,9),new PointF(22,16),new PointF(29,23),new PointF(24,29),new PointF(3,29),new PointF(11,21),new PointF(20,21),new PointF(23,18),new PointF(15,18),new PointF(9,12),new PointF(20,12),new PointF(23,9),new PointF(9,9)});
    }
}

internal sealed class StudioNav : Button
{
    [System.ComponentModel.DefaultValue(false)]public bool Selected {get;set;}
    [System.ComponentModel.DefaultValue(0)]public int Symbol {get;set;}
    private bool _hover;
    public StudioNav(){SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint,true);Cursor=Cursors.Hand;FlatStyle=FlatStyle.Flat;AccessibleRole=AccessibleRole.PageTab;}
    protected override void OnMouseEnter(EventArgs e){_hover=true;Invalidate();base.OnMouseEnter(e);}protected override void OnMouseLeave(EventArgs e){_hover=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnGotFocus(EventArgs e){Invalidate();base.OnGotFocus(e);}protected override void OnLostFocus(EventArgs e){Invalidate();base.OnLostFocus(e);}
    protected override void OnPaint(PaintEventArgs e){e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using var fill=new SolidBrush(Selected?StudioTheme.Selected:_hover?StudioTheme.Surface:StudioTheme.Sidebar);e.Graphics.FillRectangle(fill,ClientRectangle);
        if(Selected){using var bar=new SolidBrush(StudioTheme.Accent);e.Graphics.FillRectangle(bar,0,Dpi.Px(8),Dpi.Px(3),Height-Dpi.Px(16));}
        Color color=Selected?StudioTheme.Accent:StudioTheme.Muted;using var p=new Pen(color,1.6f*Dpi.Scale){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round};
        var g=e.Graphics;var saved=g.Save();g.TranslateTransform(Dpi.Px(22),Height/2-Dpi.Px(10));g.ScaleTransform(Dpi.Scale,Dpi.Scale);
        switch(Symbol){case 0:g.DrawLines(p,new[]{new Point(0,9),new Point(10,0),new Point(20,9)});g.DrawLines(p,new[]{new Point(3,7),new Point(3,20),new Point(8,20),new Point(8,13),new Point(13,13),new Point(13,20),new Point(18,20),new Point(18,7)});break;
            case 1:for(int i=0;i<3;i++){g.DrawLine(p,0,4+i*7,20,4+i*7);g.DrawRectangle(p,4+i*4,1+i*7,3,6);}break;
            case 2:g.DrawPolygon(p,new[]{new Point(4,4),new Point(16,4),new Point(21,18),new Point(16,20),new Point(12,15),new Point(8,15),new Point(4,20),new Point(0,18)});g.DrawLine(p,4,10,9,10);g.DrawLine(p,6,7,6,13);g.DrawEllipse(p,14,8,2,2);g.DrawEllipse(p,17,11,2,2);break;
            case 3:g.DrawEllipse(p,12,0,4,4);g.DrawLines(p,new[]{new Point(3,9),new Point(8,6),new Point(13,8),new Point(10,13),new Point(17,17)});g.DrawLine(p,10,13,4,20);g.DrawLine(p,13,8,20,10);break;
            case 4:g.DrawPolygon(p,new[]{new Point(3,0),new Point(3,18),new Point(8,13),new Point(12,20),new Point(15,18),new Point(11,11),new Point(18,11)});break;
            case 5:for(int i=0;i<5;i++)g.DrawLine(p,i*4,10-(i==2?9:i%2==0?3:6),i*4,10+(i==2?9:i%2==0?3:6));break;
            case 6:g.DrawRectangle(p,0,5,20,14);g.DrawRectangle(p,6,1,8,4);g.DrawLine(p,0,11,20,11);break;
            default:g.DrawEllipse(p,2,2,16,16);g.DrawLine(p,10,6,10,14);g.DrawLine(p,6,10,14,10);break;}
        g.Restore(saved);TextRenderer.DrawText(g,Text,Font,new Rectangle(Dpi.Px(55),0,Width-Dpi.Px(60),Height),Selected?StudioTheme.Accent:StudioTheme.Text,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
        if(Focused)ControlPaint.DrawFocusRectangle(g,new Rectangle(Dpi.Px(4),Dpi.Px(4),Width-Dpi.Px(8),Height-Dpi.Px(8)),StudioTheme.Accent,StudioTheme.Sidebar);
    }
}

internal sealed class StudioCurve:Control
{
    private readonly Func<FeelCurve> _curve;
    public StudioCurve(Func<FeelCurve> curve){_curve=curve;SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.ResizeRedraw,true);}
    public StudioCurve():this(()=>new FeelCurve()){}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;var r=new Rectangle(Dpi.Px(40),Dpi.Px(20),Math.Max(1,Width-Dpi.Px(64)),Math.Max(1,Height-Dpi.Px(58)));
        using var grid=new Pen(StudioTheme.Border,1);for(int i=0;i<=8;i++){float x=r.Left+r.Width*i/8f,y=r.Top+r.Height*i/8f;g.DrawLine(grid,x,r.Top,x,r.Bottom);g.DrawLine(grid,r.Left,y,r.Right,y);}
        var c=_curve();var points=Enumerable.Range(0,101).Select(i=>new PointF(r.Left+r.Width*i/100f,r.Bottom-r.Height*Math.Clamp(c.Eval(i/100f),0,1))).ToArray();
        using var area=new GraphicsPath();area.AddLines(points);area.AddLine(points[^1],new PointF(r.Right,r.Bottom));area.AddLine(r.Right,r.Bottom,r.Left,r.Bottom);area.CloseFigure();using var shade=new LinearGradientBrush(r,Color.FromArgb(105,StudioTheme.Accent),Color.FromArgb(0,StudioTheme.Accent),LinearGradientMode.Vertical);g.FillPath(shade,area);
        using var line=new Pen(StudioTheme.Accent,2.3f*Dpi.Scale);g.DrawLines(line,points);
        using var font=StudioTheme.Font(8);TextRenderer.DrawText(g,"1.0",font,new Rectangle(0,r.Top-8,r.Left-6,24),StudioTheme.Muted,TextFormatFlags.Right);TextRenderer.DrawText(g,"0.0",font,new Rectangle(0,r.Bottom-12,r.Left-6,24),StudioTheme.Muted,TextFormatFlags.Right);
        TextRenderer.DrawText(g,Lang.T("输入","Input"),font,new Rectangle(r.Left,r.Bottom+10,r.Width,24),StudioTheme.Muted,TextFormatFlags.HorizontalCenter);TextRenderer.DrawText(g,Lang.T("输出","Output"),font,new Rectangle(0,r.Top+r.Height/2-12,r.Left,24),StudioTheme.Muted);
    }
}
