using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Burrito.Installer;

public static class WindowsIntegration
{
    public static void EnsureIdle()
    {
        if(Mutex.TryOpenExisting(@"Global\CODMControllerProbe_SingleInstance",out var m))using(m)
        {try{if(!m.WaitOne(0))throw new IOException("请先正常退出手柄转换器");m.ReleaseMutex();}catch(AbandonedMutexException){m.ReleaseMutex();}}
        foreach(var p in Process.GetProcesses())using(p)
        {try{if(new[]{"codm","codmobile","cod","codhq","aow_exe","gameloop","androidemulator","androidemulatoren","androidemulatorex"}.Contains(p.ProcessName,StringComparer.OrdinalIgnoreCase))throw new IOException("请先退出游戏及模拟器："+p.ProcessName);}catch(InvalidOperationException){}}
    }
    public static async Task<int> Run(string exe,IEnumerable<string> arguments,Action<string> log)
    {
        var start=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(string a in arguments)start.ArgumentList.Add(a);
        using var p=Process.Start(start)??throw new IOException("无法启动组件维护程序");
        var stdout=p.StandardOutput.ReadToEndAsync();var stderr=p.StandardError.ReadToEndAsync();var wait=p.WaitForExitAsync();int elapsed=0;
        while(await Task.WhenAny(wait,Task.Delay(5000))!=wait){elapsed+=5;log($"Windows 正在处理（{elapsed} 秒），请勿强制关闭");}
        await wait;string output=(await stdout)+(await stderr);if(!string.IsNullOrWhiteSpace(output))log(output.Trim());log("组件维护返回码："+p.ExitCode);return p.ExitCode;
    }
    public static void Register(InstallRecord r)
    {
        using var machine=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry64);using var k=machine.CreateSubKey(Product.RegistryKey);
        k.SetValue("DisplayName",Product.Name);k.SetValue("DisplayVersion",Product.Version);k.SetValue("Publisher","Burrito");k.SetValue("InstallLocation",r.Root);
        k.SetValue("DisplayIcon",Path.Combine(r.Root,Product.MainName));k.SetValue("UninstallString","\""+Path.Combine(r.Root,Product.UninstallName)+"\"");k.SetValue("NoModify",1);k.SetValue("NoRepair",1);
        k.SetValue("EstimatedSize",(int)(Product.PayloadNames.Sum(n=>new FileInfo(SafeFiles.Child(r.Root,n)).Length)/1024));
    }
    public static void CreateShortcut(string root,string exe,string link)
    {
        UnicodeShortcut.Create(root,exe,link);
    }
    public static void Unregister(InstallRecord r)
    {
        using var machine=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry64);using var k=machine.OpenSubKey(Product.RegistryKey);
        if(r.Portable){using var portableRunOnce=machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce",true);portableRunOnce?.DeleteValue(CleanupName(r),false);return;}
        string? location=k?.GetValue("InstallLocation") as string;
        if(location!=null&&!Path.GetFullPath(location).Equals(r.Root,StringComparison.OrdinalIgnoreCase))throw new IOException("卸载入口属于另一安装目录，未删除");
        k?.Close();machine.DeleteSubKeyTree(Product.RegistryKey,false);
        if(File.Exists(Product.Shortcut))File.Delete(Product.Shortcut);
        using var runOnce=machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce",true);runOnce?.DeleteValue("CODMControllerProbeCleanup",false);
    }
    public static string? ResumeAfterReboot(InstallRecord r)
    {
        using var machine=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry64);using var key=machine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce");
        key.SetValue(CleanupName(r),"\""+Path.Combine(r.Root,Product.UninstallName)+"\" --resume");
        return r.Portable?null:UnicodeShortcut.TryCreate(r.Root,Product.UninstallName,Product.Shortcut);
    }
    public static string? RecoveryRegistration(InstallRecord r)
    {
        if(r.Portable)return null;
        using var machine=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry64);using var k=machine.CreateSubKey(Product.RegistryKey);
        k.SetValue("DisplayName",Product.Name+"（清理待完成）");k.SetValue("DisplayVersion",Product.Version);k.SetValue("Publisher","Burrito");k.SetValue("InstallLocation",r.Root);k.SetValue("UninstallString","\""+Path.Combine(r.Root,Product.UninstallName)+"\"");
        return UnicodeShortcut.TryCreate(r.Root,Product.UninstallName,Product.Shortcut);
    }
    private static string CleanupName(InstallRecord r)=>r.Portable?"CODMControllerProbeCleanup-"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(r.Root.ToUpperInvariant())))[..12]:"CODMControllerProbeCleanup";
    public static bool Restart()
    {
        if(!OpenProcessToken(Process.GetCurrentProcess().Handle,0x28,out var token))return false;
        try{if(!LookupPrivilegeValue(null,"SeShutdownPrivilege",out var luid))return false;var privilege=new TokenPrivileges{Count=1,Luid=luid,Attributes=2};if(!AdjustTokenPrivileges(token,false,ref privilege,0,IntPtr.Zero,IntPtr.Zero)||Marshal.GetLastWin32Error()!=0)return false;return InitiateSystemShutdownEx(null,"CODM 手柄组件维护需要重启，请保存工作。",30,false,true,0x80040002);}
        finally{CloseHandle(token);}
    }
    [StructLayout(LayoutKind.Sequential)]private struct Luid{public uint Low;public int High;}
    [StructLayout(LayoutKind.Sequential)]private struct TokenPrivileges{public uint Count;public Luid Luid;public uint Attributes;}
    [DllImport("advapi32.dll",SetLastError=true)]private static extern bool OpenProcessToken(IntPtr process,uint access,out IntPtr token);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern bool LookupPrivilegeValue(string? system,string name,out Luid luid);
    [DllImport("advapi32.dll",SetLastError=true)]private static extern bool AdjustTokenPrivileges(IntPtr token,bool disable,ref TokenPrivileges privilege,uint length,IntPtr previous,IntPtr returned);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern bool InitiateSystemShutdownEx(string? machine,string message,uint timeout,bool force,bool reboot,uint reason);
    [DllImport("kernel32.dll")]private static extern bool CloseHandle(IntPtr handle);
}
