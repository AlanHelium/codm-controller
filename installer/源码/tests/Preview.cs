using Burrito.Installer;
static class Preview
{
    [STAThread]static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        bool install=args.Length==0||args[0]!="uninstall";
        string readme=File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../payload/必看 README.TXT")));
        var form=new WizardForm(install,install?Product.DefaultRoot:@"D:\手柄工具\CODM手柄转换器",readme,(_,_,_)=>Task.FromResult(new Outcome()),()=>false,true);
        if(args.Contains("large")){form.ClientSize=new(840,650);form.Shown+=(_,_)=>form.ScalePreviewFonts(1.25f);}
        if(args.Contains("reboot"))form.Shown+=(_,_)=>form.ShowResult(new Outcome{Success=true,RestartRequired=true,Summary="程序清理已执行，驱动待重启；保留复查入口"});
        if(args.Contains("success"))form.Shown+=(_,_)=>form.ShowResult(new Outcome{Success=true,CompleteCleanup=true,Summary="所选项目已核验移除；关闭后清理运行缓存"});
        if(args.Length>=3&&args[1]=="--render")
        {
            form.StartPosition=FormStartPosition.Manual;form.Location=new(-32000,-32000);form.Show();for(int i=0;i<6;i++){Application.DoEvents();form.PerformLayout();}using var bitmap=new Bitmap(form.Width,form.Height);form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));bitmap.Save(args[2]);form.Close();return;
        }
        Application.Run(form);
    }
}
