using Microsoft.Win32;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;

namespace Burrito.Installer;

public sealed record Driver(string Name,string File,string Hash);
public sealed record DriverState(string Name,bool Service,bool Enabled,bool SysFile,bool Filter,List<string> Products,List<string> InfPackages)
{
    public bool Ready=>Service&&Enabled&&SysFile&&(Name!="HidHide"||Filter);
    public bool HasResidue=>Service||SysFile||Filter||Products.Count>0||InfPackages.Count>0;
    public string Summary=>$"服务{(Service?"在":"无")} / 文件{(SysFile?"在":"无")} / 过滤器{(Filter?"在":"无")} / 安装项{Products.Count} / 驱动包{InfPackages.Count}";
}
public sealed class DriverOutcome
{
    public bool Success {get;set;}=true;
    public bool Reboot {get;set;}
    public List<string> Problems {get;set;}=[];
    public List<DriverState> States {get;set;}=[];
}
public static class Drivers
{
    public static readonly Driver[] Packages=[new("ViGEmBus","ViGEmBus_Setup.exe","89220A7865076B342892F98865F3499FB7C4CFD673159E89D352C360FD014C6A"),new("HidHide","HidHide_1.5.230_x64.exe","F4BBBCB82E6258641B887C74BC81C4C5F66E4AA811808DFC304347687B7605F6")];
    private static readonly string[] FilterClasses=["{745a17a0-74d3-11d0-b6fe-00a0c90f57da}","{d61ca365-5af4-4486-998b-9db4734c6ca3}","{05f5cfe2-4733-4950-a6bb-07aad01a3a84}"];
    public static bool IsTargetInf(string content,string service)=>Regex.IsMatch(content,@"(?im)^\s*AddService\s*=\s*"+Regex.Escape(service)+@"\s*,")&&content.Contains("Nefarius",StringComparison.OrdinalIgnoreCase);
    public static bool MatchesProduct(string name,string publisher,string driver)=>publisher.Contains("Nefarius",StringComparison.OrdinalIgnoreCase)&&(driver=="HidHide"?name.Equals("HidHide",StringComparison.OrdinalIgnoreCase):name.Equals("Nefarius Virtual Gamepad Emulation Bus Driver",StringComparison.OrdinalIgnoreCase)||name.Equals("ViGEm Bus Driver",StringComparison.OrdinalIgnoreCase));
    public static DriverState Inspect(string driver)
    {
        using var service=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\"+driver);
        bool enabled=service?.GetValue("Start") is int start&&start!=4;
        bool filter=false;if(driver=="HidHide")foreach(string cls in FilterClasses)
        {using var key=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\"+cls);if(key?.GetValue("UpperFilters") is string[] filters&&filters.Contains("HidHide",StringComparer.OrdinalIgnoreCase))filter=true;}
        var products=new List<string>();
        foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32})
        {
            using var machine=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,view);using var root=machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");if(root==null)continue;
            foreach(string name in root.GetSubKeyNames())using(var entry=root.OpenSubKey(name))
            {if(entry==null)continue;string display=entry.GetValue("DisplayName") as string??"",publisher=entry.GetValue("Publisher") as string??"";
             if(MatchesProduct(display,publisher,driver)&&Guid.TryParse(name,out var id)&&Convert.ToInt32(entry.GetValue("WindowsInstaller",0))==1)products.Add(id.ToString("B"));}
        }
        var infs=new List<string>();string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"INF");
        foreach(string file in Directory.EnumerateFiles(folder,"oem*.inf"))
        {string content=File.ReadAllText(file);if(IsTargetInf(content,driver))infs.Add(Path.GetFileName(file));}
        return new(driver,service!=null,enabled,File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"drivers",driver+".sys")),filter,products.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),infs);
    }
    private static void VerifyVendor(string path,Driver d)
    {
        SafeFiles.NoLinks(path);using var stream=File.OpenRead(path);
        if(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream))!=d.Hash)throw new IOException("官方驱动包校验失败："+d.Name);
    }
    private static bool PendingFileDeletion(string driver)
    {
        using var key=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager");
        if(key?.GetValue("PendingFileRenameOperations") is not string[] paths)return false;
        string expected=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"drivers",driver+".sys");
        for(int i=0;i+1<paths.Length;i+=2)if(paths[i+1].Length==0&&paths[i].Replace(@"\??\","").Equals(expected,StringComparison.OrdinalIgnoreCase))return true;
        return false;
    }
    public static bool RemovalPending(string driver)
    {
        using var key=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\"+driver);
        return Convert.ToInt32(key?.GetValue("DeleteFlag",0)??0)!=0||PendingFileDeletion(driver);
    }
    public static bool NeedsRebootAfterRemoval(DriverState state,bool loaded)=>state.Products.Count==0&&state.InfPackages.Count==0&&!state.Filter&&state.Service&&loaded;
    public static bool IsOrphanCandidate(DriverState state)=>state.Products.Count==0&&state.InfPackages.Count==0&&!state.Filter&&(state.Service||state.SysFile);
    private static bool HasReference(string name)
    {
        bool Scan(RegistryKey key,bool devices,int depth,int maximum){
            foreach(string value in devices?new[]{"UpperFilters","LowerFilters","Service"}:new[]{"UpperFilters","LowerFilters","DependOnService"}) {
                object? item=key.GetValue(value);if(item is string text&&text.Equals(name,StringComparison.OrdinalIgnoreCase)||item is string[] list&&list.Contains(name,StringComparer.OrdinalIgnoreCase))return true;
            }
            if(depth<maximum)foreach(string sub in key.GetSubKeyNames()){
                if(devices&&sub.Equals("Properties",StringComparison.OrdinalIgnoreCase))continue;
                if(!devices&&maximum==1&&!Regex.IsMatch(sub,@"^\{[0-9A-Fa-f-]+\}$")&&key.Name.EndsWith(@"\Class",StringComparison.OrdinalIgnoreCase))continue;
                using var child=key.OpenSubKey(sub)??throw new IOException("无法完整核对驱动引用："+key.Name+"\\"+sub);
                if(Scan(child,devices,depth+1,maximum))return true;
            }return false;
        }
        foreach(var item in new[]{(@"SYSTEM\CurrentControlSet\Control\Class",false,1),(@"SYSTEM\CurrentControlSet\Enum",true,3),(@"SYSTEM\CurrentControlSet\Services",false,1)}) {
            using var key=Registry.LocalMachine.OpenSubKey(item.Item1)??throw new IOException("无法核对驱动引用");if(Scan(key,item.Item2,0,item.Item3))return true;
        }
        return false;
    }
    private static async Task<bool> CleanOrphan(DriverState state,DriverOutcome result,Action<string> log)
    {
        if(!IsOrphanCandidate(state)||HasReference(state.Name))return false;
        string file=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"drivers",state.Name+".sys");SafeFiles.NoLinks(file);
        if(state.SysFile) {
            using var stream=File.OpenRead(file);string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
            // The SHA256 is the WHQL-signed HidHide 1.4.181 driver retained by the pinned official package.
            if(state.Name!="HidHide"||hash!="E3BB8B6A4E4FF90290B34E710EDBEF88CF71CEC6BA0CADCB8E1685D5DD65DDB2")return false;
        }
        log(state.Name+" 已无官方安装项、驱动包、过滤器及设备引用，正常清理核验过的孤立组件");
        if(state.Service) {
            IntPtr manager=OpenSCManager(null,null,1);if(manager==IntPtr.Zero)throw new IOException("无法打开服务管理器");
            try {
                IntPtr service=OpenService(manager,state.Name,0x10024);
                if(service==IntPtr.Zero){int error=Marshal.GetLastWin32Error();if(error==1072){result.Reboot=true;log("服务已标记删除，等待重启复查");return true;}if(error!=1060)throw new IOException("无法正常维护孤立服务："+error);}
                else try {
                    using(var key=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\"+state.Name)) {
                        string image=key?.GetValue("ImagePath") as string??"";
                        if(!image.Equals(@"\SystemRoot\System32\drivers\"+state.Name+".sys",StringComparison.OrdinalIgnoreCase)&&!image.Equals(file,StringComparison.OrdinalIgnoreCase))throw new IOException("驱动服务路径不匹配，未清理");
                        if(Convert.ToInt32(key?.GetValue("Type",0))!=1)throw new IOException("目标不是内核驱动服务，未清理");
                    }
                    if(!QueryServiceStatus(service,out var status))throw new IOException("无法核对服务停止状态");
                    if(status.State!=1){if(!ControlService(service,1,out _)){int error=Marshal.GetLastWin32Error();result.Reboot=true;
                        if(!DeleteService(service)&&Marshal.GetLastWin32Error()!=1072)throw new IOException("驱动无法正常停止，且不能登记重启清理");
                        if(state.SysFile&&!MoveFileEx(file,null,4))log("系统管理的驱动文件将在重启释放后通过 Windows Installer 清理，不更改系统权限");
                        log("Windows 无法正常停止驱动（"+error+"），服务与已核验文件已安排重启释放后清理；尚未宣称完成");return true;}
                        for(int i=0;i<40;i++){await Task.Delay(100);if(!QueryServiceStatus(service,out status))throw new IOException("无法核对驱动停止结果");if(status.State==1)break;}}
                    if(status.State!=1){result.Reboot=true;log("驱动仍未停止，等待重启后复查");return true;}
                    if(!DeleteService(service)&&Marshal.GetLastWin32Error()!=1072)throw new IOException("停止后的服务登记移除失败");
                } finally{CloseServiceHandle(service);}
            } finally{CloseServiceHandle(manager);}
        }
        if(Loaded(state.Name)||HasReference(state.Name)){result.Reboot=true;log("组件仍被 Windows 引用，保留文件等待复查");return true;}
        if(File.Exists(file))try{File.Delete(file);}catch(UnauthorizedAccessException){int code=await OrphanFileCleanup.Remove(log);result.Reboot|=ExitPolicy.Reboot(code);if(!ExitPolicy.Success(code))throw new IOException("Windows Installer 孤立文件清理未完成："+code);}
        log(state.Name+" 已停止，核验过的孤立驱动文件已移除");return true;
    }
    public static bool Loaded(string name)
    {
        IntPtr manager=OpenSCManager(null,null,1);if(manager==IntPtr.Zero)throw new IOException("无法核对驱动服务状态");
        try{IntPtr service=OpenService(manager,name,4);if(service==IntPtr.Zero){int error=Marshal.GetLastWin32Error();if(error is 1060 or 1072)return error==1072;throw new IOException("驱动服务状态查询失败："+error);}
        try{if(!QueryServiceStatus(service,out var status))throw new IOException("无法查询驱动运行状态");return status.State!=1;}finally{CloseServiceHandle(service);}}
        finally{CloseServiceHandle(manager);}
    }
    [StructLayout(LayoutKind.Sequential)]private struct ServiceStatus{public uint Type,State,Accepted,Win32Exit,ServiceExit,Checkpoint,WaitHint;}
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern IntPtr OpenSCManager(string? machine,string? database,uint access);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern IntPtr OpenService(IntPtr manager,string name,uint access);
    [DllImport("advapi32.dll",SetLastError=true)]private static extern bool QueryServiceStatus(IntPtr service,out ServiceStatus status);
    [DllImport("advapi32.dll",SetLastError=true)]private static extern bool ControlService(IntPtr service,uint control,out ServiceStatus status);
    [DllImport("advapi32.dll",SetLastError=true)]private static extern bool DeleteService(IntPtr service);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern bool MoveFileEx(string existing,string? target,uint flags);
    [DllImport("advapi32.dll")]private static extern bool CloseServiceHandle(IntPtr handle);
    public static async Task<DriverOutcome> Install(string root,Action<string> log)
    {
        var result=new DriverOutcome();
        foreach(var d in Packages)
        {
            if(RemovalPending(d.Name)){result.Reboot=true;log(d.Name+" 上次移除仍待重启，请重启后再安装");break;}
            var before=Inspect(d.Name);if(before.Ready){log(d.Name+" 已就绪，复用已有组件");result.States.Add(before);continue;}
            string vendor=SafeFiles.Child(root,"components/"+d.File);VerifyVendor(vendor,d);
            string logfile=Path.Combine(root,d.Name+"-install.log");log("正在安装或修复 "+d.Name);
            int code;
            if(before.Products.Count>0)
                code=await WindowsIntegration.Run(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"msiexec.exe"),["/fa",before.Products[0],"/qn","/norestart","REBOOT=ReallySuppress","/L*v",logfile],log);
            else
                code=await WindowsIntegration.Run(vendor,["/exenoui","/qn","/norestart","REBOOT=ReallySuppress","/L*v",logfile],log);
            result.Reboot|=ExitPolicy.Reboot(code);
            if(!ExitPolicy.Success(code)){result.Success=false;result.Problems.Add(d.Name+" 安装未完成（"+code+"）");break;}
            var after=Inspect(d.Name);result.States.Add(after);log(d.Name+"："+after.Summary);
            if(!after.Ready&&!result.Reboot){result.Success=false;result.Problems.Add(d.Name+" 未通过组件就绪检查");break;}
        }
        return result;
    }
    public static async Task<DriverOutcome> Remove(string root,Action<string> log)
    {
        var result=new DriverOutcome();
        if(!await OrphanFileCleanup.RemoveRegistered(log)){result.Success=false;result.Problems.Add("先前的临时维护安装项尚未清除");return result;}
        foreach(var d in Packages.Reverse())
        {
            var state=Inspect(d.Name);if(!state.HasResidue){log(d.Name+" 未检测到残留");result.States.Add(state);continue;}
            if(await CleanOrphan(state,result,log)) {
                var verified=Inspect(d.Name);result.States.Add(verified);log(d.Name+" 孤立组件复查："+verified.Summary);
                if(verified.HasResidue&&!result.Reboot){result.Success=false;result.Problems.Add(d.Name+" 孤立组件尚有残留");}continue;
            }
            if(RemovalPending(d.Name)||NeedsRebootAfterRemoval(state,Loaded(d.Name)))
            {result.Reboot=true;result.States.Add(state);log(d.Name+" 官方登记与加载状态表明移除待重启；重启后仍须复查");continue;}
            if(state.Products.Count==0&&(state.Service||state.Filter||state.SysFile))
            {
                // Repair registration through the official signed package; never manually delete a live filter/service/sys file.
                log(d.Name+" 官方卸载注册缺失，先通过官方包修复卸载入口");
                string vendor=SafeFiles.Child(root,"components/"+d.File);VerifyVendor(vendor,d);
                int repair=await WindowsIntegration.Run(vendor,["/exenoui","/qn","/norestart","REBOOT=ReallySuppress"],log);
                result.Reboot|=ExitPolicy.Reboot(repair);state=Inspect(d.Name);
                if(!ExitPolicy.Success(repair)||state.Products.Count==0){result.Success=false;result.Problems.Add(d.Name+" 官方卸载入口修复未完成，已保留文件供重试");result.States.Add(state);continue;}
            }
            foreach(string product in state.Products)
            {
                log("通过 Windows Installer 卸载 "+d.Name);
                int code=await WindowsIntegration.Run(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"msiexec.exe"),["/x",product,"/qn","/norestart","REBOOT=ReallySuppress"],log);
                result.Reboot|=ExitPolicy.Reboot(code);
                if(!ExitPolicy.Success(code)&&code!=1605){result.Success=false;result.Problems.Add(d.Name+" 官方卸载失败（"+code+"）");}
            }
            var after=Inspect(d.Name);
            if(!after.Filter&&after.Products.Count==0)
            {
                foreach(string inf in after.InfPackages)
                {
                    if(!Regex.IsMatch(inf,@"^oem[0-9]+\.inf$",RegexOptions.IgnoreCase))throw new IOException("驱动包名称校验失败");
                    log("清理精确识别的 "+d.Name+" 驱动包："+inf);
                    int code=await WindowsIntegration.Run(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"pnputil.exe"),["/delete-driver",inf,"/uninstall"],log);
                    result.Reboot|=ExitPolicy.Reboot(code);
                    if(!ExitPolicy.Success(code)){result.Success=false;result.Problems.Add(d.Name+" 驱动包移除未完成（"+code+"）");}
                }
            }
            after=Inspect(d.Name);result.States.Add(after);log(d.Name+" 卸载复查："+after.Summary);
            if(await CleanOrphan(after,result,log)){after=Inspect(d.Name);log(d.Name+" 最终复查："+after.Summary);}
            if(RemovalPending(d.Name)||NeedsRebootAfterRemoval(after,Loaded(d.Name))){result.Reboot=true;log(d.Name+" 仍由 Windows 加载或标记删除，须重启释放后复查");}
            if(after.SysFile&&PendingFileDeletion(d.Name)){result.Reboot=true;log(d.Name+" 驱动文件已由系统安排重启删除，尚未宣称清理完成");}
            if(after.HasResidue&&!result.Reboot){result.Success=false;result.Problems.Add(d.Name+" 仍有残留，需要继续处理");}
        }
        return result;
    }
}
