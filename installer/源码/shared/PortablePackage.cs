using System.Security.Cryptography;
using System.Text.Json;
namespace Burrito.Installer;

public sealed record PortableFile(string Name,string Hash);
public static class PortablePackage
{
    public const string Manifest="portable-files.json";
    public static InstallRecord Initialize(string root)
    {
        root=SafeFiles.ValidateRoot(root);string record=SafeFiles.Child(root,Product.RecordName);
        if(File.Exists(record))return InstallRecord.Load(record);
        string manifest=SafeFiles.Child(root,Manifest);
        var files=JsonSerializer.Deserialize<List<PortableFile>>(File.ReadAllText(manifest))??throw new IOException("免安装文件清单无效");
        if(files.Count!=Product.PayloadNames.Length||!files.Select(f=>f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(Product.PayloadNames))throw new IOException("免安装文件清单不匹配");
        foreach(var file in files){using var input=File.OpenRead(SafeFiles.Child(root,file.Name));if(Convert.ToHexString(SHA256.HashData(input))!=file.Hash)throw new IOException("免安装包文件校验失败："+file.Name);}
        var result=new InstallRecord{Root=root,Portable=true,DriversPreviouslyPresent=Drivers.Packages.ToDictionary(d=>d.Name,d=>Drivers.Inspect(d.Name).Ready)};result.Save();return result;
    }
    public static async Task<Outcome> Prepare(string root,Action<Update> progress)
    {
        var outcome=new Outcome();
        try {
            WindowsIntegration.EnsureIdle();using var maintenance=WindowsIntegration.AcquireMaintenance();var record=InstallRecord.Load(Path.Combine(root,Product.RecordName));if(!record.Portable)throw new IOException("这不是免安装包");
            if(record.UninstallPending)throw new IOException("这个目录仍待完成清理，请先完成清理后重新解压");
            void Log(string text){outcome.Details.Add(text);progress(new(1,text));}
            var drivers=await Drivers.Install(record.Root,Log);outcome.Success=drivers.Success;outcome.RestartRequired=drivers.Reboot;outcome.Details.AddRange(drivers.Problems);
            record.ComponentRestartRequired=drivers.Reboot||(record.ComponentRestartRequired&&CODMControllerProbe.ComponentProbe.SameBoot(record.ComponentPreparedBoot,CODMControllerProbe.ComponentProbe.BootStamp));record.ComponentPreparedBoot=CODMControllerProbe.ComponentProbe.BootStamp;record.Save();
            progress(new(1,"检查组件接口，等待 Windows 初始化"));var readiness=await CODMControllerProbe.ComponentProbe.WaitAsync(root);outcome.RestartRequired|=readiness.NeedsRestart;outcome.ReadyToLaunch=drivers.Success&&readiness.Ready;outcome.Details.Add(readiness.Message);
            outcome.Details.AddRange(readiness.Details);outcome.FilesInstalled=true;
            outcome.Summary=!drivers.Success?"驱动准备未完成，请查看具体记录":outcome.RestartRequired?"组件已安装，Windows 尚有更新待完成，可稍后重启":outcome.ReadyToLaunch?"组件准备完成，可以开始使用":"驱动已安装；"+string.Join("、",new[]{readiness.Vigem,readiness.HidHide}.Where(s=>!s.RuntimeReady).Select(s=>s.Name))+" 暂时无法调用，请查看具体记录";
        }catch(Exception ex){outcome.Summary="组件准备未完成："+ex.Message;outcome.Details.Add(ex.Message);}
        return outcome;
    }
}
