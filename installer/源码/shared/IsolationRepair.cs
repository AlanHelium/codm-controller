using CODMControllerProbe;
using System.Diagnostics;
using System.Text.Json;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets.DualShock4;
namespace Burrito.Installer;

public sealed record IsolationRepairResult(bool Success,bool Verified,bool Pending,string Message);
public static class IsolationRepair
{
    static readonly JsonSerializerOptions Json=new(){IncludeFields=true,WriteIndented=true};
    static bool Same(IEnumerable<string> a,IEnumerable<string> b)=>a.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(b);
    static bool DeniedApp(string image)=>new[]{"dotnet.exe","codm.exe","codmobile.exe","cod.exe","codhq.exe","aow_exe.exe","gameloop.exe","androidemulator.exe","androidemulatoren.exe","androidemulatorex.exe","chrome.exe","msedge.exe","firefox.exe"}.Contains(Path.GetFileName(image),StringComparer.OrdinalIgnoreCase);
    public static bool NeedsRepair(string? root,Action<string> log)
    {
        if(root==null){log("未找到程序目录，无法核对转换器白名单路径");return true;}
        var state=HidHideManager.TakeSnapshot();if(!state.Success){log(state.Error);return true;}
        string image=HidHideManager.ToDriverImagePath(Path.Combine(root,Product.MainName));
        bool missing=!state.Whitelist.Contains(image,StringComparer.OrdinalIgnoreCase);
        log("隔离配置：反向模式="+state.Inverse+"；当前程序白名单="+!missing);
        // An attached controller requires a fresh access check, not just matching rule text.
        var devices=DeviceCatalog.Enumerate();
        return state.Inverse||missing||state.Whitelist.Any(DeniedApp)||devices.Any(d=>d.Virtual&&state.Blacklist.Contains(d.Id,StringComparer.OrdinalIgnoreCase))||devices.Any(d=>!d.Virtual&&!d.Protected&&d.Gamepad);
    }
    static void Apply(HidHideManager.SnapshotResult s)
    {
        if(!HidHideManager.SetWhitelist(s.Whitelist)||!HidHideManager.SetBlacklist(s.Blacklist)||!HidHideManager.SetInverse(s.Inverse)||!HidHideManager.SetActive(s.Active))throw new IOException("HidHide规则写入失败");
        var read=HidHideManager.TakeSnapshot();
        if(!read.Success||read.Active!=s.Active||read.Inverse!=s.Inverse||!Same(read.Whitelist,s.Whitelist)||!Same(read.Blacklist,s.Blacklist))throw new IOException("HidHide规则读回校验失败");
    }
    public static async Task<IsolationRepairResult> Run(string? root,Action<string> log,bool verifyHardware=true)
    {
        if(root==null)return new(false,false,true,"未找到程序目录；请从软件内修复入口运行，或将完整修复包放在已登记程序之外的独立目录");
        string target=SafeFiles.Child(root,Product.MainName);if(!File.Exists(target))return new(false,false,true,"转换器主程序缺失，请使用完整独立修复包恢复程序文件");
        if(!HidHideManager.RecoverPreviousSession(out bool recovered,out string recoveryError))return new(false,false,false,recoveryError);
        if(recovered)log("已恢复上次中断的隔离会话");
        var before=HidHideManager.TakeSnapshot();if(!before.Success)return new(false,false,false,before.Error);
        string reportRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CODMControllerProbe","maintenance","isolation-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(reportRoot);SafeFiles.WriteAtomic(Path.Combine(reportRoot,"before.json"),JsonSerializer.Serialize(before,Json));
        var normalized=new HidHideManager.SnapshotResult{Success=true,Active=before.Active,Inverse=false,Whitelist=before.Whitelist.Where(p=>!DeniedApp(p)).ToList(),Blacklist=before.Blacklist.ToList()};
        string app=HidHideManager.ToDriverImagePath(target),self=HidHideManager.ToDriverImagePath(Environment.ProcessPath!);
        if(!normalized.Whitelist.Contains(app,StringComparer.OrdinalIgnoreCase))normalized.Whitelist.Add(app);
        bool session=false,complete=false;
        try
        {
            // A temporary exemption lets this maintenance process see all HID collections.
            var discover=new HidHideManager.SnapshotResult{Success=true,Active=before.Active,Inverse=false,Whitelist=normalized.Whitelist.Append(self).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),Blacklist=before.Blacklist};
            Apply(discover);
            var devices=DeviceCatalog.Enumerate();
            var virtualIds=devices.Where(d=>d.Virtual).SelectMany(d=>d.Ancestors.Append(d.Id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            normalized.Blacklist=before.Blacklist.Where(id=>!virtualIds.Contains(id)).ToList();
            Apply(normalized);
            log("已核对当前EXE的驱动路径，修正反向模式，排除游戏/浏览器及共享宿主的错误豁免，移除在场虚拟设备隐藏项");
            if(!verifyHardware){complete=true;return new(true,false,false,"程序、组件和隔离规则已准备完成；启动转换时验证手柄输入");}
            var ids=DeviceCatalog.HideCandidates(devices);
            if(ids.Count==0){complete=true;return new(true,false,true,"隔离规则已修复；未接入可识别的实体手柄，实际隔离验证待接入");}
            IsolationProbe.StartWatchdog();
            if(HidHideManager.HideDevices(ids)==0)throw new IOException(HidHideManager.LastError);
            session=true;
            using var client=new ViGEmClient();var pad=client.CreateDualShock4Controller();pad.AutoSubmitReport=false;
            using var outputCancel=new CancellationTokenSource();Task? outputTask=null;
            try
            {
                pad.Connect();pad.ResetReport();pad.SubmitReport();
                outputTask=Task.Run(async()=>{while(!outputCancel.IsCancellationRequested){pad.SubmitReport();await Task.Delay(8,outputCancel.Token);}});await Task.Delay(350);
                // Add the newly created virtual controller to the exclusion check too.
                var virtualNow=DeviceCatalog.Enumerate().Where(d=>d.Virtual).SelectMany(d=>d.Ancestors.Append(d.Id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var applied=HidHideManager.TakeSnapshot();if(!applied.Success)throw new IOException(applied.Error);
                if(!HidHideManager.SetBlacklist(applied.Blacklist.Where(id=>!virtualNow.Contains(id))))throw new IOException("虚拟DS4排除规则写入失败");
                var outside=IsolationProbe.Verify(ids);
                SafeFiles.WriteAtomic(Path.Combine(reportRoot,"outside-access.json"),JsonSerializer.Serialize(outside,Json));
                log("独立非白名单进程："+outside.Message);
                if(!outside.Verified)throw new IOException(outside.Message);
                string insideFile=Path.Combine(reportRoot,"converter-access.json");
                var start=new ProcessStartInfo(target){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};start.ArgumentList.Add("--repair-access-report");start.ArgumentList.Add(insideFile);
                using var process=Process.Start(start)??throw new IOException("无法启动转换器读取验证");
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));
                try{await process.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException){try{process.Kill();}catch{}throw new IOException("转换器读取验证超时，未确认修复成功");}
                if(process.ExitCode!=0||!File.Exists(insideFile))throw new IOException("当前转换器不支持读取验证或未正常返回，请使用同版完整修复包");
                var inside=JsonSerializer.Deserialize<IsolationResult>(File.ReadAllText(insideFile),Json)??throw new IOException("转换器读取报告无效");
                var expected=devices.Where(d=>ids.Contains(d.Id,StringComparer.OrdinalIgnoreCase)&&d.Path.Length>0&&d.Gamepad).Select(d=>d.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if(expected.Count==0||expected.Any(id=>!inside.Access.Any(a=>a.Id.Equals(id,StringComparison.OrdinalIgnoreCase)&&a.Error==0)))throw new IOException("隐藏开启后，转换器未能重新打开所有实体游戏手柄接口");
                if(!inside.Access.Any(a=>a.Virtual&&a.Error==0))throw new IOException("转换器未能读取虚拟手柄接口");
                log("验证通过：转换器可重开实体手柄；外部进程实体读取被拒绝；虚拟DS4报告可读");
                complete=true;return new(true,true,false,"白名单与隔离修复完成，实际读取验证通过");
            }
            finally{outputCancel.Cancel();if(outputTask!=null)try{await outputTask;}catch(OperationCanceledException){}finally{try{pad.Disconnect();}catch{}}}
        }
        catch(Exception ex){log("隔离验证未通过："+ex.Message);return new(false,false,false,ex.Message+"；记录："+reportRoot);}
        finally
        {
            bool restored=!session||HidHideManager.Restore();
            try
            {
                if(!restored)throw new IOException("临时隔离恢复失败："+HidHideManager.LastError);
                Apply(complete?normalized:before);
                log("临时隐藏已结束，规则已读回核对；已恢复验证前的待机状态");
            }
            catch(Exception ex){throw new IOException("隔离收尾未完成，请保留恢复记录："+reportRoot+"；"+ex.Message,ex);}
        }
    }
}
