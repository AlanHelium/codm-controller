using CODMControllerProbe;

namespace Burrito.Installer;

public sealed class UninstallRunner(Action<Update> progress)
{
    public async Task<Outcome> Run(string recordPath,bool removeDrivers,bool deleteData)
    {
        var result=new Outcome();
        InstallRecord? recovery=null;
        void Log(string text){result.Details.Add(text);progress(new(1,text));}
        try
        {
            WindowsIntegration.EnsureIdle();var record=InstallRecord.Load(recordPath);recovery=record;
            progress(new(0,"恢复实体输入并核对安装记录"));
            bool sameBoot=record.UninstallPending&&record.PendingBoot>0&&Math.Abs((decimal)ExitPolicy.Boot-record.PendingBoot)<1000;
            if(sameBoot)Log("上次清理待重启，先复查是否可正常停止无引用组件；不强制卸载内核驱动");
            record.UninstallPending=true;record.PendingBoot=0;record.RemoveDrivers=removeDrivers;record.DeleteData=deleteData;record.Save();
            var hide=Drivers.Inspect("HidHide");
            bool activeDriver=hide.Filter||(hide.Service&&Drivers.Loaded("HidHide"));
            if(activeDriver&&!HidHideManager.Restore())throw new IOException("实体输入恢复未完成："+HidHideManager.LastError);
            if(activeDriver)
            {
                var snapshot=HidHideManager.TakeSnapshot();if(!snapshot.Success)throw new IOException("无法读取设备连接状态，未继续移除");
                string own=HidHideManager.ToDriverImagePath(Path.Combine(record.Root,Product.MainName));
                if(!HidHideManager.SetWhitelist(snapshot.Whitelist.Where(p=>!p.Equals(own,StringComparison.OrdinalIgnoreCase))))throw new IOException("本程序的驱动白名单清理未完成");
            }
            else Log("HidHide 已无加载过滤器，停止的残留服务无需读取设备 IOCTL，继续官方清理");
            bool driversOk=true;
            if(removeDrivers)
            {progress(new(1,"通过官方卸载器移除 ViGEmBus / HidHide"));var drivers=await Drivers.Remove(record.Root,Log);driversOk=drivers.Success;result.RestartRequired=drivers.Reboot;result.Details.AddRange(drivers.Problems);}
            else {result.Retained.Add("ViGEmBus / HidHide 共享驱动按选择保留");Log("保留共享驱动；结果不会标为完全清理");}
            if(!driversOk&&!result.RestartRequired){result.Summary="驱动清理尚未完成，卸载程序和修复文件已保留，请查看详细记录";return result;}
            if(deleteData)
            {
                foreach(var special in new[]{Environment.SpecialFolder.ApplicationData,Environment.SpecialFolder.LocalApplicationData})
                {string expected=Path.Combine(Environment.GetFolderPath(special),"CODMControllerProbe");SafeFiles.DeleteOwnedTree(expected,expected);Log("已删除当前用户目录："+expected);}
            }
            else result.Retained.Add("当前用户的手感、键位、授权及维护记录按选择保留");
            progress(new(2,"逐项核验文件、驱动和卸载入口"));
            if(result.RestartRequired)
            {
                record.UninstallPending=true;record.PendingBoot=ExitPolicy.Boot;record.RemoveDrivers=removeDrivers;record.DeleteData=deleteData;record.PendingReasons=result.Details;record.Save();
                result.Retained.AddRange(SafeFiles.RemoveInstalledFiles(record.Root,true));WindowsIntegration.ResumeAfterReboot(record);
                result.Success=driversOk&&result.Retained.Count==(deleteData?0:1);
                result.Summary="程序清理已执行，驱动待重启；保留卸载器与官方包用于重启后复查";return result;
            }
            // Check that the registration is ours before deleting the recovery executable.
            WindowsIntegration.Unregister(record);
            var leftovers=SafeFiles.RemoveInstalledFiles(record.Root,false);result.Retained.AddRange(leftovers);
            if(leftovers.Count>0){WindowsIntegration.RecoveryRegistration(record);result.Summary="部分文件未删除，已保留卸载入口；请查看剩余项后重试";return result;}
            result.Success=true;result.CompleteCleanup=removeDrivers&&deleteData&&result.Retained.Count==0;
            result.Summary=result.CompleteCleanup?"程序、配置与驱动已核验移除；关闭窗口后清理卸载器运行缓存":"卸载完成，保留项已按你的选择列出";
            Log(result.Summary);
        }
        catch(Exception ex)
        {
            if(recovery!=null&&File.Exists(Path.Combine(recovery.Root,Product.RecordName)))
            {
                try{string helper=Path.Combine(recovery.Root,Product.UninstallName);if(!File.Exists(helper))File.Copy(Environment.ProcessPath!,helper);WindowsIntegration.RecoveryRegistration(recovery);}catch(Exception repair){result.Details.Add("恢复卸载入口未完成："+repair.Message);}
            }
            result.Summary="卸载未完成："+ex.Message;result.Details.Add(ex.Message);progress(new(2,result.Summary));
        }
        return result;
    }
}
