using Microsoft.Win32;
using Nefarius.ViGEm.Client;
using System.Text.Json;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CODMControllerProbe;

public sealed record ComponentState(string Name,bool Installed,bool Configured,bool RuntimeReady,bool RestartPending,string Error,bool? Active=null)
{
    public string Detail=>Name+"："+(RuntimeReady?Lang.T("可以调用","Available"):RestartPending?Lang.T("Windows 正在更新此组件","Windows update pending"):!Installed?Lang.T("尚未安装","Not installed"):!Configured?Lang.T("安装配置不完整","Incomplete installation"):Lang.T("暂时无法调用","Not available"))+(Error.Length>0?"；"+Error:"");
}
public sealed record ComponentReadiness(ComponentState Vigem,ComponentState HidHide,bool SetupRestartPending)
{
    public bool NeedsRestart=>SetupRestartPending||Vigem.RestartPending||HidHide.RestartPending;
    public bool Ready=>Vigem.RuntimeReady&&HidHide.RuntimeReady&&!NeedsRestart;
    public string Message=>NeedsRestart?Lang.T("组件已安装，本次更新等待 Windows 重启完成；可稍后重启。","Components are installed; Windows needs a restart to finish this update. You can restart later."):
        !Vigem.Installed||!HidHide.Installed?Lang.T("连接组件尚未完整安装，请运行安装包或准备组件工具。","Connection components are missing; run Setup or Prepare components."):
        !Vigem.Configured||!HidHide.Configured?Lang.T("组件文件已存在，但安装配置不完整，请使用组件准备工具修复。","Component files exist, but configuration is incomplete; repair with Prepare components."):
        !Ready?Lang.T("驱动已安装，但暂时无法调用：","Drivers are installed but unavailable: ")+string.Join("、",new[]{Vigem,HidHide}.Where(s=>!s.RuntimeReady).Select(s=>s.Name))+Lang.T("。请查看下方具体原因；此检查不要求连接手柄。",". See the details below; no controller is required for this check."):
        Lang.T("组件接口已就绪。","Component interfaces are ready.");
    public IEnumerable<string> Details=>new[]{Vigem.Detail,HidHide.Detail};
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
    public static ComponentReadiness Capture(string? root=null)
    {
        var vigem=Read("ViGEmBus");var hid=Read("HidHide");
        // An old installer receipt must not block components which now work.
        // Actual pending driver removal is still reported by each component.
        return new(vigem,hid,!(vigem.RuntimeReady&&hid.RuntimeReady)&&PendingFromRecord(root));
    }
    static ComponentState Read(string name)
    {
        bool installed=false,configured=false,pending=false;
        try
        {
            using var service=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\"+name);
            bool file=File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"drivers",name+".sys"));
            installed=service!=null||file;
            bool enabled=service?.GetValue("Start") is int start&&start!=4;
            bool filter=name!="HidHide";
            if(!filter)foreach(string cls in FilterClasses){using var k=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\"+cls);if(k?.GetValue("UpperFilters") is string[] f&&f.Contains(name,StringComparer.OrdinalIgnoreCase))filter=true;}
            pending=Convert.ToInt32(service?.GetValue("DeleteFlag",0)??0)!=0;
            using(var session=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager"))
            {
                if(session?.GetValue("PendingFileRenameOperations") is string[] paths)
                {string exact=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"drivers",name+".sys");for(int i=0;i+1<paths.Length;i+=2)if(paths[i].Replace(@"\??\","").Equals(exact,StringComparison.OrdinalIgnoreCase))pending=true;}
            }
            configured=service!=null&&file&&enabled&&filter;
            if(!configured||pending)return new(name,installed,configured,false,pending,pending?Lang.T("检测到驱动自身的待更新或待删除记录。","Driver replacement or removal is pending."):Lang.T("服务、驱动文件、启用状态或过滤器登记缺失。","Service, driver file, enabled state or filter registration is missing."));
            var device=DriverDevices.Inspect(name);
            if(!device.Started)return new(name,true,false,false,false,!device.Present
                ?Lang.T("仅有驱动文件和服务，缺少对应的系统设备。请运行新版安装包或准备组件工具修复。","Driver files and service exist, but the system device is missing. Run Setup or Prepare components to repair it.")
                :Lang.T("驱动设备未能启动，Windows 设备问题代码：","The driver device did not start. Windows device problem code: ")+device.Problem+"；"+device.InstanceId);
            if(name=="HidHide")
            {
                using var handle=CreateFile(@"\\.\HidHide",0x80000000,7,IntPtr.Zero,3,0x80,IntPtr.Zero);
                if(handle.IsInvalid)throw new Win32Exception(Marshal.GetLastWin32Error());
                var buffer=new byte[1];
                if(!DeviceIoControl(handle,0x80010000u|0x4000u|(2052u<<2),IntPtr.Zero,0,buffer,1,out _,IntPtr.Zero))throw new Win32Exception(Marshal.GetLastWin32Error());
                return new(name,true,true,true,false,"",buffer[0]==1);
            }
            using var client=new ViGEmClient();
            return new(name,true,true,true,false,"");
        }
        catch(Exception ex)
        {
            string hint=ex is Win32Exception w ? w.NativeErrorCode switch {
                2 or 3=>Lang.T("Windows 尚未提供驱动控制通道；请保存记录供排查。","Windows has not exposed the driver control interface; save the diagnostic log."),
                5=>Lang.T("系统拒绝访问驱动控制通道。","Windows denied access to the driver control interface."),
                32=>Lang.T("驱动控制通道被占用，请关闭其他驱动配置窗口后重试。","The driver control interface is busy; close other driver configuration windows and retry."),
                _=>""}:ex is DllNotFoundException or BadImageFormatException?Lang.T("驱动调用库缺失或无法加载，请使用完整的新版本包。","The driver client library could not be loaded; use the complete release package."):"";
            return new(name,installed,configured,false,pending,(hint.Length>0?hint+" ":"")+ex.GetType().Name+": "+ex.Message+(ex is Win32Exception native?" [Win32="+native.NativeErrorCode+"]":""));
        }
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint disposition,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)]
    static extern bool DeviceIoControl(SafeFileHandle handle,uint code,IntPtr input,uint inputSize,[Out] byte[] output,uint outputSize,out uint returned,IntPtr overlapped);
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
