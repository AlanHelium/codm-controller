using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace Burrito.Installer;

public sealed record Payload(string Name,string Resource,string Hash,long Bytes);
public sealed class SetupRunner(Action<Update> progress)
{
    public async Task<Outcome> Run(string input)
    {
        var outcome=new Outcome();string root="";
        void Log(string text){progress(new(1,text));if(root.Length>0&&Directory.Exists(root))File.AppendAllText(Path.Combine(root,"install.log"),text+Environment.NewLine);}
        try
        {
            progress(new(0,"检查安装位置、运行中的程序和包内文件"));
            if(System.Runtime.InteropServices.RuntimeInformation.OSArchitecture!=System.Runtime.InteropServices.Architecture.X64)throw new IOException("此安装包仅支持 Windows x64");
            WindowsIntegration.EnsureIdle();root=SafeFiles.ValidateRoot(input);SafeFiles.CheckInstallTarget(root);
            if(File.Exists(Path.Combine(root,Product.RecordName))&&InstallRecord.Load(Path.Combine(root,Product.RecordName)).UninstallPending)
            {outcome.RestartRequired=Drivers.Packages.Any(d=>Drivers.RemovalPending(d.Name));outcome.Summary="这个目录仍有待完成的卸载，请先运行目录内的卸载器完成复查，再重新安装";progress(new(2,outcome.Summary));return outcome;}
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
                var record=new InstallRecord{Root=root,DriversPreviouslyPresent=Drivers.Packages.ToDictionary(d=>d.Name,d=>Drivers.Inspect(d.Name).Ready)};
                Directory.CreateDirectory(root);record.Save();
                foreach(var payload in payloads){string to=SafeFiles.Child(root,payload.Name);Directory.CreateDirectory(Path.GetDirectoryName(to)!);File.Copy(SafeFiles.Child(staging,payload.Name),to,true);Log("已安装："+payload.Name);}
                WindowsIntegration.Register(record);Log("已注册卸载入口与开始菜单快捷方式");
            }
            finally{SafeFiles.DeleteOwnedTree(staging,staging);}
            progress(new(1,"准备官方手柄驱动"));var drivers=await Drivers.Install(root,Log);outcome.RestartRequired=drivers.Reboot;outcome.Details.AddRange(drivers.States.Select(s=>s.Name+"："+s.Summary));
            outcome.Success=drivers.Success;outcome.Details.AddRange(drivers.Problems);
            outcome.Summary=!drivers.Success?"安装未完成，已保留卸载入口和详细记录，可修复或卸载":drivers.Reboot?"文件已安装，驱动需要重启后确认就绪":"安装完成，可以开始使用";
            progress(new(2,outcome.Summary));
        }
        catch(Exception ex){outcome.Summary="安装未完成："+ex.Message;outcome.Details.Add(ex.Message);progress(new(2,outcome.Summary));}
        return outcome;
    }
}
