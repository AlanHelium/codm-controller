using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Burrito.Installer;

public static class Appearance
{
    public static readonly Color Background=Color.FromArgb(23,25,28),Surface=Color.FromArgb(34,37,41),Border=Color.FromArgb(60,63,68),Text=Color.FromArgb(242,241,238),Muted=Color.FromArgb(167,169,172),Accent=Color.FromArgb(242,135,70);
    public static GraphicsPath Round(RectangleF r,float radius=12){var p=new GraphicsPath();float d=Math.Min(radius*2,Math.Min(r.Width,r.Height));p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
    public static Label Label(string text,float size=10,bool bold=false)=>new(){Text=text,AutoSize=true,Dock=DockStyle.Fill,UseCompatibleTextRendering=true,Font=new Font("Microsoft YaHei UI",size,bold?FontStyle.Bold:FontStyle.Regular),ForeColor=bold?Text:Muted,BackColor=Color.Transparent,Margin=new Padding(0,0,0,8)};
    public static TableLayoutPanel Stack(Control parent,int padding=0)
    {var stack=new TableLayoutPanel{AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Dock=DockStyle.Top,ColumnCount=1,Padding=new Padding(padding),Margin=Padding.Empty,BackColor=Color.Transparent};stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));parent.Controls.Add(stack);return stack;}
    public static void Add(TableLayoutPanel stack,Control child){int row=stack.RowCount++;stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));stack.Controls.Add(child,0,row);}
    public static TableLayoutPanel Card(TableLayoutPanel body,string title)
    {
        var card=new CardPanel{AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Dock=DockStyle.Top,Padding=new Padding(16),Margin=new Padding(0,0,0,12)};
        var content=Stack(card);Add(content,Label(title,11,true));Add(body,card);return content;
    }
}
public sealed class CardPanel:Panel
{
    public CardPanel(){DoubleBuffered=true;BackColor=Appearance.Surface;SetStyle(ControlStyles.ResizeRedraw,true);}
    protected override void OnPaintBackground(PaintEventArgs e)
    {Color background=Appearance.Background;for(Control? at=Parent;at!=null;at=at.Parent)if(at.BackColor.A==255){background=at.BackColor;break;}e.Graphics.Clear(background);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using var path=Appearance.Round(new RectangleF(1,1,Width-3,Height-3));using var fill=new LinearGradientBrush(ClientRectangle,Color.FromArgb(40,43,47),Appearance.Surface,90f);e.Graphics.FillPath(fill,path);using var border=new Pen(Appearance.Border);e.Graphics.DrawPath(border,path);}
}
public sealed class WarmButton:Button
{
    [System.ComponentModel.DefaultValue(false)]public bool Primary {get;set;}
    public WarmButton(){Size=new(132,42);Margin=new(0,0,10,0);Font=new("Microsoft YaHei UI",9.5f,FontStyle.Bold);SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);}
    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics;g.Clear(Parent?.BackColor??Appearance.Background);g.SmoothingMode=SmoothingMode.AntiAlias;
        using var path=Appearance.Round(new RectangleF(1,1,Width-3,Height-3),8);
        using var brush=new SolidBrush(!Enabled?Color.FromArgb(61,63,66):Primary?Appearance.Accent:Appearance.Surface);g.FillPath(brush,path);
        using var pen=new Pen(Focused?Appearance.Accent:Appearance.Border);g.DrawPath(pen,path);
        using var text=new SolidBrush(!Enabled?Appearance.Muted:Primary?Color.FromArgb(29,26,23):Appearance.Text);using var format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center};g.DrawString(Text,Font,text,ClientRectangle,format);
    }
}
public sealed class WizardForm:Form
{
    readonly bool _install,_preview,_componentOnly,_repairMode;
    readonly Func<string,bool,bool,Task<Outcome>> _run;
    readonly Func<bool> _restart;
    readonly string _readme;
    readonly Panel _viewport=new(){Dock=DockStyle.Fill,AutoScroll=true,Padding=new Padding(26,0,26,0)};
    readonly TextBox _path=new(){Dock=DockStyle.Fill};
    readonly CheckBox _drivers=new(){Text="同时移除 ViGEmBus / HidHide 共享驱动",Checked=true,AutoSize=true};
    readonly CheckBox _data=new(){Text="同时删除个人配置与音频",Checked=true,AutoSize=true};
    readonly TextBox _log=new(){Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Height=180,Dock=DockStyle.Fill,BackColor=Appearance.Surface,ForeColor=Appearance.Text,BorderStyle=BorderStyle.None};
    readonly Label _advice=Appearance.Label("",10);
    readonly WarmButton _repairAction=new(){Text="打开修复工具",Width=136,Visible=false};
    readonly Label _status=Appearance.Label("准备就绪",12,true);
    readonly WarmButton _primary=new(){Primary=true,Width=172},_close=new(){Text="取消",Width=88};
    readonly LinkLabel _options=new(){Text="安装选项 ▾",AutoSize=true,LinkColor=Appearance.Muted,ActiveLinkColor=Appearance.Accent,Margin=new Padding(0,6,0,12)};
    readonly LinkLabel _details=new(){Text="展开详细记录 ▾",AutoSize=true,LinkColor=Appearance.Muted,ActiveLinkColor=Appearance.Accent,Margin=new Padding(0,12,0,10)};
    readonly ProgressBar _progress=new(){Dock=DockStyle.Top,Height=5,Visible=false,Style=ProgressBarStyle.Marquee};
    TableLayoutPanel _body=null!,_optionPanel=null!,_detailPanel=null!;
    bool _busy;
    Outcome? _outcome;
    public bool CleanupCachesOnClose {get;private set;}
    public string InstallRoot=>_path.Text;
    public WizardForm(bool install,string root,string readme,Func<string,bool,bool,Task<Outcome>> run,Func<bool> restart,bool preview=false,bool componentOnly=false,bool repairMode=false)
    {
        _install=install;_run=run;_restart=restart;_readme=readme;_preview=preview;_componentOnly=componentOnly;_repairMode=repairMode;_path.Text=root;
        Text=Product.Name+(_repairMode?" · 修复工具":install?" · 安装":" · 卸载");ClientSize=new(710,555);MinimumSize=new(650,510);StartPosition=FormStartPosition.CenterScreen;BackColor=Appearance.Background;ForeColor=Appearance.Text;Font=new("Microsoft YaHei UI",9.5f);MaximizeBox=false;AutoScaleMode=AutoScaleMode.Dpi;
        var shell=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Margin=Padding.Empty};shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));shell.RowStyles.Add(new RowStyle(SizeType.Absolute,108));shell.RowStyles.Add(new RowStyle(SizeType.Percent,100));shell.RowStyles.Add(new RowStyle(SizeType.Absolute,74));Controls.Add(shell);
        var header=new Panel{Dock=DockStyle.Fill,Padding=new Padding(26,22,26,0)};var h=Appearance.Stack(header);Appearance.Add(h,Appearance.Label(_repairMode?"修复软件":install?"安装 CODM 手柄转换器":"卸载 CODM 手柄转换器",20,true));Appearance.Add(h,Appearance.Label("v"+Product.Version+" · "+(_repairMode?"自动检查并处理已知问题":install?"沿用已有配置，可直接覆盖安装":"默认移除程序、个人配置及共享驱动"),9));shell.Controls.Add(header,0,0);
        shell.Controls.Add(_viewport,0,1);_body=Appearance.Stack(_viewport);BuildBody();
        var footer=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(20,14,26,12),WrapContents=false};footer.Controls.Add(_primary);footer.Controls.Add(_close);footer.Controls.Add(_repairAction);shell.Controls.Add(footer,0,2);
        _primary.Text=StartText;_primary.Click+=async(_,_)=>await Execute();_close.Click+=(_,_)=>Close();
        _repairAction.Click+=(_,_)=>{string exe=Path.Combine(_path.Text,LocalRecovery.RepairExe);try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe){UseShellExecute=true});Close();}catch(Exception ex){_advice.Text="修复工具未能启动："+ex.Message;}};
        _options.Click+=(_,_)=>{_optionPanel.Visible=!_optionPanel.Visible;_options.Text=(_install?"安装选项":"卸载选项")+(_optionPanel.Visible?" ▴":" ▾");Reflow();};
        _details.Click+=(_,_)=>{_detailPanel.Visible=!_detailPanel.Visible;_details.Text=_detailPanel.Visible?"收起详细记录 ▴":"展开详细记录 ▾";Reflow();};
        _viewport.SizeChanged+=(_,_)=>Reflow();Shown+=(_,_)=>{Reflow();if(_preview)_status.Text="界面预览";};FormClosing+=(_,e)=>{if(_busy&&e.CloseReason!=CloseReason.WindowsShutDown)e.Cancel=true;};
    }
    string StartText=>_repairMode?"开始修复":_install?(_componentOnly?"准备组件":"立即安装"):"卸载并清理";
    void BuildBody()
    {
        var intro=Appearance.Card(_body,_repairMode?"保留个人设置":_install?"一次安装，自动准备":"卸载范围");
        Appearance.Add(intro,Appearance.Label(_repairMode?"检查程序文件、驱动与白名单；修复过程保留手感、键位和音频。":_install?"自动准备必要组件，并在软件目录提供修复工具和卸载入口。":"清理程序、个人设置和两项共享驱动；共享驱动也可能被其他手柄软件使用。",10));
        if(!_install)Appearance.Add(intro,Appearance.Label("需要保留个人设置或共享驱动，可展开下方卸载选项。",9));
        if(!_repairMode)
        {
            _options.Text=_install?"安装选项 ▾":"卸载选项 ▾";Appearance.Add(_body,_options);_optionPanel=Appearance.Stack(new Panel());_optionPanel.Visible=false;Appearance.Add(_body,_optionPanel);
            if(_install){Appearance.Add(_optionPanel,Appearance.Label("安装位置",10,true));Appearance.Add(_optionPanel,_path);_path.ReadOnly=_componentOnly;var choose=new WarmButton{Text="更改位置",Width=110};choose.Enabled=!_componentOnly;choose.Click+=(_,_)=>{using var picker=new FolderBrowserDialog{SelectedPath=_path.Text};if(picker.ShowDialog(this)==DialogResult.OK)_path.Text=Path.Combine(picker.SelectedPath,"CODM手柄转换器");};Appearance.Add(_optionPanel,choose);}
            else{Appearance.Add(_optionPanel,_drivers);Appearance.Add(_optionPanel,_data);Appearance.Add(_optionPanel,Appearance.Label("个人配置删除后无法恢复。",9));}
        }
        var result=Appearance.Card(_body,"状态");Appearance.Add(result,_status);Appearance.Add(result,_advice);Appearance.Add(result,_progress);Appearance.Add(_body,_details);
        _detailPanel=Appearance.Stack(new Panel());_detailPanel.Visible=false;Appearance.Add(_body,_detailPanel);Appearance.Add(_detailPanel,_log);
        var actions=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,WrapContents=true};var save=new WarmButton{Text="保存记录",Width=106};save.Click+=(_,_)=>SaveLog();var guide=new WarmButton{Text="使用说明",Width=106};guide.Click+=(_,_)=>{using var f=new Form{Text="使用说明",Size=new Size(700,560),StartPosition=FormStartPosition.CenterParent};f.Controls.Add(new RichTextBox{Dock=DockStyle.Fill,ReadOnly=true,Text=_readme,BackColor=Appearance.Surface,ForeColor=Appearance.Text});f.ShowDialog(this);};var restartButton=new WarmButton{Text="重启电脑…",Width=114};restartButton.Click+=(_,_)=>{if(!_busy&&MessageBox.Show(this,"请先保存其他工作。确认后请求 Windows 正常重启。","重启电脑",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)==DialogResult.Yes)_restart();};actions.Controls.Add(save);actions.Controls.Add(guide);actions.Controls.Add(restartButton);Appearance.Add(_detailPanel,actions);
        Appearance.Add(_detailPanel,Appearance.Label("安装位置："+_path.Text+"\n两项驱动为共享组件。主程序与安装器尚未代码签名，请核验发布来源与校验值。",9));
    }
    void Reflow(){if(_body==null)return;_body.Width=Math.Max(1,_viewport.ClientSize.Width-_viewport.Padding.Horizontal-SystemInformation.VerticalScrollBarWidth);void Fit(Control p){foreach(Control c in p.Controls){if(c is Label {AutoSize:true}||c is CheckBox {AutoSize:true})c.MaximumSize=new Size(Math.Max(100,p.ClientSize.Width-p.Padding.Horizontal-c.Margin.Horizontal),0);Fit(c);}}Fit(_body);_body.PerformLayout();}
    public void UpdateProgress(Update u){if(IsDisposed)return;if(InvokeRequired){BeginInvoke(()=>UpdateProgress(u));return;}_log.AppendText((_log.TextLength>0?Environment.NewLine:"")+u.Text);_status.Text=u.Step==0?"正在检查，请稍候…":u.Step==2?"正在核对结果…":_repairMode?"正在修复，请稍候…":_install?"正在安装，请稍候…":"正在清理，请稍候…";}
    async Task Execute()
    {
        if(_busy||_preview)return;
        if(_outcome?.RestartRequired==true||(_repairMode&&_outcome?.ReadyToLaunch==true)||(!_install&&_outcome?.Success==true)){Close();return;}
        if(_install&&!_repairMode&&_outcome?.ReadyToLaunch==true){try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(_path.Text,Product.MainName)){UseShellExecute=true});Close();}catch(Exception ex){_status.Text="无法启动软件："+ex.Message;}return;}
        _busy=true;_primary.Enabled=false;_close.Enabled=false;_path.Enabled=false;_drivers.Enabled=false;_data.Enabled=false;_progress.Visible=true;
        try{ShowResult(await _run(_path.Text,_drivers.Checked,_data.Checked));}catch(Exception ex){ShowResult(new Outcome{Summary="操作未完成："+ex.Message});}
        finally{_busy=false;_primary.Enabled=true;_close.Enabled=true;_path.Enabled=true;_drivers.Enabled=true;_data.Enabled=true;_progress.Visible=false;}
    }
    public void ShowResult(Outcome r)
    {
        _outcome=r;_advice.Text=RecoveryAdvice.For(r);_repairAction.Visible=!r.Success&&!r.RestartRequired&&File.Exists(Path.Combine(_path.Text,LocalRecovery.RepairExe));
        _status.Text=!r.Success&&!r.RestartRequired?(_repairMode?"修复尚未完成":_install?"安装尚未完成":"清理尚未完成"):r.RestartRequired?"已处理可完成的项目。Windows 尚需一次重启，请保存工作后手动重启。":r.Summary;
        _status.ForeColor=r.Success&&!r.RestartRequired?Color.FromArgb(103,207,158):Color.FromArgb(244,173,111);
        _log.Text=string.Join(Environment.NewLine,_log.Lines.Append(r.Summary).Concat(r.Details).Concat(r.Retained).Where(s=>s.Length>0).Distinct());
        _primary.Text=r.RestartRequired?"完成":_repairMode?(r.ReadyToLaunch?"完成":"开始修复"):_install?(r.ReadyToLaunch?"完成并打开":"重试安装"):(r.Success?"完成":"重试清理");_close.Text="关闭";
        CleanupCachesOnClose=!_install&&r.Success&&!r.RestartRequired;
        if(!r.Success&&!_detailPanel.Visible)_details.Text="查看失败原因与详细记录 ▾";
        if(!_busy){_primary.Enabled=true;_close.Enabled=true;}Reflow();
    }
    public void SetRepairChecking(){_primary.Enabled=false;_status.Text="正在检查，请稍候…";}
    void SaveLog(){using var save=new SaveFileDialog{FileName="CODM-"+(_repairMode?"修复":_install?"安装":"卸载")+"记录.txt",Filter="文本记录|*.txt"};if(save.ShowDialog(this)==DialogResult.OK)File.WriteAllText(save.FileName,_log.Text,new System.Text.UTF8Encoding(true));}
    public void SetCleanupChoices(bool drivers,bool data){_drivers.Checked=drivers;_data.Checked=data;}
    public void ScalePreviewFonts(float factor){Font=new Font(Font.FontFamily,Font.Size*factor);Reflow();}
}
