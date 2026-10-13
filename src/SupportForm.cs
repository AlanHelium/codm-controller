namespace CODMControllerProbe;

public sealed class SupportForm:Form
{
    public SupportForm()
    {
        Text=Lang.T("支持作者","Support the author");BackColor=StudioTheme.Background;ForeColor=StudioTheme.Text;Font=StudioTheme.Font(10);ClientSize=new(740,720);AutoScaleMode=AutoScaleMode.Dpi;
        var scroll=new Panel{Dock=DockStyle.Fill,AutoScroll=true,Padding=new Padding(24)};Controls.Add(scroll);
        var stack=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=1};stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));scroll.Controls.Add(stack);
        void Add(Control control){int row=stack.RowCount++;stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));control.Margin=new Padding(0,0,0,16);stack.Controls.Add(control,0,row);}
        Label Line(string text,float size=10,bool bold=false)=>new(){Text=text,AutoSize=true,UseCompatibleTextRendering=true,Dock=DockStyle.Fill,ForeColor=bold?StudioTheme.Text:StudioTheme.Muted,Font=StudioTheme.Font(size,bold?FontStyle.Bold:FontStyle.Regular)};
        Add(Line(Lang.T("全部功能免费，支持完全自愿。","All features are free. Support is entirely voluntary."),20,true));
        Add(Line(Lang.T("这个项目花了很多时间和精力。从手柄适配、输入手感和键位映射，到界面和安装卸载，都是一点点排查和打磨出来的。","This project took a lot of time and care: controller support, input feel, mappings, the interface and installation all needed repeated testing.")));
        Add(Line(Lang.T("如果这个工具让你玩得更舒服，可以请我吃点东西：","If this tool makes your games more comfortable, you can buy me a meal:")));
        Add(Line(Lang.T("买一包速冻饺子 · 简单吃一顿\n来一份猪脚饭 · 好好吃顿饭\n猪脚饭，再加一杯瑞幸 · 吃饱再提提神","A bag of dumplings · a simple meal\nA pork rice bowl · a proper lunch\nA rice bowl and a Luckin coffee · a little extra energy"),12,true));
        Add(Line(Lang.T("扫下面的原始赞赏码，自选金额就好。没有最低金额，也没有对应的功能权限。","Scan the original QR below and choose any amount. There is no minimum and no feature access attached to a donation.")));
        Add(new PictureBox{Image=Assets.RewardQr(),SizeMode=PictureBoxSizeMode.Zoom,Size=new Size(340,340),Anchor=AnchorStyles.Left});
        Add(Line(Lang.T("不赞赏也能使用全部功能。谢谢每一位使用、反馈、分享和支持这个项目的人。","All features remain available without a donation. Thank you for using, reporting issues, sharing and supporting this project.")));
        void Fit(){int width=Math.Max(120,scroll.ClientSize.Width-scroll.Padding.Horizontal-SystemInformation.VerticalScrollBarWidth);stack.Width=width;foreach(Control child in stack.Controls)if(child is Label)child.MaximumSize=new Size(width,0);stack.PerformLayout();}
        scroll.SizeChanged+=(_,_)=>Fit();Shown+=(_,_)=>Fit();
    }
}
