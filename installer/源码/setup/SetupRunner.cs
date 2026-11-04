using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace Burrito.Installer;

public sealed record Payload(string Name,string Resource,string Hash,long Bytes);
public sealed class SetupRunner(Action<Update> progress)
{
    public async Task<Outcome> Run(string input)
    {
        var outcome=new Outcome();string root="";string? shortcutWarning=null;
        void Log(string text){progress(new(1,text));if(root.Length>0&&Directory.Exists(root))File.AppendAllText(Path.Combine(root,"install.log"),text+Environment.NewLine);}
        try
        {
            progress(new(0,"检查安装位置、运行中的程序和包内文件"));
            if(System.Runtime.InteropServices.RuntimeInformation.OSArchitecture!=System.Runtime.InteropServices.Architecture.X64)throw new IOException("此安装包仅支持 Windows x64");
            WindowsIntegration.EnsureIdle();using var maintenance=WindowsIntegration.AcquireMaintenance();root=SafeFiles.ValidateRoot(input);SafeFiles.CheckInstallTarget(root);
            var prior=File.Exists(Path.Combine(root,Product.RecordName))?InstallRecord.Load(Path.Combine(root,Product.RecordName)):null;
            if(prior?.UninstallPending==true)
            {
                outcome.RestartRequired=Drivers.Packages.Any(d=>Drivers.RemovalPending(d.Name)||Drivers.NeedsRebootAfterRemoval(Drivers.Inspect(d.Name),Drivers.Loaded(d.Name)));
                if(outcome.RestartRequired){outcome.Summary="Windows 仍在释放上次移除的组件，请保存工作后重启再安装";progress(new(2,outcome.Summary));return outcome;}
                Log("上次卸载记录已核对，无待释放组件，继续本次安装并修复需要的项目");
            }
            var assembly=typeof(SetupRunner).Assembly;
            using var manifest=assembly.GetManifestResourceStream("payload.manifest")??throw new IOException("缺少安装清单");
            var payloads=JsonSerializer.Deserialize<List<Payload>>(manifest)??throw new IOException("安装清单无效");
            if(!payloads.Select(p=>p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(Product.PayloadNames))throw new IOException("安装文件清单不匹配");
            var drive=new DriveInfo(Path.GetPathRoot(root)!);if(drive.AvailableFreeSpace<payloads.Sum(p=>p.Bytes)+200L*1024*1024)throw new IOException("目标磁盘剩余空间不足，请至少留出500MB");
            // Stage and verify every embedded file before touching the user's target directory.
            string staging=Path.Combine(Path.GetTempPath(),"CODM-Setup-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);
            try
            {
                foreach(var payload in payloads)
                {
                    string file=SafeFiles.Child(staging,payload.Name);Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                    using(var from=assembly.GetManifestResourceStream(payload.Resource)??throw new IOException("内嵌文件缺失："+payload.Name))using(var to=File.Create(file))from.CopyTo(to);
                    using var read=File.OpenRead(file);if(Convert.ToHexString(SHA256.HashData(read))!=payload.Hash)throw new IOException("文件校验失败："+payload.Name);
                }
                var record=new InstallRecord{Root=root,DriversPreviouslyPresent=Drivers.Packages.ToDictionary(d=>d.Name,d=>Drivers.Inspect(d.Name).Ready),ComponentRestartRequired=prior?.ComponentRestartRequired==true&&CODMControllerProbe.ComponentProbe.SameBoot(prior.ComponentPreparedBoot,CODMControllerProbe.ComponentProbe.BootStamp),ComponentPreparedBoot=prior?.ComponentPreparedBoot??0};
                Directory.CreateDirectory(root);record.Save();
                foreach(var payload in payloads){string to=SafeFiles.Child(root,payload.Name);Directory.CreateDirectory(Path.GetDirectoryName(to)!);File.Copy(SafeFiles.Child(staging,payload.Name),to,true);Log("已复制文件："+payload.Name);}
                WindowsIntegration.Register(record);Log("已注册系统卸载入口");
                shortcutWarning=UnicodeShortcut.TryCreate(root,Product.MainName,Product.Shortcut);
                if(shortcutWarning==null)Log("已创建开始菜单快捷方式");
                else{outcome.HasWarnings=true;outcome.Details.Add(shortcutWarning);Log(shortcutWarning+" 软件文件与驱动安装将继续。");}
            }
            finally{SafeFiles.DeleteOwnedTree(staging,staging);}
            outcome.FilesInstalled=true;
            progress(new(1,"准备官方手柄驱动"));var drivers=await Drivers.Install(root,Log);outcome.RestartRequired=drivers.Reboot;outcome.Details.AddRange(drivers.States.Select(s=>s.Name+"："+s.Summary));
            outcome.Success=drivers.Success;outcome.Details.AddRange(drivers.Problems);
            var installed=InstallRecord.Load(Path.Combine(root,Product.RecordName));
            installed.ComponentRestartRequired=drivers.Reboot||(installed.ComponentRestartRequired&&CODMControllerProbe.ComponentProbe.SameBoot(installed.ComponentPreparedBoot,CODMControllerProbe.ComponentProbe.BootStamp));installed.ComponentPreparedBoot=CODMControllerProbe.ComponentProbe.BootStamp;installed.Save();
            progress(new(1,"检查驱动接口，等待 Windows 完成初始化"));
            var readiness=await CODMControllerProbe.ComponentProbe.WaitAsync(root);outcome.RestartRequired|=readiness.NeedsRestart;outcome.ReadyToLaunch=drivers.Success&&readiness.Ready;
            outcome.Details.Add(readiness.Message);
            outcome.Details.AddRange(readiness.Details);
            string action=prior!=null?"软件更新完成":"软件安装完成";
            outcome.Summary=!drivers.Success?action+"；驱动准备未完成，请查看具体记录":outcome.RestartRequired?action+"；Windows 尚有组件更新待完成，可稍后重启":outcome.ReadyToLaunch?action+"，可以开始使用":action+"；"+string.Join("、",new[]{readiness.Vigem,readiness.HidHide}.Where(s=>!s.RuntimeReady).Select(s=>s.Name))+" 暂时无法调用，可打开软件查看诊断";
            if(shortcutWarning!=null)outcome.Summary+="；开始菜单快捷方式未创建，请从安装目录打开软件";
            progress(new(2,outcome.Summary));
        }
        catch(Exception ex){outcome.Summary="安装未完成："+ex.Message;outcome.Details.Add(ex.Message);progress(new(2,outcome.Summary));}
        return outcome;
    }
}
