using System.Reflection;
using System.Text.Json;

namespace Burrito.Installer;
internal static class Program
{
    [STAThread]static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        // An interactive update waits for the exact prior process, without
        // killing it or attempting to replace files while it is converting.
        int wait=Array.IndexOf(args,"--wait-pid");int stamp=Array.IndexOf(args,"--wait-start");
        if(wait>=0&&stamp>=0&&wait+1<args.Length&&stamp+1<args.Length&&int.TryParse(args[wait+1],out int pid)&&long.TryParse(args[stamp+1],out long started))
        {
            try{using var prior=System.Diagnostics.Process.GetProcessById(pid);if(prior.StartTime.ToUniversalTime().Ticks==started&&!prior.WaitForExit(30000)){MessageBox.Show("旧版仍在运行，请退出转换器后再打开更新安装包。","更新尚未开始");return;}}
            catch(ArgumentException){}
            catch(Exception ex){MessageBox.Show("无法确认旧版退出状态："+ex.Message,"更新尚未开始");return;}
        }
        string Readme(){using var s=Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.readme")!;using var reader=new StreamReader(s);return reader.ReadToEnd();}
        if(args.Length>=2&&args[0]=="--install")
        {
            var r=new SetupRunner(_=>{}).Run(args[1]).GetAwaiter().GetResult();
            int check=Array.IndexOf(args,"--hardware-check");
            if(check>=0&&check+1<args.Length&&r.ReadyToLaunch)
            {
                int code=WindowsIntegration.Run(Path.Combine(args[1],Product.MainName),["--hardware-check",args[check+1]],r.Details.Add).GetAwaiter().GetResult();
                r.Details.Add("独立硬件验证已执行，返回码："+code+"；具体结果见指定检查报告");
            }
            if(args.Length>=4&&args[2]=="--report")File.WriteAllText(args[3],JsonSerializer.Serialize(r,Product.Json));Environment.ExitCode=r.Success?(r.RestartRequired?3010:0):1;return;
        }
        string installRoot=WindowsIntegration.ExistingRootOrDefault();int updateRoot=Array.IndexOf(args,"--update-root");
        if(updateRoot>=0&&updateRoot+1<args.Length)
        {
            try{installRoot=SafeFiles.ValidateRoot(args[updateRoot+1]);SafeFiles.CheckInstallTarget(installRoot);}
            catch(Exception ex){MessageBox.Show("原安装位置无法用于更新："+ex.Message,"更新尚未开始");return;}
        }
        WizardForm? form=null;form=new(true,installRoot,Readme(),(root,_,_)=>Task.Run(()=>new SetupRunner(u=>form!.UpdateProgress(u)).Run(root)),WindowsIntegration.Restart);Application.Run(form);
    }
}
