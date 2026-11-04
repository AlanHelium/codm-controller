namespace CODMControllerProbe;

public partial class MainForm
{
    private void BuildStudioOverview()
    {
        // Preserve the reference structure: product, profile card, parameter row, actions.
        var page=new Panel {Dock=DockStyle.Fill,AutoScroll=false,BackColor=StudioTheme.Background,
            Padding=new Padding(Dpi.Px(32),Dpi.Px(26),Dpi.Px(32),Dpi.Px(22)),Margin=Padding.Empty};
        _studioContent.Controls.Add(page);
        var body=new TableLayoutPanel {Dock=DockStyle.Fill,AutoSize=false,AutoScroll=false,ColumnCount=1,RowCount=8,Margin=Padding.Empty};
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        int[] compactHeights={100,200,17,32,68,68,48};
        int[] referenceHeights={124,284,17,40,84,84,48};
        foreach(int height in compactHeights)body.RowStyles.Add(new RowStyle(SizeType.Absolute,Dpi.Px(height)));
        body.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        Action? fitDetails=null;
        void FitSections()
        {
            int minimum=compactHeights.Sum(Dpi.Px);
            int growth=Enumerable.Range(0,7).Sum(i=>Dpi.Px(referenceHeights[i])-Dpi.Px(compactHeights[i]));
            int extra=Math.Clamp(body.ClientSize.Height-minimum,0,growth);
            var heights=compactHeights.Select(Dpi.Px).ToArray();int assigned=0;
            for(int i=0;i<7;i++)
            {
                int addition=growth==0?0:extra*(Dpi.Px(referenceHeights[i])-heights[i])/growth;
                heights[i]+=addition;assigned+=addition;
            }
            for(int i=0;assigned<extra;i=(i+1)%7)
                if(heights[i]<Dpi.Px(referenceHeights[i])){heights[i]++;assigned++;}
            body.SuspendLayout();
            try{for(int i=0;i<7;i++)if(body.RowStyles[i].Height!=heights[i])body.RowStyles[i].Height=heights[i];}
            finally{body.ResumeLayout(true);}
            fitDetails?.Invoke();
        }
        body.SizeChanged+=(_,_)=>FitSections();page.Controls.Add(body);_studioHome=body;
        var tips=new ToolTip {AutoPopDelay=15000};page.Disposed+=(_,_)=>tips.Dispose();
        Label FixedText(string text,float size,bool bold,Color color)
        {
            var label=StudioText(text,size,bold,color);label.AutoSize=false;label.Dock=DockStyle.Fill;
            label.AutoEllipsis=true;label.Margin=Padding.Empty;label.TextAlign=ContentAlignment.MiddleLeft;
            label.TextChanged+=(_,_)=>tips.SetToolTip(label,label.Text);tips.SetToolTip(label,text);return label;
        }
        void ClickThrough(Control control,Action action)
        {
            control.Cursor=Cursors.Hand;control.Click+=(_,_)=>action();
            foreach(Control child in control.Controls)ClickThrough(child,action);
        }
        var header=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Margin=new Padding(0,0,0,Dpi.Px(4))};
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute,Dpi.Px(18)));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute,Dpi.Px(54)));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute,Dpi.Px(24)));
        header.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        header.Controls.Add(FixedText("CONTROLLER WORKSPACE",8,false,StudioTheme.Muted),0,0);
        header.Controls.Add(FixedText(Lang.T("随时进入状态","Ready when you are"),26,true,StudioTheme.Text),0,1);
        header.Controls.Add(FixedText(Lang.T("连接设备，调整配置，然后开始转换。","Connect a controller, tune your profile, then start."),10,false,StudioTheme.Muted),0,2);
        body.Controls.Add(header,0,0);

        var hero=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=new Padding(0,0,0,Dpi.Px(8))};
        hero.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,58));hero.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,42));
        hero.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        _studioHero=new StudioControllerHero {Dock=DockStyle.Fill,Margin=new Padding(0,0,Dpi.Px(20),0)};hero.Controls.Add(_studioHero,0,0);
        var info=UiLayout.Stack();info.Dock=DockStyle.Fill;info.Margin=new Padding(0,Dpi.Px(12),0,0);
        _studioDevice=FixedText(Lang.T("设备待检测","Device not checked"),15,true,StudioTheme.Text);
        _studioDevice.Height=Dpi.Px(30);_studioDevice.Margin=new Padding(0,0,0,Dpi.Px(4));UiLayout.Add(info,_studioDevice);
        _controllerSource=FixedText(Lang.T("实体手柄 · 标准转换链路","Physical controller · standard conversion"),8,false,StudioTheme.Muted);_controllerSource.Height=Dpi.Px(28);
        _controllerSource.Margin=new Padding(0,0,0,Dpi.Px(4));UiLayout.Add(info,_controllerSource);
        _studioState=FixedText("",10,false,StudioTheme.Muted);_studioState.Height=Dpi.Px(32);
        _studioState.Margin=new Padding(0,0,0,Dpi.Px(6));UiLayout.Add(info,_studioState);
        btnToggle.AutoSize=false;btnToggle.Height=Dpi.Px(46);btnToggle.Font=StudioTheme.Font(11,FontStyle.Bold);
        btnToggle.Margin=new Padding(0,Dpi.Px(8),0,Dpi.Px(8));Assets.StylePrimary(btnToggle);UiLayout.Add(info,btnToggle);
        var diagnostic=StudioAction(Lang.T("设备检测","Device check"),OpenDiag);diagnostic.AutoSize=false;
        diagnostic.MinimumSize=new Size(0,Dpi.Px(36));diagnostic.Height=Dpi.Px(36);diagnostic.Margin=Padding.Empty;
        UiLayout.Add(info,diagnostic);hero.Controls.Add(info,1,0);body.Controls.Add(hero,0,1);
        body.Controls.Add(new Panel {Anchor=AnchorStyles.Left|AnchorStyles.Right,Height=Dpi.Px(1),BackColor=StudioTheme.Border,Margin=Padding.Empty},0,2);

        var profileHead=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};
        profileHead.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));profileHead.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        profileHead.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        profileHead.Controls.Add(FixedText(Lang.T("当前配置","CURRENT PROFILE"),11,true,StudioTheme.Text),0,0);
        var manage=StudioAction(Lang.T("管理配置 →","Manage profile →"),()=>ShowStudioSection(6));
        manage.MinimumSize=new Size(Dpi.Px(100),Dpi.Px(28));manage.Padding=new Padding(Dpi.Px(8),0,Dpi.Px(8),0);
        manage.Margin=Padding.Empty;manage.Anchor=AnchorStyles.Right;profileHead.Controls.Add(manage,1,0);body.Controls.Add(profileHead,0,3);

        // Original full-width profile card, with the three parameters below it.
        var profile=new StudioCardTable {Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,BackColor=StudioTheme.Surface,
            Padding=new Padding(Dpi.Px(12)),Margin=new Padding(0,0,0,Dpi.Px(8))};
        profile.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,Dpi.Px(46)));
        profile.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));profile.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,Dpi.Px(102)));
        profile.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        profile.Controls.Add(new StudioBrand {Size=new Size(Dpi.Px(34),Dpi.Px(34)),Anchor=AnchorStyles.None,Margin=new Padding(0,0,Dpi.Px(10),0)},0,0);
        var currentText=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty};
        currentText.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        currentText.RowStyles.Add(new RowStyle(SizeType.Percent,50));currentText.RowStyles.Add(new RowStyle(SizeType.Percent,50));
        currentText.Controls.Add(FixedText(Lang.T("我的常用配置","My current profile"),11,true,StudioTheme.Text),0,0);
        _studioProfile=FixedText("",9,false,StudioTheme.Muted);currentText.Controls.Add(_studioProfile,0,1);
        ClickThrough(currentText,()=>ShowStudioSection(1));profile.Controls.Add(currentText,1,0);
        var edit=StudioAction(Lang.T("编辑 →","Edit →"),()=>ShowStudioSection(1));edit.AutoSize=false;edit.Size=new Size(Dpi.Px(96),Dpi.Px(36));
        edit.MinimumSize=new Size(0,Dpi.Px(32));edit.Margin=Padding.Empty;edit.Anchor=AnchorStyles.Right;profile.Controls.Add(edit,2,0);body.Controls.Add(profile,0,4);

        var stats=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=new Padding(0,0,0,Dpi.Px(4))};
        for(int i=0;i<3;i++)stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/3));stats.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        Label Stat(int column,string title,Action action)
        {
            var cell=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty};
            cell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            cell.RowStyles.Add(new RowStyle(SizeType.Absolute,Dpi.Px(24)));cell.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            if(column>0)cell.Paint+=(_,e)=>{using var pen=new Pen(StudioTheme.Border);e.Graphics.DrawLine(pen,0,Dpi.Px(8),0,Math.Max(Dpi.Px(8),cell.Height-Dpi.Px(8)));};
            void FitCell()
            {
                int space=Math.Max(0,(cell.ClientSize.Height-Dpi.Px(64))/2);
                var padding=new Padding(Dpi.Px(12),space,Dpi.Px(12),space);if(cell.Padding!=padding)cell.Padding=padding;
            }
            cell.SizeChanged+=(_,_)=>FitCell();
            var caption=FixedText(title,9,false,StudioTheme.Muted);caption.TextAlign=ContentAlignment.MiddleCenter;
            var value=FixedText("",18,true,StudioTheme.Text);value.TextAlign=ContentAlignment.MiddleCenter;
            cell.Controls.Add(caption,0,0);cell.Controls.Add(value,0,1);ClickThrough(cell,action);stats.Controls.Add(cell,column,0);FitCell();return value;
        }
        _studioTier=Stat(0,Lang.T("灵敏度","Sensitivity"),()=>ShowStudioSection(1));
        _studioAds=Stat(1,Lang.T("开镜倍率","ADS multiplier"),()=>ShowStudioSection(1));
        _studioMove=Stat(2,Lang.T("移动方案","Movement"),()=>ShowStudioSection(3));body.Controls.Add(stats,0,5);
        var quick=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};
        quick.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));quick.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));quick.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var feel=StudioAction(Lang.T("调整手感 →","Tune response →"),()=>ShowStudioSection(1));
        var keys=StudioAction(Lang.T("自定义键位 →","Customize keys →"),()=>ShowStudioSection(2));
        foreach(var button in new[]{feel,keys}){button.AutoSize=false;button.Dock=DockStyle.Fill;button.MinimumSize=new Size(0,Dpi.Px(36));}
        feel.Margin=new Padding(0,Dpi.Px(2),Dpi.Px(6),Dpi.Px(2));keys.Margin=new Padding(Dpi.Px(6),Dpi.Px(2),0,Dpi.Px(2));
        quick.Controls.Add(feel,0,0);quick.Controls.Add(keys,1,0);body.Controls.Add(quick,0,6);
        fitDetails=()=>{
            bool compact=body.RowStyles[1].Height<Dpi.Px(248);
            info.Margin=new Padding(0,Dpi.Px(compact?4:12),0,0);
            _controllerSource.Height=Dpi.Px(compact?20:28);_studioState.Height=Dpi.Px(compact?24:32);
            btnToggle.Height=Dpi.Px(compact?42:46);btnToggle.Margin=new Padding(0,Dpi.Px(compact?4:8),0,Dpi.Px(compact?6:8));
            profile.Padding=new Padding(Dpi.Px(compact?8:12));profile.Margin=new Padding(0,0,0,Dpi.Px(compact?4:8));
        };
        UiLayout.FitText(body);FitSections();
    }
}
