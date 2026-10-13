using System.Runtime.InteropServices;
namespace CODMControllerProbe;

public partial class MainForm
{
    private TableLayoutPanel? _studioShell;
    private Panel _studioContent=null!;
    private readonly List<StudioNav> _studioNav=new();
    private readonly List<Form> _studioForms=new();
    private UiStack? _studioHome;
    private Label? _studioDevice,_studioTier,_studioAds,_studioMove,_studioProfile;
    private int _studioSection;
    private bool _studioNavigating,_studioLang,_studioSync;
    private StudioSlider? _studioSensitivity,_studioAdsSlider;
    private StudioNumber? _studioSensitivityNumber,_studioAdsNumber;
    private StudioCurve? _studioGraph;
    private StudioControllerHero? _studioHero;
    private Label? _studioState;
    private readonly ControllerDiscovery _controllerDiscovery=new();
    private readonly System.Windows.Forms.Timer _studioRefresh=new(){Interval=500};
    private long _nextDiscovery;

    private int _studioStick=1;
    private readonly List<(Button Button,string Key)> _studioCurveButtons=new();
    private static string[] StudioSections=>new[]{Lang.T("总览","Overview"),Lang.T("手感调校","Feel tuning"),Lang.T("键位映射","Key mapping"),Lang.T("移动与奔跑","Move & sprint"),Lang.T("光标与交互","Cursor & holds"),Lang.T("震动与语音","Rumble & voice"),Lang.T("工具与配置","Tools & config"),Lang.T("实验性功能","Experimental")};
    private void BuildHomeShell()
    {
        Controls.Clear();BackgroundImage=null;BackColor=StudioTheme.Background;FormBorderStyle=FormBorderStyle.None;
        Padding=new Padding(Dpi.Px(1));AutoScaleMode=AutoScaleMode.Dpi;DoubleBuffered=true;
        ClientSize=new Size(Math.Min(Dpi.Px(1120),Screen.FromControl(this).WorkingArea.Width-Dpi.Px(30)),Math.Min(Dpi.Px(800),Screen.FromControl(this).WorkingArea.Height-Dpi.Px(40)));
        MinimumSize=new Size(Dpi.Px(900),Dpi.Px(640));_studioLang=Lang.En;
        _studioShell=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=2,Margin=Padding.Empty};
        _studioShell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,Dpi.Px(208)));_studioShell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        _studioShell.RowStyles.Add(new RowStyle(SizeType.Absolute,Dpi.Px(56)));_studioShell.RowStyles.Add(new RowStyle(SizeType.Percent,100));Controls.Add(_studioShell);
        var caption=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,BackColor=StudioTheme.Sidebar,Margin=Padding.Empty,Padding=new Padding(Dpi.Px(20),Dpi.Px(8),Dpi.Px(10),Dpi.Px(8))};
        caption.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,Dpi.Px(40)));caption.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));caption.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        caption.Controls.Add(new StudioBrand{Dock=DockStyle.Fill,Margin=new Padding(0,Dpi.Px(4),Dpi.Px(8),Dpi.Px(4))},0,0);
        _lblTitle.Dock=DockStyle.Fill;_lblTitle.Font=StudioTheme.Font(11,FontStyle.Bold);_lblTitle.ForeColor=StudioTheme.Text;_lblTitle.Margin=Padding.Empty;_lblTitle.TextAlign=ContentAlignment.MiddleLeft;caption.Controls.Add(_lblTitle,1,0);
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,WrapContents=false,Margin=Padding.Empty};
        foreach(var button in new[]{btnLang,btnVersion}){button.AutoSize=false;button.Size=new Size(Dpi.Px(button==btnLang?64:82),Dpi.Px(36));Assets.StyleOutline(button);actions.Controls.Add(button);}btnVersion.Click+=(_,_)=>OpenUnlock();
        foreach(var item in new[]{("—",(Action)(()=>WindowState=FormWindowState.Minimized)),("□",()=>WindowState=WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized),("×",()=>Close())}) {
            var b=new StudioButton{Text=item.Item1,Size=new Size(Dpi.Px(38),Dpi.Px(36)),Font=new Font("Segoe UI",12),Margin=new Padding(Dpi.Px(5),0,0,0),AccessibleName=item.Item1=="×"?"Close":item.Item1=="□"?"Maximize":"Minimize"};Assets.StyleOutline(b);b.Click+=(_,_)=>item.Item2();actions.Controls.Add(b);
        }
        caption.Controls.Add(actions,2,0);_studioShell.Controls.Add(caption,0,0);_studioShell.SetColumnSpan(caption,2);
        void Drag(object? _,MouseEventArgs e){if(e.Button!=MouseButtons.Left)return;ReleaseCapture();StudioSendMessage(Handle,0xA1,2,0);}
        caption.MouseDown+=Drag;_lblTitle.MouseDown+=Drag;caption.DoubleClick+=(_,_)=>WindowState=WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized;
        var sidebar=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Margin=Padding.Empty,BackColor=StudioTheme.Sidebar,Padding=new Padding(0,Dpi.Px(24),0,Dpi.Px(20))};
        sidebar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));sidebar.RowStyles.Add(new RowStyle(SizeType.AutoSize));sidebar.RowStyles.Add(new RowStyle(SizeType.Percent,100));sidebar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var nav=UiLayout.Stack();nav.BackColor=StudioTheme.Sidebar;
        for(int i=0;i<StudioSections.Length;i++){int section=i;var b=new StudioNav{Text=StudioSections[i],Symbol=i,Font=StudioTheme.Font(10),Height=Dpi.Px(48),Margin=new Padding(0,0,0,Dpi.Px(4)),AccessibleName=StudioSections[i]};b.Click+=(_,_)=>ShowStudioSection(section);_studioNav.Add(b);UiLayout.Add(nav,b);}
        sidebar.Controls.Add(nav,0,0);var signature=UiLayout.Stack(22);signature.BackColor=StudioTheme.Sidebar;UiLayout.Add(signature,StudioText("B U R R I T O",9,true,StudioTheme.Muted));UiLayout.Add(signature,StudioText(Lang.T("控制，从容一点","Control, with confidence"),8,false,StudioTheme.Muted));sidebar.Controls.Add(signature,0,2);_studioShell.Controls.Add(sidebar,0,1);
        _studioContent=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty,BackColor=StudioTheme.Background};_studioShell.Controls.Add(_studioContent,1,1);
        lblStatus.TextChanged+=(_,_)=>RefreshStudioState();_lblSummary.TextChanged+=(_,_)=>RefreshStudioState();ShowStudioSection(0);UiLayout.FitText(_studioShell);FormClosing+=(_,_)=>CloseStudioForms();
        _studioRefresh.Tick+=(_,_)=>{
            if(Environment.TickCount64>=_nextDiscovery){_nextDiscovery=Environment.TickCount64+1200;_controllerDiscovery.Refresh();}
            if(!_running){_gameKeys.Pump();_wasdShiftHold.Pump();}CheckNewPhysicalDevices();RefreshStudioState();
        };
        Shown+=(_,_)=>{_controllerDiscovery.Refresh();_studioRefresh.Start();};
        FormClosed+=(_,_)=>{_studioRefresh.Stop();_studioRefresh.Dispose();_controllerDiscovery.Dispose();};
        Paint+=(_,e)=>{using var border=new Pen(StudioTheme.Border);e.Graphics.DrawRectangle(border,0,0,Width-1,Height-1);};
    }
    private static Label StudioText(string text,float size=10,bool bold=false,Color? color=null)=>new(){Text=text,AutoSize=true,Font=StudioTheme.Font(size,bold?FontStyle.Bold:FontStyle.Regular),ForeColor=color??StudioTheme.Text,BackColor=Color.Transparent,Margin=new Padding(0,0,0,Dpi.Px(8))};
    private static Button StudioAction(string text,Action action,bool primary=false){var b=new StudioButton{Text=text,AutoSize=true,MinimumSize=new Size(Dpi.Px(100),Dpi.Px(40)),Padding=new Padding(Dpi.Px(12),Dpi.Px(6),Dpi.Px(12),Dpi.Px(6)),Font=StudioTheme.Font(10,FontStyle.Bold),Margin=new Padding(0,0,Dpi.Px(10),Dpi.Px(8))};if(primary)Assets.StylePrimary(b);else Assets.StyleOutline(b);b.Click+=(_,_)=>action();return b;}
    private static Panel Divider()=>new(){Height=Dpi.Px(1),BackColor=StudioTheme.Border,Margin=new Padding(0,Dpi.Px(14),0,Dpi.Px(18))};
    private void CloseStudioForms(){bool was=_studioNavigating;_studioNavigating=true;foreach(var f in _studioForms.ToArray().Reverse())if(!f.IsDisposed){f.Close();f.Dispose();}_studioForms.Clear();_studioNavigating=was;}
    internal void ShowStudioSection(int section)
    {
        if(_studioShell==null)return;_studioNavigating=true;CloseStudioForms();
        // Preserve the status and Start button: the input loop continues to update these objects.
        foreach(var c in new Control[]{lblStatus,lblHidHide,btnToggle})if(c.Parent!=null)c.Parent.Controls.Remove(c);
        foreach(Control c in _studioContent.Controls.Cast<Control>().ToArray()){_studioContent.Controls.Remove(c);c.Dispose();}
        _studioHome=null;_studioHero=null;_studioState=null;_studioSensitivity=null;_studioAdsSlider=null;_studioGraph=null;_studioCurveButtons.Clear();_studioSection=Math.Clamp(section,0,7);
        foreach(var b in _studioNav){b.Selected=_studioNav.IndexOf(b)==_studioSection;b.Invalidate();}_studioNavigating=false;
        if(_studioSection==0)BuildStudioOverview();else if(_studioSection==1)BuildStudioFeel();else OpenSettings(_studioSection-1);RefreshStudioState();
    }
    private UiStack StudioPage(string title,string eyebrow,string hint="")
    {
        var page=new Panel{Dock=DockStyle.Fill,AutoScroll=true,BackColor=StudioTheme.Background,Padding=new Padding(Dpi.Px(32),Dpi.Px(26),Dpi.Px(32),Dpi.Px(22))};_studioContent.Controls.Add(page);var body=UiLayout.Stack();page.Controls.Add(body);UiLayout.Add(body,StudioText(eyebrow,8,false,StudioTheme.Muted));var heading=StudioText(title,26,true);heading.Margin=new Padding(0,Dpi.Px(4),0,Dpi.Px(8));UiLayout.Add(body,heading);
        if(hint.Length>0){var note=StudioText(hint,10,false,StudioTheme.Muted);note.Margin=new Padding(0,0,0,Dpi.Px(22));UiLayout.Add(body,note);}page.SizeChanged+=(_,_)=>UiLayout.FitText(body);return body;
    }
    private void BuildStudioOverview()
    {
        var body=StudioPage(Lang.T("随时进入状态","Ready when you are"),"CONTROLLER WORKSPACE",Lang.T("连接设备，调整配置，然后开始转换。","Connect a controller, tune your profile, then start."));_studioHome=body;
        var hero=new TableLayoutPanel{ColumnCount=2,RowCount=1,Dock=DockStyle.Top,Height=Dpi.Px(236),Margin=new Padding(0,0,0,Dpi.Px(10))};hero.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,58));hero.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,42));hero.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        _studioHero=new StudioControllerHero{Dock=DockStyle.Fill,Margin=new Padding(0,0,Dpi.Px(20),0)};hero.Controls.Add(_studioHero,0,0);
        var info=UiLayout.Stack();info.Margin=new Padding(0,Dpi.Px(12),0,0);_studioDevice=StudioText(Lang.T("设备待检测","Device not checked"),15,true);UiLayout.Add(info,_studioDevice);
        _studioState=StudioText("",10,false,StudioTheme.Muted);_studioState.Margin=new Padding(0,Dpi.Px(4),0,Dpi.Px(12));UiLayout.Add(info,_studioState);
        btnToggle.AutoSize=false;btnToggle.Height=Dpi.Px(46);btnToggle.Font=StudioTheme.Font(11,FontStyle.Bold);btnToggle.Margin=new Padding(0,Dpi.Px(12),0,Dpi.Px(10));Assets.StylePrimary(btnToggle);UiLayout.Add(info,btnToggle);UiLayout.Add(info,StudioAction(Lang.T("设备检测","Device check"),OpenDiag));hero.Controls.Add(info,1,0);UiLayout.Add(body,hero);UiLayout.Add(body,Divider());
        var profileHead=new TableLayoutPanel{AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=2,RowCount=1,AutoSize=false,Height=Dpi.Px(44),Dock=DockStyle.Top};profileHead.RowStyles.Add(new RowStyle(SizeType.AutoSize));profileHead.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));profileHead.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));profileHead.Controls.Add(StudioText(Lang.T("当前配置","CURRENT PROFILE"),11,true),0,0);profileHead.Controls.Add(StudioAction(Lang.T("管理配置 →","Manage profile →"),()=>ShowStudioSection(6)),1,0);UiLayout.Add(body,profileHead);
        var profile=new StudioCardTable{AutoSizeMode=AutoSizeMode.GrowAndShrink,AutoSize=false,Height=Dpi.Px(82),Dock=DockStyle.Top,ColumnCount=3,RowCount=1,BackColor=StudioTheme.Surface,Padding=new Padding(Dpi.Px(16)),Margin=new Padding(0,0,0,Dpi.Px(18))};profile.RowStyles.Add(new RowStyle(SizeType.AutoSize));profile.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,Dpi.Px(46)));profile.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));profile.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));profile.Controls.Add(new StudioBrand{Size=new Size(Dpi.Px(34),Dpi.Px(34)),Margin=new Padding(0,Dpi.Px(6),Dpi.Px(12),0)},0,0);
        var profileText=UiLayout.Stack();UiLayout.Add(profileText,StudioText(Lang.T("我的常用配置","My current profile"),11,true));_studioProfile=StudioText("",9,false,StudioTheme.Muted);UiLayout.Add(profileText,_studioProfile);profile.Controls.Add(profileText,1,0);profile.Controls.Add(StudioAction(Lang.T("编辑 →","Edit →"),()=>ShowStudioSection(1)),2,0);UiLayout.Add(body,profile);
        var stats=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=false,Height=Dpi.Px(78),ColumnCount=3,RowCount=1,Margin=new Padding(0,0,0,Dpi.Px(20))};
        stats.RowStyles.Add(new RowStyle(SizeType.Percent,100));for(int i=0;i<3;i++)stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/3));
        Label Stat(int i,string title){var stack=UiLayout.Stack();stack.HasLeftDivider=i>0;stack.Padding=new Padding(Dpi.Px(12),Dpi.Px(6),Dpi.Px(12),0);
            var caption=StudioText(title,9,false,StudioTheme.Muted);caption.AutoSize=false;caption.Height=Dpi.Px(24);caption.TextAlign=ContentAlignment.MiddleCenter;UiLayout.Add(stack,caption);
            var value=StudioText("",18,true);value.AutoSize=false;value.Height=Dpi.Px(36);value.TextAlign=ContentAlignment.MiddleCenter;UiLayout.Add(stack,value);stats.Controls.Add(stack,i,0);return value;}
        stats.CellPaint+=(_,e)=>{if(e.Column>0){using var pen=new Pen(StudioTheme.Border);e.Graphics.DrawLine(pen,e.CellBounds.Left,e.CellBounds.Top+Dpi.Px(12),e.CellBounds.Left,e.CellBounds.Bottom-Dpi.Px(12));}};
        _studioTier=Stat(0,Lang.T("灵敏度","Sensitivity"));_studioAds=Stat(1,Lang.T("开镜倍率","ADS multiplier"));_studioMove=Stat(2,Lang.T("移动方案","Movement"));UiLayout.Add(body,stats);
        var quick=new TableLayoutPanel{AutoSizeMode=AutoSizeMode.GrowAndShrink,AutoSize=false,Height=Dpi.Px(48),Dock=DockStyle.Top,ColumnCount=2,RowCount=1};quick.RowStyles.Add(new RowStyle(SizeType.AutoSize));quick.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));quick.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));var feel=StudioAction(Lang.T("调整手感 →","Tune response →"),()=>ShowStudioSection(1));var keys=StudioAction(Lang.T("自定义键位 →","Customize keys →"),()=>ShowStudioSection(2));feel.Dock=keys.Dock=DockStyle.Fill;quick.Controls.Add(feel,0,0);quick.Controls.Add(keys,1,0);UiLayout.Add(body,quick);
        UiLayout.FitText(body);
    }
    private void BuildStudioFeel()
    {
        var body=StudioPage(Lang.T("找到你的手感","Find your feel"),Lang.T("设置 / 手感调校","SETTINGS / FEEL"),Lang.T("查看腰射与开镜响应，调整即时生效并自动保存。","Review hip and ADS response. Changes apply and save automatically."));
        var tabs=new TableLayoutPanel{AutoSizeMode=AutoSizeMode.GrowAndShrink,AutoSize=false,Height=Dpi.Px(48),Dock=DockStyle.Top,ColumnCount=2,RowCount=1,Margin=new Padding(0,0,0,Dpi.Px(18))};tabs.RowStyles.Add(new RowStyle(SizeType.AutoSize));tabs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));tabs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));Button? hip=null,ads=null;
        void Select(int index){_studioStick=index;if(hip!=null)Assets.StyleCapsule(hip,index==1,false);if(ads!=null)Assets.StyleCapsule(ads,index==2,false);_studioGraph?.Invalidate();}
        hip=StudioAction(Lang.T("腰射","Hip fire"),()=>Select(1));ads=StudioAction(Lang.T("开镜","ADS"),()=>Select(2));hip.Dock=ads.Dock=DockStyle.Fill;tabs.Controls.Add(hip,0,0);tabs.Controls.Add(ads,1,0);UiLayout.Add(body,tabs);Select(_studioStick);
        var card=UiLayout.Stack(16);card.BackColor=StudioTheme.Surface;card.IsCard=true;card.Margin=new Padding(0,0,0,Dpi.Px(16));var graphHead=new TableLayoutPanel{AutoSizeMode=AutoSizeMode.GrowAndShrink,AutoSize=false,Height=Dpi.Px(48),Dock=DockStyle.Top,ColumnCount=3,RowCount=1};graphHead.RowStyles.Add(new RowStyle(SizeType.AutoSize));graphHead.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));graphHead.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));graphHead.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));graphHead.Controls.Add(StudioText(Lang.T("响应曲线","Response curve"),11,true),0,0);graphHead.Controls.Add(StudioAction(Lang.T("恢复默认","Restore defaults"),RestoreStudioFeelDefaults),1,0);graphHead.Controls.Add(StudioAction(Lang.T("编辑曲线 →","Edit curve →"),OpenFeelForm),2,0);UiLayout.Add(card,graphHead);
        var graphRow=new TableLayoutPanel{Dock=DockStyle.Top,ColumnCount=2,RowCount=1,Height=Dpi.Px(160)};graphRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));graphRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,Dpi.Px(132)));graphRow.RowStyles.Add(new RowStyle(SizeType.Percent,100));_studioGraph=new StudioCurve(()=>_studioStick==1?_curveRight:_curveRightAds){Dock=DockStyle.Fill,BackColor=StudioTheme.Surface};graphRow.Controls.Add(_studioGraph,0,0);var styles=UiLayout.Stack();styles.Margin=new Padding(Dpi.Px(12),Dpi.Px(18),0,0);
        foreach(var style in new[]{("std",Lang.T("标准","Standard")),("lin",Lang.T("线性","Linear")),("dyn",Lang.T("动态","Dynamic"))}){string key=style.Item1;var b=StudioAction(style.Item2,()=>{SwitchPack(key);RefreshStudioState();});b.MinimumSize=new Size(0,Dpi.Px(40));UiLayout.Add(styles,b);_studioCurveButtons.Add((b,key));}graphRow.Controls.Add(styles,1,0);UiLayout.Add(card,graphRow);UiLayout.Add(body,card);
        void Parameter(string title,int value,int min,int max,Action<int> changed,out StudioSlider slider,out StudioNumber number){var row=new TableLayoutPanel{AutoSizeMode=AutoSizeMode.GrowAndShrink,AutoSize=true,Dock=DockStyle.Top,ColumnCount=2,RowCount=2,Margin=new Padding(0,0,0,Dpi.Px(14))};row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,Dpi.Px(170)));row.RowStyles.Add(new RowStyle(SizeType.AutoSize));row.RowStyles.Add(new RowStyle(SizeType.AutoSize));row.Controls.Add(StudioText(title,11,true),0,0);
            var n=new StudioNumber{Minimum=min,Maximum=max,Value=Math.Clamp(value,min,max),DisplaySuffix=max==20?" / 20":"%",Dock=DockStyle.Top,Font=StudioTheme.Font(11),BackColor=StudioTheme.Surface,ForeColor=StudioTheme.Text,AccessibleName=title,Margin=new Padding(Dpi.Px(14),0,0,0)};var s=new StudioSlider{Minimum=min,Maximum=max,Value=Math.Clamp(value,min,max),Dock=DockStyle.Top,Height=Dpi.Px(34),AccessibleName=title,Margin=new Padding(0,Dpi.Px(2),0,0)};row.Controls.Add(n,1,0);row.Controls.Add(s,0,1);row.SetColumnSpan(s,2);
            bool syncing=false;void Set(int v){if(syncing||_studioSync)return;syncing=true;try{s.Value=v;n.Value=v;changed(v);RefreshStudioState();}finally{syncing=false;}}s.ValueChanged+=(_,_)=>Set(s.Value);n.ValueChanged+=(_,_)=>Set((int)n.Value);UiLayout.Add(body,row);slider=s;number=n;}
        Parameter(Lang.T("灵敏度 · 1 慢 / 20 快","Sensitivity · 1 slow / 20 fast"),_freeSensLevel+1,1,20,v=>SetSensLevelFromSettings(v-1),out _studioSensitivity,out _studioSensitivityNumber);Parameter(Lang.T("开镜倍率 · %","ADS multiplier · %"),(int)Math.Round(_adsMultiplier*100),50,150,v=>SetAdsMultiplierFromSettings(v/100f),out _studioAdsSlider,out _studioAdsNumber);
        var advanced=UiLayout.Stack();advanced.Visible=false;var expand=StudioAction(Lang.T("⌄  高级调校","⌄  Advanced tuning"),()=>{advanced.Visible=!advanced.Visible;UiLayout.FitText(body);});expand.TextAlign=ContentAlignment.MiddleLeft;UiLayout.Add(body,expand);UiLayout.Add(advanced,StudioText(Lang.T("反死区、横纵灵敏度、控制点和输出频率可在曲线编辑器中调整。","Tune anti-deadzone, axis sensitivity, control points and output rate in the curve editor."),9,false,StudioTheme.Muted));UiLayout.Add(advanced,StudioAction(Lang.T("进入完整曲线编辑 →","Open full curve editor →"),OpenFeelForm));UiLayout.Add(body,advanced);UiLayout.Add(body,StudioText(Lang.T("调整会自动保存，无需关闭页面。","Changes save automatically; you can stay on this page."),8,false,StudioTheme.Muted));UiLayout.FitText(body);
    }
    private void RefreshStudioState()
    {
        if(_studioShell==null||_studioNavigating)return;
        if(_studioHome!=null&&!_studioHome.IsDisposed){var visual=ControllerVisuals.Select(_controllerDiscovery.Devices,SonyHidPad.IsOpen?SonyHidPad.DevicePath:XboxHidPad.IsOpen?XboxHidPad.DevicePath:"",_running&&_sourceSlot>=0&&!SonyHidPad.IsOpen&&!XboxHidPad.IsOpen);
            _studioHero?.SetController(visual);_studioDevice!.Text=visual.Title;
            bool working=_running&&!_outputPaused&&_physicalSessionReady&&_ds4!=null&&visual.Connected;
            _studioState!.Text=_startingConversion?Lang.T("● 正在准备转换，请稍候","● Preparing conversion…"):
                _conversionError.Length>0?"● "+_conversionError:
                _vigemBusMissing?Lang.T("● 转换暂不可用 · 请检查设备","● Conversion unavailable · check device"):
                !visual.Connected?Lang.T("● 等待连接手柄","● Waiting for a controller"):
                working?Lang.T("● 转换已成功启动","● Conversion started successfully"):
                _running?Lang.T("● 已连接 · 等待有效输入","● Connected · waiting for valid input"):
                Lang.T("● 已连接 · 可以开始转换","● Connected · ready to start");
            _studioState.ForeColor=_conversionError.Length>0?Assets.ErrRed:working?Assets.OkGreen:visual.Connected?StudioTheme.Accent:StudioTheme.Muted;_studioTier!.Text=$"{_freeSensLevel+1} / 20";_studioAds!.Text=$"{Math.Round(_adsMultiplier*100)}%";_studioMove!.Text=_expWasdMove?"WASD":Lang.T("原生摇杆","Analog");string style=_activePackKey.EndsWith("dyn")?Lang.T("动态曲线","Dynamic"):_activePackKey.EndsWith("std")?Lang.T("标准曲线","Standard"):Lang.T("线性曲线","Linear");_studioProfile!.Text=(_nativeInput?Lang.T("手柄原生","Pad native"):Lang.T("键鼠","Keyboard / mouse"))+" · "+style;}
        if(_studioSensitivity!=null&&!_studioSensitivity.IsDisposed){_studioSync=true;try{_studioSensitivity.Value=_freeSensLevel+1;_studioSensitivityNumber!.Value=_freeSensLevel+1;_studioAdsSlider!.Value=(int)Math.Round(_adsMultiplier*100);_studioAdsNumber!.Value=_studioAdsSlider.Value;}finally{_studioSync=false;}foreach(var (b,key) in _studioCurveButtons)Assets.StyleCapsule(b,_activePackKey.EndsWith(key),false);_studioGraph?.Invalidate();}
    }
    private void ReflowHome(){if(_studioShell==null)return;for(int i=0;i<_studioNav.Count;i++)_studioNav[i].Text=StudioSections[i];if(_studioLang!=Lang.En){_studioLang=Lang.En;BeginInvoke(()=>ShowStudioSection(_studioSection));}RefreshStudioState();UiLayout.FitText(_studioContent);}
    private void RestoreStudioFeelDefaults()
    {
        var defaults=AppSettings.CreateDefault();AppSettings.EnsurePacks(defaults);
        var pack=defaults.Packs.FirstOrDefault(p=>p.Key==_activePackKey)??defaults.Packs.First();
        _masterLeft.LoadShapeFrom(pack.Left.ToCurve(),1f);_masterRight.LoadShapeFrom(pack.Right.ToCurve(),1f);_masterRightAds.LoadShapeFrom(pack.RightAds.ToCurve(),1f);
        _freeSensLevel=defaults.FreeSensLevel;_adsMultiplier=defaults.AdsMultiplier;_fineTuneGains=defaults.FineTuneGains;
        SyncMasterToPack();DeriveAllFromMaster();SaveSettingsNow();RefreshSummary();RefreshStudioState();
    }
    private void RequestStudioReset()
    {
        var confirm=new Form{Text=Lang.T("恢复默认设置","Restore defaults"),Font=StudioTheme.Font()};
        var body=UiLayout.Stack(24);
        UiLayout.Add(body,StudioText(Lang.T("恢复出厂调校？","Restore factory tuning?"),20,true));
        UiLayout.Add(body,StudioText(Lang.T("手感、灵敏度和键位将恢复默认。当前授权与自定义语音库会保留。","Feel, sensitivity and keys will reset. Your license and custom voice library are kept."),11,false,StudioTheme.Muted));
        UiLayout.Add(body,StudioAction(Lang.T("确认恢复默认","Confirm reset"),()=>{ResetToDefaults();confirm.Close();},true));
        UiLayout.Add(body,StudioAction(Lang.T("保留当前设置","Keep current settings"),()=>confirm.Close()));
        confirm.Controls.Add(body);MountStudioForm(confirm);
    }
    internal void MountStudioForm(Form form,string? title=null,bool root=false)
    {
        foreach(Control c in _studioContent.Controls)c.Visible=false;
        var wrapper=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty,Padding=new Padding(Dpi.Px(24),Dpi.Px(16),Dpi.Px(24),Dpi.Px(16)),BackColor=StudioTheme.Background};wrapper.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));wrapper.RowStyles.Add(new RowStyle(SizeType.AutoSize));wrapper.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var bar=new TableLayoutPanel{AutoSizeMode=AutoSizeMode.GrowAndShrink,Dock=DockStyle.Top,AutoSize=true,ColumnCount=2,RowCount=1,Margin=new Padding(0,0,0,Dpi.Px(12))};bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));if(!root)bar.Controls.Add(StudioAction(Lang.T("← 返回","← Back"),()=>form.Close()),0,0);var heading=StudioText(title??form.Text,root?24:16,true);heading.Margin=new Padding(root?0:Dpi.Px(12),Dpi.Px(7),0,0);bar.Controls.Add(heading,1,0);wrapper.Controls.Add(bar,0,0);
        form.TopLevel=false;form.FormBorderStyle=FormBorderStyle.None;form.ControlBox=false;form.MinimumSize=Size.Empty;form.MaximumSize=Size.Empty;form.Dock=DockStyle.Fill;form.Margin=Padding.Empty;StudioTheme.Apply(form);wrapper.Controls.Add(form,0,1);_studioContent.Controls.Add(wrapper);wrapper.BringToFront();_studioForms.Add(form);
        foreach(var button in Descendants(form).OfType<Button>().Where(b=>b.DialogResult!=DialogResult.None))button.Click+=(_,_)=>{if(!form.IsDisposed)form.Close();};
        form.FormClosed+=(_,_)=>{_studioForms.Remove(form);if(_studioNavigating||IsDisposed)return;BeginInvoke(()=>{if(IsDisposed||_studioNavigating)return;wrapper.Dispose();if(_studioForms.Count==1&&_studioForms[0] is SettingsForm)ShowStudioSection(_studioSection);else if(_studioForms.Count>0){var parent=_studioForms[^1];parent.Parent!.Visible=true;parent.Parent.BringToFront();UiLayout.FitText(parent);}else ShowStudioSection(_studioSection);RefreshSensArea();});};form.Show();UiLayout.FitText(form);
    }
    private static IEnumerable<Control> Descendants(Control root){foreach(Control child in root.Controls){yield return child;foreach(var nested in Descendants(child))yield return nested;}}
    [DllImport("user32.dll")]private static extern bool ReleaseCapture();
    [DllImport("user32.dll",EntryPoint="SendMessageW")]private static extern IntPtr StudioSendMessage(IntPtr hWnd,int msg,int wParam,int lParam);
    protected override void WndProc(ref Message m){if(m.Msg==0x84&&FormBorderStyle==FormBorderStyle.None&&WindowState==FormWindowState.Normal){base.WndProc(ref m);var pt=PointToClient(new Point(unchecked((short)(long)m.LParam),unchecked((short)((long)m.LParam>>16))));int edge=Dpi.Px(7);bool left=pt.X<edge,right=pt.X>=Width-edge,top=pt.Y<edge,bottom=pt.Y>=Height-edge;if(left||right||top||bottom)m.Result=(IntPtr)(top?(left?13:right?14:12):bottom?(left?16:right?17:15):left?10:11);return;}base.WndProc(ref m);}
}
