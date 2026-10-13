namespace CODMControllerProbe;

// Each setting owns a row. Text, values and sliders never share absolute coordinates.
internal static class UiLayout
{
    public static UiStack Stack(int padding = 0)
    {
      var table = new UiStack {
        AutoSize = false, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Dock = DockStyle.Top, Padding = new Padding(Dpi.Px(padding)), Margin = Padding.Empty,
      };
      table.SuspendLayout();
      return table;
    }
    public static void Responsive(Button button)
    {
        button.AutoSizeMode=AutoSizeMode.GrowAndShrink;
        button.SizeChanged+=(_,_)=>Assets.Round(button,Dpi.Px(8));
        Assets.Round(button,Dpi.Px(8));
    }
    public static void Add(UiStack parent, Control child)
    {
        child.Dock = DockStyle.None;
        parent.Controls.Add(child);
    }
    public static Label Text(string text, bool title = false) => new() {
        Text = text, AutoSize = true, Dock = DockStyle.Top,
        Font = Assets.Ui(title ? 11f : 9f, title ? FontStyle.Bold : FontStyle.Regular),
        ForeColor = title ? Assets.MainText : Assets.SubText,
        Margin = new Padding(0, 0, 0, Dpi.Px(title ? 10 : 8)),
    };
    public static UiStack Card(UiStack page, string title, string description = "")
    {
        var card = Stack(18); card.BackColor = Assets.CardWhite;card.IsCard=true;
        card.Margin = new Padding(0, 0, 0, Dpi.Px(16));
        Add(card, Text(title, true));
        if (description.Length > 0) Add(card, Text(description));
        Add(page, card); return card;
    }
    public static Button Action(UiStack parent, string title, Action action, bool primary = false)
    {
        var button = new StudioButton { Text = title, AutoSize = true, MinimumSize = new Size(0,Dpi.Px(42)),
            Padding = new Padding(Dpi.Px(12),Dpi.Px(8),Dpi.Px(12),Dpi.Px(8)),
            Font = Assets.Ui(9.5f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0,Dpi.Px(4),0,Dpi.Px(8)) };
        if (primary) Assets.StylePrimary(button); else Assets.StyleOutline(button);
        Responsive(button); button.Click += (_,_) => action(); Add(parent,button); return button;
    }
    public static CheckBox Check(UiStack parent, string title, bool value, Action<bool> changed, string description = "")
    {
        var check = new StudioCheckBox { Text = title, Checked = value, AutoSize = false,
            Font = Assets.Ui(9.5f), Margin = new Padding(0,Dpi.Px(6),0,Dpi.Px(8)) };
        check.CheckedChanged += (_,_) => changed(check.Checked); Add(parent,check);
        if (description.Length > 0) Add(parent, Text(description));
        return check;
    }
    public static StudioSlider Slider(UiStack parent, string title, int value, int min, int max,
        Func<int,string> format, Action<int> changed, string description = "", int step = 1)
    {
        var block = Stack(); block.Margin = new Padding(0,Dpi.Px(8),0,Dpi.Px(10));
        var heading = new TableLayoutPanel {AutoSizeMode=AutoSizeMode.GrowAndShrink, ColumnCount=2, RowCount=1, AutoSize=true, Dock=DockStyle.Top };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var name = Text(title); name.ForeColor = Assets.MainText;
        var number = Text(format(Math.Clamp(value,min,max))); number.Font=Assets.Ui(9.5f,FontStyle.Bold);
        number.ForeColor=Assets.PrimaryOrange; number.TextAlign=ContentAlignment.MiddleRight;
        number.Margin=new Padding(Dpi.Px(14),0,0,0);
        heading.Controls.Add(name,0,0); heading.Controls.Add(number,1,0); Add(block,heading);
        var slider = new StudioSlider { Minimum=min, Maximum=max, Value=Math.Clamp(value,min,max),
            TickStyle=TickStyle.None, AutoSize=false, Height=Dpi.Px(34), SmallChange=step,
            LargeChange=Math.Max(step,(max-min)/10), Margin=new Padding(0,0,0,Dpi.Px(4)),
            AccessibleName=title };
        slider.ValueChanged += (_,_) => { number.Text=format(slider.Value); changed(slider.Value); };
        Add(block,slider); if(description.Length>0) Add(block,Text(description)); Add(parent,block); return slider;
    }
    public static FlowLayoutPanel Buttons(UiStack parent)
    {
        var flow=new FlowLayoutPanel { AutoSize=true, AutoSizeMode=AutoSizeMode.GrowAndShrink,
            WrapContents=true, Margin=new Padding(0,0,0,Dpi.Px(8)), Dock=DockStyle.Top };
        Add(parent,flow); return flow;
    }
    public static void BoundSlider(UiStack parent,string title,Label value,StudioSlider slider)
    {
        var block=Stack(); block.Margin=new Padding(0,Dpi.Px(6),0,Dpi.Px(10));
        var heading=new TableLayoutPanel {ColumnCount=2,RowCount=1,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Dock=DockStyle.Top};
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var label=Text(title);label.ForeColor=Assets.MainText;value.AutoSize=true;value.Margin=new Padding(Dpi.Px(12),0,0,0);
        heading.Controls.Add(label,0,0);heading.Controls.Add(value,1,0);Add(block,heading);
        slider.AutoSize=false;slider.Height=Dpi.Px(34);slider.TickStyle=TickStyle.None;slider.AccessibleName=title;
        Add(block,slider);Add(parent,block);
    }
    public static Button Choice(FlowLayoutPanel parent,string title,Action action, bool active=false)
    {
        var button=new StudioButton { Text=title, AutoSize=true, MinimumSize=new Size(Dpi.Px(48),Dpi.Px(36)),
            Font=Assets.Ui(9f,FontStyle.Bold), Margin=new Padding(0,0,Dpi.Px(8),Dpi.Px(8)),
            Padding=new Padding(Dpi.Px(10),Dpi.Px(4),Dpi.Px(10),Dpi.Px(4)) };
        Assets.StyleCapsule(button,active,false); Responsive(button); button.Click+=(_,_)=>action(); parent.Controls.Add(button); return button;
    }
    public static void FitText(Control root)
    {
        if(root is UiStack && root.Parent is Panel {AutoScroll:true} viewport){
            var max=new Size(Math.Max(1,viewport.ClientSize.Width-viewport.Padding.Horizontal-SystemInformation.VerticalScrollBarWidth),0);
            if(root.MaximumSize!=max)root.MaximumSize=max;
        }
        foreach(Control child in root.Controls) {
            if(child is Label {AutoSize:true} || child is CheckBox {AutoSize:true}) {
                int width=Math.Max(Dpi.Px(100), root.ClientSize.Width-root.Padding.Horizontal-child.Margin.Horizontal);
                var max=new Size(width,0); if(child.MaximumSize!=max) child.MaximumSize=max;
            }
            FitText(child);
        }
        if(root is UiStack stack) stack.FinishConstruction();
    }
    public static void MappingDialog(Form form, Label title, Label hint, TableLayoutPanel original, params Button[] footerButtons)
    {
        form.Controls.Clear(); form.MaximumSize=Size.Empty; form.FormBorderStyle=FormBorderStyle.Sizable;
        var area=Screen.FromControl(form).WorkingArea;
        form.ClientSize=new Size(Math.Min(Dpi.Px(760),area.Width-Dpi.Px(40)),Math.Min(Dpi.Px(700),area.Height-Dpi.Px(80)));
        form.MinimumSize=new Size(Dpi.Px(640),Dpi.Px(450));
        var shell=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Padding=new Padding(Dpi.Px(20))};
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent,100)); shell.RowStyles.Add(new RowStyle(SizeType.Absolute,Dpi.Px(60)));
        form.Controls.Add(shell);
        var viewport=new Panel {Dock=DockStyle.Fill,AutoScroll=true,Margin=Padding.Empty}; var body=Stack(); viewport.Controls.Add(body);shell.Controls.Add(viewport,0,0);
        title.AutoSize=true;hint.AutoSize=true;title.Margin=new Padding(0,0,0,Dpi.Px(10));hint.Margin=new Padding(0,0,0,Dpi.Px(16)); Add(body,title);Add(body,hint);
        var cells=original.Controls.Cast<Control>().Select(c=>(Control:c,Row:original.GetRow(c),Column:original.GetColumn(c))).ToArray();
        foreach(var group in cells.GroupBy(x=>x.Row).OrderBy(x=>x.Key)) {
            var row=new TableLayoutPanel {AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=original.ColumnCount,RowCount=1,
                Dock=DockStyle.Top,BackColor=Assets.CardWhite,Padding=new Padding(Dpi.Px(10)),Margin=new Padding(0,0,0,Dpi.Px(8))};
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,Dpi.Px(160)));row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            for(int i=2;i<original.ColumnCount;i++) row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            foreach(var cell in group) {
                var control=cell.Control; control.Dock=DockStyle.Fill; control.Margin=new Padding(Dpi.Px(6));
                if(control is Button button) {button.AutoSize=true;button.MinimumSize=new Size(Dpi.Px(76),Dpi.Px(36)); Responsive(button);}
                else {control.AutoSize=true; control.MinimumSize=new Size(0,Dpi.Px(28));}
                row.Controls.Add(control,cell.Column,0);
            }
            Add(body,row);
        }
        var footer=new FlowLayoutPanel {Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false,Padding=new Padding(0,Dpi.Px(12),0,0)};
        foreach(var button in footerButtons.Reverse()) {button.AutoSize=true;button.MinimumSize=new Size(Dpi.Px(110),Dpi.Px(40));Responsive(button);footer.Controls.Add(button);}
        shell.Controls.Add(footer,0,1); if(footerButtons.Length>0){form.AcceptButton=footerButtons[^1];}
        form.Shown+=(_,_)=>FitText(shell); viewport.SizeChanged+=(_,_)=>FitText(body); UiPreview.Attach(form);
    }
}

