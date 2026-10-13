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
    private readonly bool _install,_preview,_componentOnly;
    private readonly Func<string,bool,bool,Task<Outcome>> _run;
    private readonly Func<bool> _restart;
    private readonly string _readme;
    private readonly Panel _viewport=new(){Dock=DockStyle.Fill,AutoScroll=true,Padding=new Padding(24,0,24,0),BackColor=Appearance.Background};
    private readonly Panel _document=new(){Dock=DockStyle.Fill,Visible=false,Padding=new Padding(24,8,24,8)};
    private readonly TextBox _path=new(){Dock=DockStyle.Fill,BorderStyle=BorderStyle.FixedSingle,BackColor=Appearance.Surface,ForeColor=Appearance.Text};
    private readonly CheckBox _agree=new(){Text="我已阅读必看说明，并知悉以上安装内容与安全提示",AutoSize=true,UseCompatibleTextRendering=true,ForeColor=Appearance.Text,BackColor=Appearance.Background,Margin=new Padding(0,4,0,12)};
    private readonly CheckBox _drivers=new(){Text="移除 ViGEmBus / HidHide 共享驱动",Checked=true,AutoSize=true,UseCompatibleTextRendering=true,ForeColor=Appearance.Text,BackColor=Appearance.Surface};
    private readonly CheckBox _data=new(){Text="删除当前用户的配置、授权、语音和维护记录",Checked=true,AutoSize=true,UseCompatibleTextRendering=true,ForeColor=Appearance.Text,BackColor=Appearance.Surface};
    private readonly TextBox _log=new(){Multiline=true,ReadOnly=true,TabStop=false,ScrollBars=ScrollBars.Vertical,Height=136,Dock=DockStyle.Fill,BackColor=Appearance.Background,ForeColor=Appearance.Muted,BorderStyle=BorderStyle.None,Visible=false};
    private readonly Label _status=Appearance.Label("等待开始",10,true);
    private readonly Label[] _steps=new Label[3];
    private readonly FlowLayoutPanel _footer=new(){Dock=DockStyle.Fill,WrapContents=false,Padding=new Padding(24,12,14,12)};
    private readonly WarmButton _primary=new(){Primary=true},_close=new(){Text="取消",Width=86},_read=new(){Text="必看说明",Width=104},_browse=new(){Text="选择文件夹",Width=108},_logs=new(){Text="详细记录",Width=104},_back=new(){Text="返回",Width=100,Visible=false};
    private bool _busy;
    private Outcome? _outcome;
    private TableLayoutPanel _body=null!;
    public bool CleanupCachesOnClose {get;private set;}
    public string InstallRoot=>_path.Text;
    public WizardForm(bool install,string root,string readme,Func<string,bool,bool,Task<Outcome>> run,Func<bool> restart,bool preview=false,bool componentOnly=false)
    {
        _install=install;_run=run;_restart=restart;_readme=readme;_preview=preview;_componentOnly=componentOnly;_path.ReadOnly=componentOnly;_browse.Visible=!componentOnly;
        Text=Product.Name+(componentOnly?" · 组件准备":install?" · 安装":" · 卸载");ClientSize=new(920,740);MinimumSize=new(840,650);StartPosition=FormStartPosition.CenterScreen;BackColor=Appearance.Background;ForeColor=Appearance.Text;Font=new("Microsoft YaHei UI",9.5f);MaximizeBox=false;
        var shell=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=3,BackColor=Appearance.Background,Margin=Padding.Empty};shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,202));shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));shell.RowStyles.Add(new RowStyle(SizeType.Absolute,108));shell.RowStyles.Add(new RowStyle(SizeType.Percent,100));shell.RowStyles.Add(new RowStyle(SizeType.Absolute,68));Controls.Add(shell);
        var side=new Panel{Dock=DockStyle.Fill,BackColor=Color.FromArgb(27,29,32),Padding=new Padding(22,26,20,20)};shell.Controls.Add(side,0,0);shell.SetRowSpan(side,3);var nav=Appearance.Stack(side);
        Appearance.Add(nav,Appearance.Label("BURRITO",11,true));Appearance.Add(nav,Appearance.Label("CODM\n手柄转换器",19,true));Appearance.Add(nav,Appearance.Label("v"+Product.Version,9));Appearance.Add(nav,new Panel{Height=30,Dock=DockStyle.Top});
        string[] steps=install?["检查准备","安装组件","开始使用"]:["恢复输入","移除组件","核验结果"];
        for(int i=0;i<3;i++){_steps[i]=Appearance.Label($"0{i+1}   {steps[i]}",10,i==0);_steps[i].Margin=new(0,0,0,24);Appearance.Add(nav,_steps[i]);}
        var credit=Appearance.Label("控制，从容一点。\n安装内容与清理结果\n都会在这里说清。",8.5f);credit.Dock=DockStyle.Bottom;side.Controls.Add(credit);
        var heading=new Panel{Dock=DockStyle.Fill,Padding=new(24,22,24,0)};shell.Controls.Add(heading,1,0);var title=Appearance.Stack(heading);Appearance.Add(title,Appearance.Label(install?"准备好，就开始。":"卸载，也清楚交代。",22,true));Appearance.Add(title,Appearance.Label(install?"自选安装位置 · 文件与驱动逐项说明":"先退出游戏和转换器，再移除所选项目",9.5f));
        var host=new Panel{Dock=DockStyle.Fill,BackColor=Appearance.Background};host.Controls.Add(_viewport);host.Controls.Add(_document);shell.Controls.Add(host,1,1);_body=Appearance.Stack(_viewport);_path.Text=root;
        BuildBody();shell.Controls.Add(_footer,1,2);foreach(var b in new[]{_primary,_close,_read,_logs,_back})_footer.Controls.Add(b);
        _primary.Text=componentOnly?"准备组件":install?"开始安装":"开始卸载";_primary.Enabled=!install;_agree.CheckedChanged+=(_,_)=>{if(!_busy)_primary.Enabled=_agree.Checked;};
        _primary.Click+=async(_,_)=>await Execute();_close.Click+=(_,_)=>Close();_read.Click+=(_,_)=>ShowReadme();_back.Click+=(_,_)=>HideReadme();_logs.Click+=(_,_)=>{if(_outcome!=null)SaveLog();else{_log.Visible=!_log.Visible;Reflow();if(_log.Visible)_viewport.ScrollControlIntoView(_log);}};
        _browse.Click+=(_,_)=>{using var picker=new FolderBrowserDialog{Description="选择父文件夹，软件会使用独立的 CODM手柄转换器 子文件夹",SelectedPath=_path.Text,UseDescriptionForTitle=true};if(picker.ShowDialog(this)==DialogResult.OK){string chosen=picker.SelectedPath;_path.Text=Path.GetFileName(chosen)=="CODM手柄转换器"?chosen:Path.Combine(chosen,"CODM手柄转换器");}};
        _viewport.SizeChanged+=(_,_)=>Reflow();Shown+=(_,_)=>{Reflow();if(_preview)_status.Text="界面预览，不执行安装或卸载";};FormClosing+=(_,e)=>{if(_busy&&e.CloseReason!=CloseReason.WindowsShutDown)e.Cancel=true;};
    }
    protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);int value=1;DwmSetWindowAttribute(Handle,20,ref value,sizeof(int));}
    [DllImport("dwmapi.dll")]private static extern int DwmSetWindowAttribute(IntPtr window,int attr,ref int value,int size);
    private void BuildBody()
    {
        if(_install)
        {
            var place=Appearance.Card(_body,"安装位置");var row=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=2,Margin=new Padding(0,3,0,2)};row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,124));_path.Margin=new(0,8,10,6);row.Controls.Add(_path,0,0);row.Controls.Add(_browse,1,0);Appearance.Add(place,row);Appearance.Add(place,Appearance.Label("请使用独立子文件夹；配置不会在安装时重置。",9));
            var items=Appearance.Card(_body,_componentOnly?"免安装包已包含":"本次安装包含");Appearance.Add(items,Appearance.Label("主程序 v"+Product.Version+"、卸载程序、必看 README.TXT、赞赏码原图。",9.5f));Appearance.Add(items,Appearance.Label("ViGEmBus：虚拟手柄输出　·　HidHide：避免实体输入与改键重复",9.5f));Appearance.Add(items,Appearance.Label(_componentOnly?"这里只准备或复用共享驱动，不移动文件、不登记应用卸载入口。":"已有就绪组件会复用。两项驱动为共享组件，卸载可能影响其他手柄软件。",9));
            var safety=Appearance.Card(_body,"安全软件提示");var warning=Appearance.Label("本安装包与主程序尚未代码签名，可能出现未知发行者或风险提示。请核验来源、发布的 SHA256 和扫描结果；不要把所有提示都当作误报，也不要关闭系统防护。",9);warning.ForeColor=Color.FromArgb(229,191,134);Appearance.Add(safety,warning);Appearance.Add(_body,_agree);
        }
        else
        {
            var root=Appearance.Card(_body,"本次卸载位置");Appearance.Add(root,Appearance.Label(_path.Text,9));
            var items=Appearance.Card(_body,"选择清理范围");Appearance.Add(items,_drivers);Appearance.Add(items,Appearance.Label("共享驱动也可能被其他手柄软件使用；取消勾选会明确列为保留项。",9));Appearance.Add(items,_data);Appearance.Add(items,Appearance.Label("删除后无法恢复。需要保留时先导出配置，或取消这一项。",9));
            var verify=Appearance.Card(_body,"完成后如何确认");Appearance.Add(verify,Appearance.Label("核对程序文件、配置、驱动服务、过滤器、官方安装项与驱动包。",9.5f));Appearance.Add(verify,Appearance.Label("被占用、失败或待重启都会分别显示。需要重启时由你选择，重启后继续复查；不提前宣称完全清理。",9));
        }
        var status=Appearance.Card(_body,"处理状态");Appearance.Add(status,_status);Appearance.Add(status,_log);
        if(_install)Appearance.Add(_body,Appearance.Label("喜欢这个工具？安装文件夹内有赞赏码.png，可自愿鼓励作者；不影响使用。",8.5f));
    }
    private void Reflow()
    {
        if(_body==null)return;_body.Width=Math.Max(1,_viewport.ClientSize.Width-_viewport.Padding.Horizontal-SystemInformation.VerticalScrollBarWidth);
        void Fit(Control parent){foreach(Control child in parent.Controls){if(child is Label {AutoSize:true}||child is CheckBox {AutoSize:true})child.MaximumSize=new(Math.Max(80,parent.ClientSize.Width-parent.Padding.Horizontal-child.Margin.Horizontal),0);Fit(child);}}
        Fit(_body);_body.PerformLayout();
    }
    public void UpdateProgress(Update update)
    {
        if(IsDisposed)return;if(InvokeRequired){BeginInvoke(()=>UpdateProgress(update));return;}
        for(int i=0;i<_steps.Length;i++)_steps[i].ForeColor=i==update.Step?Appearance.Accent:Appearance.Muted;
        _status.Text=update.Text;_log.AppendText(((_log.TextLength>0)?Environment.NewLine:"")+update.Text);_log.SelectionStart=_log.TextLength;_log.ScrollToCaret();Reflow();if(_busy)_viewport.ScrollControlIntoView(_status);
    }
    private async Task Execute()
    {
        if(_busy||_preview)return;
        if(_outcome!=null)
        {
            if(_outcome.RestartRequired){if(MessageBox.Show(this,"请先保存其他工作。确认后请求 Windows 在30秒后正常重启，不强制结束应用。","立即重启？",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)==DialogResult.Yes&&!_restart())MessageBox.Show(this,"未能请求重启，请稍后手动重启。","重启提示");return;}
            if(_install&&_outcome.Success&&_outcome.ReadyToLaunch){try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(_path.Text,Product.MainName)){UseShellExecute=true});Close();}catch(Exception ex){MessageBox.Show(this,ex.Message);}return;}
        }
        if(!_install&&MessageBox.Show(this,"将按勾选项移除程序、共享驱动和个人配置。请确认已备份需要保留的内容。","确认卸载",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
        _busy=true;_log.Visible=true;foreach(Control control in new Control[]{_primary,_close,_agree,_path,_browse,_drivers,_data,_read})control.Enabled=false;
        try{_outcome=await _run(_path.Text,_drivers.Checked,_data.Checked);ShowResult(_outcome);}
        catch(Exception ex){ShowResult(new Outcome{Summary="操作未完成："+ex.Message});}
        finally{_busy=false;_primary.Enabled=true;_close.Enabled=true;_read.Enabled=true;if(_outcome==null||(!_outcome.Success&&!_outcome.RestartRequired)){foreach(Control c in new Control[]{_agree,_path,_browse,_drivers,_data})c.Enabled=true;_primary.Enabled=!_install||_agree.Checked;}}
    }
    public void ShowResult(Outcome result)
    {
        _outcome=result;_log.Visible=true;_status.Text=result.Summary;_status.ForeColor=result.Success&&(!_install||result.ReadyToLaunch)&&!result.RestartRequired?Color.FromArgb(103,207,158):Color.FromArgb(244,173,111);
        foreach(string line in result.Details.Concat(result.Retained))_log.AppendText(Environment.NewLine+line);
        if(result.RestartRequired){_primary.Text="立即重启";_close.Text="稍后重启";_primary.Visible=true;}
        else {_primary.Text=_install&&result.Success?(result.ReadyToLaunch?"启动软件":"重新检查"):"重试";_primary.Visible=_install||!result.Success;_close.Text="完成";}
        CleanupCachesOnClose=!_install&&result.Success&&!result.RestartRequired;_logs.Text="保存记录";Reflow();_viewport.ScrollControlIntoView(_status);
        if(!_busy){_primary.Enabled=result.Success||result.RestartRequired||!_install||_agree.Checked;_close.Enabled=true;}
    }
    private void ShowReadme()
    {
        foreach(Control c in _document.Controls.Cast<Control>().ToArray())c.Dispose();_document.Controls.Clear();var text=new RichTextBox{Dock=DockStyle.Fill,ReadOnly=true,BorderStyle=BorderStyle.None,BackColor=Appearance.Surface,ForeColor=Appearance.Text,Font=new Font("Microsoft YaHei UI",9.5f),Text=_readme,DetectUrls=false};_document.Controls.Add(text);_viewport.Visible=false;_document.Visible=true;_document.BringToFront();foreach(var b in new[]{_primary,_close,_read,_logs})b.Visible=false;_back.Visible=true;
    }
    private void HideReadme(){_document.Visible=false;_viewport.Visible=true;_back.Visible=false;foreach(var b in new[]{_primary,_close,_read,_logs})b.Visible=true;if(_outcome!=null)ShowResult(_outcome);}
    private void SaveLog(){using var save=new SaveFileDialog{FileName=Product.Name+(_install?"-安装记录.txt":"-卸载记录.txt"),Filter="文本记录|*.txt"};if(save.ShowDialog(this)==DialogResult.OK)File.WriteAllText(save.FileName,_log.Text,new System.Text.UTF8Encoding(true));}
    public void SetCleanupChoices(bool drivers,bool data){_drivers.Checked=drivers;_data.Checked=data;}
    public void ScalePreviewFonts(float factor){var fonts=new List<(Control Control,Font Font)>();void Capture(Control c){fonts.Add((c,c.Font));foreach(Control child in c.Controls)Capture(child);}Capture(this);foreach(var item in fonts)item.Control.Font=new Font(item.Font.FontFamily,item.Font.Size*factor,item.Font.Style);Reflow();}
}
