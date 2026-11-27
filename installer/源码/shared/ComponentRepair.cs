using CODMControllerProbe;
using System.Text.Json;

namespace Burrito.Installer;

public enum RepairAction { Healthy, Restart, FinishCleanup, Repair }
public static class RepairPolicy
{
    public static RepairAction Decide(ComponentReadiness state,bool uninstallPending)
        =>state.NeedsRestart?RepairAction.Restart:uninstallPending?RepairAction.FinishCleanup:state.Ready?RepairAction.Healthy:RepairAction.Repair;
}

// Shared by the installed maintenance helper and the separate repair kit.
// Repair never removes the application or personal configuration directories.
public static class ComponentRepair
{
    public const string KitManifest="repair-kit.json",KitMarker="Burrito.CODM.RepairKit.v1";
    static (string Root,InstallRecord? Record) Resolve(string input,bool initialize)
    {
        string root=SafeFiles.ValidateRoot(input),recordPath=SafeFiles.Child(root,Product.RecordName);
        if(File.Exists(recordPath))return(root,InstallRecord.Load(recordPath));
        string portable=SafeFiles.Child(root,PortablePackage.Manifest);
        if(File.Exists(portable))return(root,initialize?PortablePackage.Initialize(root):null);
        using var kit=JsonDocument.Parse(File.ReadAllText(SafeFiles.Child(root,KitManifest)));
        if(kit.RootElement.GetProperty("Marker").GetString()!=KitMarker)throw new IOException("这不是完整的连接修复包，请重新解压作者提供的修复包。");
        string installed=WindowsIntegration.ExistingRootOrDefault(),installedRecord=Path.Combine(installed,Product.RecordName);
        return(root,File.Exists(installedRecord)?InstallRecord.Load(installedRecord):null);
    }
    public static async Task<Outcome> Run(string input,Action<Update> progress,bool checkOnly=false)
    {
        var result=new Outcome();
        void Log(string text){result.Details.Add(text);progress(new(1,text));}
        try
        {
            var context=Resolve(input,false);
            Log("修复工具版本："+Product.Version+"；系统："+Environment.OSVersion+"；进程架构："+System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture);
            var damaged=RepairFiles.Inspect(context.Root,context.Record,Log);
            progress(new(0,"检查程序文件与连接组件"));
            var before=await ComponentProbe.WaitAsync(null,1200);
            result.Details.Add("检查结果：");result.Details.AddRange(before.Details);
            bool isolationNeeded=before.HidHide.RuntimeReady&&IsolationRepair.NeedsRepair(context.Record?.Root,Log);
            var action=RepairPolicy.Decide(before,false);
            bool stale=context.Record?.UninstallPending==true;
            if(stale)Log("检测到旧卸载标记；按真实组件状态复查，不再仅凭旧标记阻止恢复");
            if(action==RepairAction.Restart)
            {result.Success=true;result.RestartRequired=true;result.Summary="Windows 仍有驱动更新或移除待完成，请手动重启后再次打开修复工具";return result;}
            if(action==RepairAction.FinishCleanup)
            {result.MaintenanceBlocked=true;result.Summary="上次卸载尚未完成，请先用原卸载工具完成清理，再安装新版；修复工具没有删除你的配置";return result;}
            if(action==RepairAction.Healthy&&damaged.Count==0&&!stale&&!isolationNeeded&&checkOnly)
            {result.Success=true;result.ReadyToLaunch=true;result.HasWarnings=true;result.Summary="程序文件、组件和已知规则正常；未接入手柄时不代表已通过实机隔离验证";return result;}
            if(checkOnly)
            {result.Summary="发现需要处理的项目，点击“开始修复”；请先退出转换器、游戏和模拟器";return result;}

            WindowsIntegration.EnsureIdle();using var maintenance=WindowsIntegration.AcquireMaintenance();
            context=Resolve(input,true);
            damaged=RepairFiles.Inspect(context.Root,context.Record,Log);

            // Re-check under the maintenance lock in case another operation
            // completed while the check window was open.
            var current=ComponentProbe.Capture();
            if(Drivers.Packages.Any(d=>Drivers.RemovalPending(d.Name)||Drivers.NeedsRebootAfterRemoval(Drivers.Inspect(d.Name),Drivers.Loaded(d.Name)))){result.Success=true;result.RestartRequired=true;result.Summary="Windows仍在释放真实驱动组件，请手动重启后再修复";return result;}
            if(current.NeedsRestart){result.Success=true;result.RestartRequired=true;result.Summary=current.Message;return result;}
            bool filesOkay=context.Record==null||RepairFiles.Restore(context.Root,context.Record,damaged,Log);
            if(context.Record is {} receipt&&receipt.UninstallPending){WindowsIntegration.CancelCleanup(receipt);receipt.UninstallPending=false;receipt.PendingReasons.Clear();receipt.Save();Log("真实待移除状态已排除，已恢复安装记录；保留用户配置");}
            if(current.Ready&&filesOkay){var isolation=await IsolationRepair.Run(context.Record?.Root,Log);result.Success=isolation.Success;result.ReadyToLaunch=isolation.Success;result.HasWarnings=isolation.Pending;result.Summary=isolation.Message;return result;}

            progress(new(1,"修复缺失或不完整的官方连接组件"));
            var drivers=await Drivers.Install(context.Root,Log);
            result.Success=drivers.Success;result.RestartRequired=drivers.Reboot;result.Details.AddRange(drivers.Problems);
            if(context.Record is {} record)
            {
                record.ComponentRestartRequired=drivers.Reboot;
                record.ComponentPreparedBoot=ComponentProbe.BootStamp;
                record.Save();
            }
            progress(new(2,"检查实际驱动调用结果"));
            var after=await ComponentProbe.WaitAsync(context.Record?.Root);
            result.RestartRequired|=after.NeedsRestart;result.ReadyToLaunch=filesOkay&&after.Ready;result.Success=result.ReadyToLaunch||result.RestartRequired;
            if(result.ReadyToLaunch){var isolation=await IsolationRepair.Run(context.Record?.Root,Log);result.Success=isolation.Success;result.ReadyToLaunch=isolation.Success;result.HasWarnings=isolation.Pending;result.Summary=isolation.Message;}
            result.Details.Add("修复后：");result.Details.AddRange(after.Details);
            if(string.IsNullOrEmpty(result.Summary))result.Summary=result.RestartRequired?"组件修复已处理，Windows 需要重启完成；可稍后手动重启":result.ReadyToLaunch?"修复完成，原有配置保留，请重新打开转换器":"仍有项目未恢复，详细记录已自动保存；原有配置保留";
        }
        catch(Exception ex){result.Success=false;result.ReadyToLaunch=false;result.Summary="修复尚未完成："+ex.Message;result.Details.Add(ex.ToString());}
        finally
        {
            try
            {
                string report=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CODMControllerProbe","maintenance","connection-repair-last.json");
                SafeFiles.WriteAtomic(report,JsonSerializer.Serialize(result,Product.Json));
                string history=Path.Combine(Path.GetDirectoryName(report)!,"repair-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N")+".json");SafeFiles.WriteAtomic(history,JsonSerializer.Serialize(result,Product.Json));
            }
            catch{}
        }
        return result;
    }
}
