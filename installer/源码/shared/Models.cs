using System.Text.Json;

namespace Burrito.Installer;

public static class Product
{
    public const string Name="CODM 手柄转换器",Version="3.4.3",Marker="Burrito.CODM.Install.v1";
    public const string RegistryKey=@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\CODMControllerProbe";
    public const string RecordName="installation.json",MainName="CODMControllerProbe.exe",UninstallName="CODMControllerProbeUninstaller.exe";
    public static string DefaultRoot=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","Burrito","CODMControllerProbe");
    public static readonly string[] PayloadNames=[UninstallName,MainName,"必看 README.TXT","赞赏码.png","components/ViGEmBus_Setup.exe","components/HidHide_1.5.230_x64.exe"];
    public static readonly JsonSerializerOptions Json=new(){WriteIndented=true};
    public static string Shortcut=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),"Programs","CODM 手柄转换器.lnk");
}
public sealed class InstallRecord
{
    public string Marker {get;set;}=Product.Marker;
    public string Version {get;set;}=Product.Version;
    public string Root {get;set;}="";
    public string InstalledAt {get;set;}=DateTimeOffset.Now.ToString("O");
    public Dictionary<string,bool> DriversPreviouslyPresent {get;set;}=new();
    public bool UninstallPending {get;set;}
    public bool Portable {get;set;}
    public bool RemoveDrivers {get;set;}=true;
    public bool DeleteData {get;set;}=true;
    public long PendingBoot {get;set;}
    public List<string> PendingReasons {get;set;}=[];
    public static InstallRecord Load(string path)
    {
        var r=JsonSerializer.Deserialize<InstallRecord>(File.ReadAllText(path))??throw new IOException("安装记录无效");
        if(r.Marker!=Product.Marker)throw new IOException("安装记录不属于本软件");
        r.Root=SafeFiles.ValidateRoot(r.Root);
        if(!Path.GetFullPath(path).Equals(Path.Combine(r.Root,Product.RecordName),StringComparison.OrdinalIgnoreCase))throw new IOException("安装记录位置与安装目录不一致");
        return r;
    }
    public void Save(){SafeFiles.WriteAtomic(Path.Combine(Root,Product.RecordName),JsonSerializer.Serialize(this,Product.Json));}
}
public sealed record Update(int Step,string Text);
public sealed class Outcome
{
    public bool Success {get;set;}
    public bool RestartRequired {get;set;}
    public bool CompleteCleanup {get;set;}
    public string Summary {get;set;}="";
    public List<string> Details {get;set;}=[];
    public List<string> Retained {get;set;}=[];
}
public static class ExitPolicy
{
    public static bool Success(int code)=>code is 0 or 3010;
    public static bool Reboot(int code)=>code is 3010 or 1641;
    public static long Boot=>DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()-Environment.TickCount64;
}
