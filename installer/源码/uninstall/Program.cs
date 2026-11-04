using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace Burrito.Installer;
internal static class Program
{
    [STAThread]static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        bool repair=args.Contains("--repair")||string.Equals(Path.GetFileName(Environment.ProcessPath),"CODMControllerRepair.exe",StringComparison.OrdinalIgnoreCase)
            ||(!File.Exists(Path.Combine(AppContext.BaseDirectory,Product.RecordName))&&File.Exists(Path.Combine(AppContext.BaseDirectory,ComponentRepair.KitManifest)));
        try
        {
            if(!WaitForPreviousProcess(args))return;
            string recordPath=Path.Combine(AppContext.BaseDirectory,Product.RecordName);
            int recordArg=Array.IndexOf(args,"--record");if(recordArg>=0&&recordArg+1<args.Length)recordPath=args[recordArg+1];
            if(!repair&&args.Contains("--portable")&&!args.Contains("--relaunched")&&!File.Exists(recordPath))PortablePackage.Initialize(AppContext.BaseDirectory);
            var record=repair?null:InstallRecord.Load(recordPath);
            string running=Environment.ProcessPath??throw new IOException("无法读取卸载器路径");
            bool relaunched=args.Contains("--relaunched");
            if(!relaunched)
            {
                string folder=Path.Combine(Path.GetTempPath(),"CODM-Uninstall-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);string copy=Path.Combine(folder,Product.UninstallName);File.Copy(running,copy);
                bool commandLine=args.Contains("--run");
                var start=new ProcessStartInfo(copy){UseShellExecute=false,CreateNoWindow=commandLine,WindowStyle=commandLine?ProcessWindowStyle.Hidden:ProcessWindowStyle.Normal};start.ArgumentList.Add("--relaunched");start.ArgumentList.Add("--record");start.ArgumentList.Add(recordPath);
                if(repair&&!args.Contains("--repair"))start.ArgumentList.Add("--repair");
                foreach(string arg in args.Where(a=>a!="--record"&&a!=recordPath))start.ArgumentList.Add(arg);
                Process.Start(start)?.Dispose();return;
            }
            bool cleanCaches=false;string temp=Path.GetDirectoryName(running)!;
            try
            {
                if(repair)
                {
                    string root=Path.GetDirectoryName(Path.GetFullPath(recordPath))!;
                    if(args.Contains("--run"))
                    {
                        var outcome=ComponentRepair.Run(root,_=>{},args.Contains("--check")).GetAwaiter().GetResult();
                        int report=Array.IndexOf(args,"--report");if(report>=0&&report+1<args.Length)File.WriteAllText(args[report+1],JsonSerializer.Serialize(outcome,Product.Json));
                        Environment.ExitCode=outcome.RestartRequired?3010:outcome.Success?0:1;return;
                    }
                    string guide;using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.readme")!)using(var reader=new StreamReader(stream))guide=reader.ReadToEnd();
                    WizardForm? repairWindow=null;repairWindow=new(true,root,guide,(_,_,_)=>Task.Run(()=>ComponentRepair.Run(root,u=>repairWindow!.UpdateProgress(u))),WindowsIntegration.Restart,componentOnly:true,repairMode:true);
                    repairWindow.Shown+=async(_,_)=>{repairWindow.SetRepairChecking();var check=await Task.Run(()=>ComponentRepair.Run(root,u=>repairWindow.UpdateProgress(u),true));if(!repairWindow.IsDisposed)repairWindow.ShowResult(check);};
                    Application.Run(repairWindow);return;
                }
                if(record==null)throw new IOException("缺少安装记录");
                if(args.Contains("--prepare")) {
                    if(!record.Portable)throw new IOException("组件准备入口只用于免安装包");
                    if(args.Contains("--run")){
                        var ready=PortablePackage.Prepare(record.Root,_=>{}).GetAwaiter().GetResult();int report=Array.IndexOf(args,"--report");if(report>=0&&report+1<args.Length)File.WriteAllText(args[report+1],JsonSerializer.Serialize(ready,Product.Json));Environment.ExitCode=ready.Success?(ready.RestartRequired?3010:0):1;return;
                    }
                    string guide;using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.readme")!)using(var reader=new StreamReader(stream))guide=reader.ReadToEnd();
                    WizardForm? prepare=null;prepare=new(true,record.Root,guide,(_,_,_)=>Task.Run(()=>PortablePackage.Prepare(record.Root,u=>prepare!.UpdateProgress(u))),WindowsIntegration.Restart,componentOnly:true);Application.Run(prepare);return;
                }
                if(args.Contains("--run"))
                {
                    var outcome=new UninstallRunner(_=>{}).Run(recordPath,!args.Contains("--keep-drivers"),!args.Contains("--keep-data")).GetAwaiter().GetResult();
                    int report=Array.IndexOf(args,"--report");if(report>=0&&report+1<args.Length)File.WriteAllText(args[report+1],JsonSerializer.Serialize(outcome,Product.Json));cleanCaches=outcome.Success&&!outcome.RestartRequired;Environment.ExitCode=outcome.Success?(outcome.RestartRequired?3010:0):1;return;
                }
                string readme;using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.readme")!)using(var reader=new StreamReader(stream))readme=reader.ReadToEnd();
                WizardForm? form=null;form=new(false,record.Root,readme,(_,drivers,data)=>Task.Run(()=>new UninstallRunner(u=>form!.UpdateProgress(u)).Run(recordPath,drivers,data)),WindowsIntegration.Restart);
                if(record.UninstallPending)form.SetCleanupChoices(record.RemoveDrivers,record.DeleteData);
                if(args.Contains("--resume"))form.Shown+=async(_,_)=>form.ShowResult(await Task.Run(()=>new UninstallRunner(u=>form.UpdateProgress(u)).Run(recordPath,record.RemoveDrivers,record.DeleteData)));
                Application.Run(form);cleanCaches=form.CleanupCachesOnClose;
            }
            finally{ExitCleanup.Start(temp,cleanCaches);}
        }
        catch(Exception ex){MessageBox.Show((repair?"未执行连接修复：":"未执行卸载：")+ex.Message+"\n请从完整的安装目录或修复包启动。",repair?"连接修复提示":"卸载提示",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
    }
    static bool WaitForPreviousProcess(string[] args)
    {
        int wait=Array.IndexOf(args,"--wait-pid"),stamp=Array.IndexOf(args,"--wait-start");
        if(wait<0||stamp<0||wait+1>=args.Length||stamp+1>=args.Length)return true;
        if(!int.TryParse(args[wait+1],out int pid)||!long.TryParse(args[stamp+1],out long ticks))throw new IOException("等待进程参数无效");
        try{using var prior=Process.GetProcessById(pid);if(prior.StartTime.ToUniversalTime().Ticks==ticks&&!prior.WaitForExit(30000)){MessageBox.Show("转换器尚未退出，请正常关闭后再打开修复工具。","连接修复尚未开始");return false;}}
        catch(ArgumentException){}
        return true;
    }
}
