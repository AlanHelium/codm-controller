using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Burrito.Installer;
using CODMControllerProbe;

static class FirstRunChecks
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static int checks;
    static void Check(bool pass,string message){if(!pass)throw new Exception(message);checks++;}
    static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,Private)!.GetValue(target)!;
    static IEnumerable<Control> Children(Control root){foreach(Control child in root.Controls){yield return child;foreach(var nested in Children(child))yield return nested;}}
    static void Wait(Task task){long end=Environment.TickCount64+15000;while(!task.IsCompleted){if(Environment.TickCount64>end)throw new Exception("Test async operation timed out");Application.DoEvents();Thread.Sleep(10);}task.GetAwaiter().GetResult();}
    static void Render(Form form,string path){Application.DoEvents();using var b=new Bitmap(form.Width,form.Height);form.DrawToBitmap(b,new Rectangle(Point.Empty,form.Size));b.Save(path);}
    static Process StartHelper(string name)
    {
        string target=Path.Combine(AppContext.BaseDirectory,name+".exe");
        File.Copy(Environment.ProcessPath!,target,true);
        return Process.Start(new ProcessStartInfo(target,"--helper"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden})!;
    }
    [STAThread]static void Main(string[] args)
    {
        if(Process.GetCurrentProcess().ProcessName=="CODMControllerProbe"){File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"fake-main.pid"),Environment.ProcessId.ToString());Thread.Sleep(20000);return;}
        if(args.Contains("--helper")){Thread.Sleep(20000);return;}
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);Application.EnableVisualStyles();Lang.En=false;
        string folder=args[0];Directory.CreateDirectory(folder);
        string Hash()=>File.Exists(SettingsStore.Path_)?Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(SettingsStore.Path_))):"missing";
        string before=Hash();var hiddenBefore=HidHideManager.TakeSnapshot();
        var keywords=new[]{"codm","gameloop","androidemulator","aow_exe","appmarket"};
        foreach(string helper in new[]{"CODMController-v3.4.4-Setup","CODMControllerProbeSetup","CODMControllerProbeUninstaller","CODM手柄转换器-v3.4.3","CODMController-v3.4.4-Portable"})Check(!StartupProcessPolicy.BlocksConversion(helper,keywords),"Converter helper was mistaken for a game: "+helper);
        foreach(string game in new[]{"codm","codmobile","aow_exe","AndroidEmulatorEn","AppMarket"})Check(StartupProcessPolicy.BlocksConversion(game,keywords),"Real game/emulator guard was bypassed: "+game);
        var bus=new ComponentState("ViGEmBus",true,true,true,false,"");var hid=new ComponentState("HidHide",true,true,true,false,"",false);
        var ready=new ComponentReadiness(bus,hid,false);
        var idle=new StartupDiagnosticData(ready,true,"DualSense",false,false,false,false,false,"",false,false);
        Check(ready.Ready,"Available interfaces were not ready");Check(idle.Rows().Take(3).All(r=>r.Level==HealthLevel.Ready),"Installed drivers or connected pad were red before conversion");
        Check(idle.Rows()[3].Level==HealthLevel.Info,"Idle conversion was reported as a fault");Check(idle.Rows()[4].Level==HealthLevel.Info,"Unused audio was reported as a fault");
        var waiting=idle with {Components=ready with{HidHide=hid with{RuntimeReady=false,Active=null}}};
        Check(waiting.Rows()[1].Level==HealthLevel.Waiting,"Installed but pending interface was reported as missing");Check(waiting.Rows()[1].Summary.Contains("已安装"),"Pending state lost installation information");
        var missing=idle with{Components=ready with{Vigem=bus with{Installed=false,Configured=false,RuntimeReady=false}}};Check(missing.Rows()[0].Level==HealthLevel.Error,"Actually missing driver was not reported");
        var restarting=ready with{SetupRestartPending=true};Check(restarting.NeedsRestart&&!restarting.Ready,"Official pending restart was ignored");
        Check(ComponentProbe.SameBoot(100000,100005)&&!ComponentProbe.SameBoot(100000,130000),"Pending restart survived a fast new boot");
        string recordRoot=Path.Combine(folder,"record-only");Directory.CreateDirectory(recordRoot);
        void SaveRecord(long stamp,string marker="Burrito.CODM.Install.v1")=>File.WriteAllText(Path.Combine(recordRoot,"installation.json"),JsonSerializer.Serialize(new{Marker=marker,Root=recordRoot,ComponentRestartRequired=true,ComponentPreparedBoot=stamp}));
        SaveRecord(ComponentProbe.BootStamp);Check(ComponentProbe.PendingFromRecord(recordRoot),"Current-boot restart request was lost");
        Check(ComponentProbe.PendingFromRecord(recordRoot+Path.DirectorySeparatorChar),"App base-directory trailing slash lost restart status");
        SaveRecord(ComponentProbe.BootStamp-120000);Check(!ComponentProbe.PendingFromRecord(recordRoot),"Restart request remained after reboot");
        SaveRecord(ComponentProbe.BootStamp,"OtherProduct");Check(!ComponentProbe.PendingFromRecord(recordRoot),"Foreign maintenance record influenced status");
        typeof(MainForm).Assembly.GetType("CODMControllerProbe.UiPreview")!.GetProperty("Enabled",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!.SetValue(null,true);
        using var main=new MainForm{StartPosition=FormStartPosition.Manual,Location=new(-32000,-32000)};main.Show();Application.DoEvents();
        Check(typeof(MainForm).GetField("_cleanDriversOnExit",Private)==null,"Legacy exit cleanup is still used by the main window");
        var outlineType=typeof(MainForm).Assembly.GetType("CODMControllerProbe.StudioButton")!;
        using(var background=new Panel{BackColor=Color.FromArgb(23,25,28)})
        foreach(var size in new[]{new Size(38,36),new Size(120,42),new Size(230,48)})
        {
            using var button=(Button)Activator.CreateInstance(outlineType)!;button.Size=size;background.Controls.Add(button);
            foreach(var face in new[]{Color.FromArgb(32,35,39),Color.FromArgb(239,133,76)})
            {
                button.BackColor=face;using var bitmap=new Bitmap(size.Width,size.Height);using var graphics=Graphics.FromImage(bitmap);graphics.Clear(Color.Orange);
                outlineType.GetMethod("OnPaint",Private)!.Invoke(button,[new PaintEventArgs(graphics,new Rectangle(Point.Empty,size))]);
                Check(new[]{new Point(0,0),new Point(size.Width-1,0),new Point(0,size.Height-1),new Point(size.Width-1,size.Height-1)}.All(p=>bitmap.GetPixel(p.X,p.Y).ToArgb()==background.BackColor.ToArgb()),"A rounded repaint retained sharp orange corner pixels");
            }
            bool clicked=false;button.Click+=(_,_)=>clicked=true;button.PerformClick();Check(clicked,"Custom painting broke button activation");
        }
        Render(main,Path.Combine(folder,"圆角优化-首页.png"));
        typeof(MainForm).GetMethod("OpenSettings",Private)!.Invoke(main,[5]);Application.DoEvents();
        Check(!Children(main).OfType<CheckBox>().Any(c=>c.Text.Contains("退出后打开")),"Retired exit cleanup checkbox remains visible");
        Check(Children(main).OfType<Button>().Any(b=>b.Text.Contains("打开清理 / 卸载")),"Explicit cleanup entry is missing");
        Render(main,Path.Combine(folder,"圆角优化-维护入口.png"));
        typeof(MainForm).GetMethod("ShowStudioSection",Private)!.Invoke(main,[0]);Application.DoEvents();
        using(var setup=StartHelper("CODMController-v3.4.4-Setup"))
        {
            try{Thread.Sleep(150);Check(!setup.HasExited,"Setup test helper exited early");var blockers=(List<(string Name,int Id)>)typeof(MainForm).GetMethod("BlockingGameProcesses",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,null)!;Check(!blockers.Any(p=>p.Id==setup.Id),"Actual Setup process blocked conversion");}
            finally{if(!setup.HasExited)setup.Kill();setup.WaitForExit();}
        }
        using(var game=StartHelper("aow_exe"))
        {
            try{Thread.Sleep(150);var blockers=(List<(string Name,int Id)>)typeof(MainForm).GetMethod("BlockingGameProcesses",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,null)!;Check(blockers.Any(p=>p.Id==game.Id),"Actual emulator process was not blocked");}
            finally{if(!game.HasExited)game.Kill();game.WaitForExit();}
        }
        var liveTask=(Task<StartupDiagnosticData>)typeof(MainForm).GetMethod("CaptureStartupDiagnosticsAsync",Private)!.Invoke(main,null)!;Wait(liveTask);var live=liveTask.Result;
        Check(live.Components.Ready,"Actual component interfaces are unavailable: "+live.Components.Message);
        Check(!live.VirtualConnected&&!live.InputReadable,"Preview unexpectedly started input/output");
        Check(live.Rows().Take(2).All(r=>r.Level==HealthLevel.Ready),"Actual installed drivers were red before conversion");
        using(var activeClient=new Nefarius.ViGEm.Client.ViGEmClient())
        {
            var target=activeClient.CreateDualShock4Controller();target.AutoSubmitReport=false;target.Connect();
            try{target.ResetReport();target.SubmitReport();Check(ComponentProbe.Capture().Ready,"Read-only probe failed while another virtual controller was connected");target.ResetReport();target.SubmitReport();Check(true,"Active target remained connected after the probe");}
            finally{target.Disconnect();}
        }
        typeof(MainForm).GetField("_componentWaiting",Private)!.SetValue(main,true);typeof(MainForm).GetField("_conversionError",Private)!.SetValue(main,"接口尚未就绪");
        var recovered=(Task<StartupDiagnosticData>)typeof(MainForm).GetMethod("CaptureStartupDiagnosticsAsync",Private)!.Invoke(main,null)!;Wait(recovered);Check(recovered.Result.ConversionError.Length==0,"Ready refresh retained a stale component error");
        typeof(MainForm).GetField("_componentWaiting",Private)!.SetValue(main,false);typeof(MainForm).GetField("_conversionError",Private)!.SetValue(main,"");
        var displayed=idle;
        using var diagnostic=new DiagForm(()=>Task.FromResult(displayed),()=>{},()=>{}){StartPosition=FormStartPosition.Manual,Location=new(-32000,-32000)};
        typeof(MainForm).GetMethod("MountStudioForm",Private)!.Invoke(main,[diagnostic,null,false,false]);Application.DoEvents();
        Wait((Task)typeof(DiagForm).GetMethod("RefreshAsync",Private)!.Invoke(diagnostic,null)!);
        Check(Children(diagnostic).OfType<Label>().Count(l=>l.Text.StartsWith("✓"))==3,"Initial diagnostic display does not show three ready items");Render(main,Path.Combine(folder,"首次诊断-就绪未转换.png"));
        displayed=idle with{Running=true,InputReadable=true,VirtualConnected=true,IsolationReady=true,AudioRequested=true,AudioRunning=true};
        Wait((Task)typeof(DiagForm).GetMethod("RefreshAsync",Private)!.Invoke(diagnostic,null)!);Check(Children(diagnostic).OfType<Label>().Count(l=>l.Text.StartsWith("✓"))==5,"Refreshed diagnostic display retained the initial snapshot");
        displayed=waiting;Wait((Task)typeof(DiagForm).GetMethod("RefreshAsync",Private)!.Invoke(diagnostic,null)!);Check(!Children(diagnostic).OfType<Label>().Any(l=>l.Text.StartsWith("✕")),"Initializing components were shown as red failures");
        diagnostic.Close();Application.DoEvents();
        using(var wizard=new WizardForm(true,AppContext.BaseDirectory,"Test README",(_,_,_)=>Task.FromResult(new Outcome()),()=>throw new Exception("Automatic restart must not occur")))
        {
            wizard.StartPosition=FormStartPosition.Manual;wizard.Location=new(-32000,-32000);wizard.Show();
            wizard.ShowResult(new Outcome{Success=true,Summary="文件已安装，接口尚未就绪"});Check(Field<Button>(wizard,"_primary").Text=="重新检查","Setup offered launch before interfaces were ready");
            wizard.ShowResult(new Outcome{Success=true,RestartRequired=true,Summary="Windows 更新待重启"});Check(Field<Button>(wizard,"_close").Text=="稍后重启","Setup did not offer deferred restart");
            wizard.ShowResult(new Outcome{Success=true,ReadyToLaunch=true,Summary="接口已就绪，可以使用"});Check(Field<Button>(wizard,"_primary").Text=="启动软件","Ready Setup did not offer launch");Render(wizard,Path.Combine(folder,"安装完成-接口就绪.png"));
            string fakeMain=Path.Combine(AppContext.BaseDirectory,"CODMControllerProbe.exe");File.Copy(Environment.ProcessPath!,fakeMain,true);
            string fakePid=Path.Combine(AppContext.BaseDirectory,"fake-main.pid");if(File.Exists(fakePid))File.Delete(fakePid);
            try
            {
                var launching=(Task)typeof(WizardForm).GetMethod("Execute",Private)!.Invoke(wizard,null)!;Wait(launching);Check(wizard.IsDisposed,"Setup stayed open after launching the app");
                long deadline=Environment.TickCount64+4000;while(!File.Exists(fakePid)&&Environment.TickCount64<deadline){Application.DoEvents();Thread.Sleep(10);}
                Check(File.Exists(fakePid),"Launched test app did not start");
                using var launched=Process.GetProcessById(int.Parse(File.ReadAllText(fakePid)));Check(launched.MainModule?.FileName==fakeMain,"Launched helper identity mismatch");launched.Kill();launched.WaitForExit();
            }
            finally{File.Delete(fakeMain);if(File.Exists(fakePid))File.Delete(fakePid);}
        }
        main.Close();var hiddenAfter=HidHideManager.TakeSnapshot();
        Check(before==Hash(),"Tests changed personal configuration");
        Check(hiddenBefore.Success&&hiddenAfter.Success&&hiddenBefore.Active==hiddenAfter.Active&&hiddenBefore.Inverse==hiddenAfter.Inverse&&hiddenBefore.Blacklist.SequenceEqual(hiddenAfter.Blacklist)&&hiddenBefore.Whitelist.SequenceEqual(hiddenAfter.Whitelist),"Read-only probes changed HidHide configuration");
        File.WriteAllText(Path.Combine(folder,"首次启动回归检查.json"),JsonSerializer.Serialize(new{Passed=true,Assertions=checks,InstalledInterfaces=live.Components,PhysicalConnected=live.PhysicalConnected,SetupProcessExcluded=true,EmulatorGuardRetained=true,SetupClosesAfterLaunch=true,ConfigUnchanged=true,HidHideUnchanged=true},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"First-run checks passed: {checks} assertions; configuration and isolation unchanged");
    }
}
