using Microsoft.Win32;
using Nefarius.ViGEm.Client;
using System.Text.Json;

namespace CODMControllerProbe;

public sealed record ComponentState(string Name,bool Installed,bool Configured,bool RuntimeReady,bool RestartPending,string Error,bool? Active=null);
public sealed record ComponentReadiness(ComponentState Vigem,ComponentState HidHide,bool SetupRestartPending)
{
    public bool NeedsRestart=>SetupRestartPending||Vigem.RestartPending||HidHide.RestartPending;
    public bool Ready=>Vigem.RuntimeReady&&HidHide.RuntimeReady&&!NeedsRestart;
    public string Message=>NeedsRestart?Lang.T("组件已安装，本次更新等待 Windows 重启完成；可稍后重启。","Components are installed; Windows needs a restart to finish this update. You can restart later."):
        !Vigem.Installed||!HidHide.Installed?Lang.T("连接组件尚未完整安装，请运行安装包或准备组件工具。","Connection components are missing; run Setup or Prepare components."):
        !Vigem.Configured||!HidHide.Configured?Lang.T("组件文件已存在，但安装配置不完整，请使用组件准备工具修复。","Component files exist, but configuration is incomplete; repair with Prepare components."):
        !Ready?Lang.T("组件已安装，接口尚未就绪。请稍候并重新检查；手柄已连接时可拔插一次。","Components are installed, but interfaces are not ready. Wait and recheck; reconnect an attached controller."):
        Lang.T("组件接口已就绪。","Component interfaces are ready.");
}

// Read-only verification, shared by Setup and the app. A disabled hiding switch
// does not mean HidHide is missing, and no virtual controller is created here.
public static class ComponentProbe
{
    static readonly string[] FilterClasses=["{745a17a0-74d3-11d0-b6fe-00a0c90f57da}","{d61ca365-5af4-4486-998b-9db4734c6ca3}","{05f5cfe2-4733-4950-a6bb-07aad01a3a84}"];
    public static long BootStamp=>DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()-Environment.TickCount64;
    public static bool SameBoot(long recorded,long current)=>recorded>0&&Math.Abs(current-recorded)<5000;
    public static bool PendingFromRecord(string? root)
    {
        if(string.IsNullOrWhiteSpace(root))return false;
        try
        {
            using var record=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"installation.json")));
            var value=record.RootElement;
            return value.GetProperty("Marker").GetString()=="Burrito.CODM.Install.v1"&&
                Path.GetFullPath(value.GetProperty("Root").GetString()!).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar).Equals(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar),StringComparison.OrdinalIgnoreCase)&&
                value.TryGetProperty("ComponentRestartRequired",out var pending)&&pending.GetBoolean()&&
                value.TryGetProperty("ComponentPreparedBoot",out var boot)&&SameBoot(boot.GetInt64(),BootStamp);
        }
        catch{return false;}
    }
    public static ComponentReadiness Capture(string? root=null)=>new(Read("ViGEmBus"),Read("HidHide"),PendingFromRecord(root));
    static ComponentState Read(string name)
    {
        try
        {
            using var service=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\"+name);
            bool file=File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"drivers",name+".sys"));
            bool installed=service!=null||file;
            bool enabled=service?.GetValue("Start") is int start&&start!=4;
            bool filter=name!="HidHide";
            if(!filter)foreach(string cls in FilterClasses){using var k=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\"+cls);if(k?.GetValue("UpperFilters") is string[] f&&f.Contains(name,StringComparer.OrdinalIgnoreCase))filter=true;}
            bool pending=Convert.ToInt32(service?.GetValue("DeleteFlag",0)??0)!=0;
            using(var session=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager"))
            {
                if(session?.GetValue("PendingFileRenameOperations") is string[] paths)
                {string exact=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"drivers",name+".sys");for(int i=0;i+1<paths.Length;i+=2)if(paths[i].Replace(@"\??\","").Equals(exact,StringComparison.OrdinalIgnoreCase))pending=true;}
            }
            bool configured=service!=null&&file&&enabled&&filter;
            if(!configured||pending)return new(name,installed,configured,false,pending,"");
            if(name=="HidHide")
            {
                bool ready=HidHideManager.TryGetActive(out bool active);
                return new(name,true,true,ready,false,ready?"":"HidHide control interface is not available",ready?active:null);
            }
            using var client=new ViGEmClient();
            return new(name,true,true,true,false,"");
        }
        catch(Exception ex)
        {
            // Preserve installation information even if the actual interface failed.
            bool installed=false;try{using var k=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\"+name);installed=k!=null;}catch{}
            return new(name,installed,installed,false,false,ex.Message);
        }
    }
    public static async Task<ComponentReadiness> WaitAsync(string? root,int milliseconds=6000)
    {
        long deadline=Environment.TickCount64+milliseconds;
        while(true)
        {
            var state=await Task.Run(()=>Capture(root));
            if(state.Ready||state.NeedsRestart||!state.Vigem.Configured||!state.HidHide.Configured||Environment.TickCount64>=deadline)return state;
            await Task.Delay(400);
        }
    }
}
