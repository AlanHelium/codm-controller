using System.Reflection;
using System.Text.Json;

namespace Burrito.Installer;
internal static class Program
{
    [STAThread]static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
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
        WizardForm? form=null;form=new(true,Product.DefaultRoot,Readme(),(root,_,_)=>Task.Run(()=>new SetupRunner(u=>form!.UpdateProgress(u)).Run(root)),WindowsIntegration.Restart);Application.Run(form);
    }
}