// Explicit preferred heights avoid TableLayoutPanel adding spare height to the last auto row.
internal sealed class UiStack : Panel
{
    public UiStack(){DoubleBuffered=true;SetStyle(ControlStyles.ResizeRedraw,true);}
    private bool _arranging;
    private bool _layoutPending;
    [System.ComponentModel.DefaultValue(false)]internal bool IsCard {get;set;}
    [System.ComponentModel.DefaultValue(false)]internal bool HasLeftDivider {get;set;}
    private bool _building=true;
    public void FinishConstruction()
    {
        if(_building){_building=false;ResumeLayout(false);}
        PerformLayout();
        if(Dock!=DockStyle.Fill)Height=GetPreferredSize(new Size(Math.Max(1,Width),0)).Height;
        PerformLayout();
    }
    private int RowHeight(Control child,int width)
    {
        int available=Math.Max(1,width-Padding.Horizontal-child.Margin.Horizontal);
        if(child is Label {AutoSize:true} || child is CheckBox)
        {
            var maximum=new Size(available,0);
            if(child.MaximumSize!=maximum)child.MaximumSize=maximum;
        }
        int height=child.GetPreferredSize(new Size(available,0)).Height;
        if(!child.AutoSize && child is not UiStack && child is not StudioCheckBox) height=child.Height;
        return Math.Max(1,height)+child.Margin.Vertical;
    }
    public override Size GetPreferredSize(Size proposedSize)
    {
        int width=proposedSize.Width>1?proposedSize.Width:Math.Max(1,Width);
        int height=Padding.Vertical;
        foreach(Control child in Controls) if(child.Visible || !Visible) height+=RowHeight(child,width);
        return new Size(width,height);
    }
    protected override void OnLayout(LayoutEventArgs e)
    {
        if(_building)return;
        if(_arranging){_layoutPending=true;return;}
        _arranging=true;
        try {
            int y=Padding.Top;
            foreach(Control child in Controls) {
                if(!child.Visible&&Visible)continue;
                int width=Math.Max(1,ClientSize.Width-Padding.Horizontal-child.Margin.Horizontal);
                int height=RowHeight(child,ClientSize.Width)-child.Margin.Vertical;
                var bounds=new Rectangle(Padding.Left+child.Margin.Left,y+child.Margin.Top,width,height);
                if(child.Bounds!=bounds)child.Bounds=bounds;
                y+=height+child.Margin.Vertical;
            }
            int preferred=y+Padding.Bottom;
            if(Dock!=DockStyle.Fill&&Height!=preferred)Height=preferred;
        }finally{_arranging=false;}
        if(_layoutPending){_layoutPending=false;PerformLayout();}
    }
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if(Visible) PerformLayout();
    }
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);PerformLayout();
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if(!IsCard){base.OnPaintBackground(e);return;}
        StudioTheme.PaintCard(e.Graphics,ClientRectangle,BackColor,Parent?.BackColor??StudioTheme.Background);
    }
    protected override void OnPaint(PaintEventArgs e)
    {base.OnPaint(e);if(HasLeftDivider){using var pen=new Pen(StudioTheme.Border);e.Graphics.DrawLine(pen,0,Dpi.Px(8),0,Math.Max(Dpi.Px(8),Height-Dpi.Px(8)));}}
}
