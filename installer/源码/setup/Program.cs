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
            if(args.Length>=4&&args[2]=="--report")File.WriteAllText(args[3],JsonSerializer.Serialize(r,Product.Json));Environment.ExitCode=r.Success?(r.RestartRequired?3010:0):1;return;
        }
        WizardForm? form=null;form=new(true,Product.DefaultRoot,Readme(),(root,_,_)=>Task.Run(()=>new SetupRunner(u=>form!.UpdateProgress(u)).Run(root)),WindowsIntegration.Restart);Application.Run(form);
    }
}
