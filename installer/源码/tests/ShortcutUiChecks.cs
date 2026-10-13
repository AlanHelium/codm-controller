using System.Reflection;
using System.Text.Json;
using Burrito.Installer;

static class ShortcutUiChecks
{
    [STAThread]static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);Application.EnableVisualStyles();
        using var form=new WizardForm(true,@"C:\Apps\CODMController","Test README",(_,_,_)=>throw new Exception("Must not install in a UI check"),()=>throw new Exception("Must not restart in a UI check"),true);
        form.StartPosition=FormStartPosition.Manual;form.Location=new(-32000,-32000);form.Show();Application.DoEvents();
        T Field<T>(string name)=>(T)typeof(WizardForm).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(form)!;
        form.ShowResult(new Outcome{Success=true,ReadyToLaunch=true,Summary="组件就绪，可以使用"});var readyColor=Field<Label>("_status").ForeColor;
        form.ShowResult(new Outcome{Success=true,ReadyToLaunch=true,HasWarnings=true,Summary="安装完成，组件接口可用；快捷方式未创建，请从安装目录打开软件",Details=["开始菜单快捷方式未创建：Permission denied (test)；可从安装目录运行 CODMControllerProbe.exe。"]});
        if(Field<Label>("_status").ForeColor==readyColor)throw new Exception("Shortcut warning is indistinguishable from fully successful setup");
        if(!Field<Button>("_primary").Enabled||Field<Button>("_primary").Text!="启动软件")throw new Exception("Optional shortcut failure blocked usable app launch");
        if(!Field<TextBox>("_log").Text.Contains("CODMControllerProbe.exe"))throw new Exception("Launch workaround is absent from result log");
        string folder=Path.GetFullPath(args[0]);Directory.CreateDirectory(folder);Application.DoEvents();using var bitmap=new Bitmap(form.Width,form.Height);form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));bitmap.Save(Path.Combine(folder,"快捷方式失败-安装仍可用.png"));
        File.WriteAllText(Path.Combine(folder,"快捷方式警告UI.json"),JsonSerializer.Serialize(new{Passed=true,Assertions=3,WarningVisible=true,LaunchAvailable=true,WorkaroundVisible=true,SystemMutations=false},Product.Json));
        Console.WriteLine("Shortcut warning UI checks passed: 3 assertions; no system changes");form.Close();
    }
}
