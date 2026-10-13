using System.Runtime.InteropServices;
namespace Burrito.Installer;

// No custom action or executable is embedded. Windows Installer removes one fixed file
// in its normal system context after Drivers has verified its hash and stopped state.
public static class OrphanFileCleanup
{
    public static string Create(string path)
    {
        string product=Guid.NewGuid().ToString("B").ToUpperInvariant();
        int code=MsiOpenDatabase(path,new IntPtr(3),out uint db);Check(code);
        try {
            void Sql(string sql){Check(MsiDatabaseOpenView(db,sql,out uint view));try{Check(MsiViewExecute(view,0));}finally{MsiCloseHandle(view);}}
            Sql("CREATE TABLE `Property` (`Property` CHAR(72) NOT NULL, `Value` CHAR(0) LOCALIZABLE PRIMARY KEY `Property`)");
            Sql("CREATE TABLE `Directory` (`Directory` CHAR(72) NOT NULL, `Directory_Parent` CHAR(72), `DefaultDir` CHAR(255) NOT NULL LOCALIZABLE PRIMARY KEY `Directory`)");
            Sql("CREATE TABLE `Component` (`Component` CHAR(72) NOT NULL, `ComponentId` CHAR(38), `Directory_` CHAR(72) NOT NULL, `Attributes` SHORT NOT NULL, `Condition` CHAR(255), `KeyPath` CHAR(72) PRIMARY KEY `Component`)");
            Sql("CREATE TABLE `Feature` (`Feature` CHAR(38) NOT NULL, `Feature_Parent` CHAR(38), `Title` CHAR(64) LOCALIZABLE, `Description` CHAR(255) LOCALIZABLE, `Display` SHORT, `Level` SHORT NOT NULL, `Directory_` CHAR(72), `Attributes` SHORT NOT NULL PRIMARY KEY `Feature`)");
            Sql("CREATE TABLE `FeatureComponents` (`Feature_` CHAR(38) NOT NULL, `Component_` CHAR(72) NOT NULL PRIMARY KEY `Feature_`, `Component_`)");
            Sql("CREATE TABLE `File` (`File` CHAR(72) NOT NULL, `Component_` CHAR(72) NOT NULL, `FileName` CHAR(255) NOT NULL LOCALIZABLE, `FileSize` LONG NOT NULL, `Version` CHAR(72), `Language` CHAR(20), `Attributes` SHORT, `Sequence` LONG NOT NULL PRIMARY KEY `File`)");
            Sql("CREATE TABLE `Media` (`DiskId` SHORT NOT NULL, `LastSequence` LONG NOT NULL, `DiskPrompt` CHAR(64) LOCALIZABLE, `Cabinet` CHAR(255), `VolumeLabel` CHAR(32), `Source` CHAR(72) PRIMARY KEY `DiskId`)");
            Sql("INSERT INTO `Media` (`DiskId`,`LastSequence`) VALUES (1,0)");
            Sql("CREATE TABLE `Registry` (`Registry` CHAR(72) NOT NULL, `Root` SHORT NOT NULL, `Key` CHAR(255) NOT NULL LOCALIZABLE, `Name` CHAR(255) LOCALIZABLE, `Value` CHAR(0) LOCALIZABLE, `Component_` CHAR(72) NOT NULL PRIMARY KEY `Registry`)");
            Sql("CREATE TABLE `RemoveFile` (`FileKey` CHAR(72) NOT NULL, `Component_` CHAR(72) NOT NULL, `FileName` CHAR(255) LOCALIZABLE, `DirProperty` CHAR(72) NOT NULL, `InstallMode` SHORT NOT NULL PRIMARY KEY `FileKey`)");
            Sql("CREATE TABLE `InstallExecuteSequence` (`Action` CHAR(72) NOT NULL, `Condition` CHAR(255), `Sequence` SHORT PRIMARY KEY `Action`)");
            foreach(var item in new[]{("ProductCode",product),("ProductName","Burrito verified HidHide orphan cleanup"),("ProductVersion","3.4.2"),("ProductLanguage","1033"),("Manufacturer","Burrito"),("ALLUSERS","1"),("ARPSYSTEMCOMPONENT","1"),("REBOOT","ReallySuppress")})Sql($"INSERT INTO `Property` (`Property`,`Value`) VALUES ('{item.Item1}','{item.Item2}')");
            Sql("INSERT INTO `Directory` (`Directory`,`DefaultDir`) VALUES ('TARGETDIR','SourceDir')");
            Sql("INSERT INTO `Directory` (`Directory`,`Directory_Parent`,`DefaultDir`) VALUES ('System64Folder','TARGETDIR','.')");
            Sql("INSERT INTO `Directory` (`Directory`,`Directory_Parent`,`DefaultDir`) VALUES ('VerifiedDrivers','System64Folder','drivers')");
            Sql($"INSERT INTO `Component` (`Component`,`ComponentId`,`Directory_`,`Attributes`,`KeyPath`) VALUES ('Cleanup','{Guid.NewGuid():B}','VerifiedDrivers',260,'Marker')");
            Sql("INSERT INTO `Feature` (`Feature`,`Title`,`Level`,`Attributes`) VALUES ('Cleanup','Verified orphan cleanup',1,0)");
            Sql("INSERT INTO `FeatureComponents` (`Feature_`,`Component_`) VALUES ('Cleanup','Cleanup')");
            Sql($"INSERT INTO `Registry` (`Registry`,`Root`,`Key`,`Name`,`Value`,`Component_`) VALUES ('Marker',2,'Software\\Burrito\\OrphanCleanup\\{product}','State','1','Cleanup')");
            Sql($"INSERT INTO `Registry` (`Registry`,`Root`,`Key`,`Name`,`Component_`) VALUES ('RemoveMarker',2,'Software\\Burrito\\OrphanCleanup\\{product}','-','Cleanup')");
            Sql("INSERT INTO `RemoveFile` (`FileKey`,`Component_`,`FileName`,`DirProperty`,`InstallMode`) VALUES ('ExactHidHide','Cleanup','HidHide.sys','VerifiedDrivers',1)");
            foreach(var action in new[]{("ValidateProductID",700),("CostInitialize",800),("FileCost",900),("CostFinalize",1000),("InstallValidate",1400),("InstallInitialize",1500),("UnpublishFeatures",1800),("RemoveRegistryValues",2600),("RemoveFiles",3500),("WriteRegistryValues",5000),("RegisterUser",6000),("RegisterProduct",6100),("PublishFeatures",6300),("PublishProduct",6400),("InstallFinalize",6600)})Sql($"INSERT INTO `InstallExecuteSequence` (`Action`,`Sequence`) VALUES ('{action.Item1}',{action.Item2})");
            Check(MsiGetSummaryInformation(db,null,20,out uint summary));try{
                Check(MsiSummaryInfoSetProperty(summary,7,30,0,IntPtr.Zero,"x64;1033"));
                Check(MsiSummaryInfoSetProperty(summary,9,30,0,IntPtr.Zero,Guid.NewGuid().ToString("B")));
                Check(MsiSummaryInfoSetProperty(summary,14,3,200,IntPtr.Zero,null));
                Check(MsiSummaryInfoSetProperty(summary,15,3,0,IntPtr.Zero,null));
                Check(MsiSummaryInfoPersist(summary));
            }finally{MsiCloseHandle(summary);}
            Check(MsiDatabaseCommit(db));
        }finally{MsiCloseHandle(db);}
        return product;
    }
    public static async Task<int> Remove(Action<string> log)
    {
        string folder=Path.Combine(Path.GetTempPath(),"CODM-Setup-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try {
            string msi=Path.Combine(folder,"verified-orphan.msi"),product=Create(msi);
            string installer=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"msiexec.exe");
            log("已确认驱动停止；通过 Windows Installer 清理系统管理的精确 HidHide.sys，不修改权限");
            log("临时维护安装项："+product+"；文件清理后立即移除此项");
            int install=await WindowsIntegration.Run(installer,["/i",msi,"/qn","/norestart","REBOOT=ReallySuppress","/L*v",Path.Combine(folder,"cleanup.log")],log);
            int remove=await WindowsIntegration.Run(installer,["/x",product,"/qn","/norestart","REBOOT=ReallySuppress"],log);
            if(!ExitPolicy.Success(install)){
                string diagnostics=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CODMControllerProbe","maintenance");SafeFiles.NoLinks(diagnostics);Directory.CreateDirectory(diagnostics);
                File.Copy(Path.Combine(folder,"cleanup.log"),Path.Combine(diagnostics,"HidHide-orphan-cleanup.log"),true);log("Windows Installer 详细失败日志："+Path.Combine(diagnostics,"HidHide-orphan-cleanup.log"));return install;
            }
            if(!ExitPolicy.Success(remove)&&remove!=1605)return remove;
            return ExitPolicy.Reboot(install)||ExitPolicy.Reboot(remove)?3010:0;
        } finally{SafeFiles.DeleteOwnedTree(folder,folder);}
    }
    public static async Task<bool> RemoveRegistered(Action<string> log)
    {
        bool success=true;
        using var machine=Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine,Microsoft.Win32.RegistryView.Registry64);
        using var registry=machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");if(registry==null)return true;
        foreach(string name in registry.GetSubKeyNames()) {
            using var item=registry.OpenSubKey(name);
            if(item?.GetValue("DisplayName") as string!="Burrito verified HidHide orphan cleanup"||item.GetValue("Publisher") as string!="Burrito"||!Guid.TryParse(name,out _))continue;
            log("清理先前留下的精确维护项："+name);
            int code=await WindowsIntegration.Run(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"msiexec.exe"),["/x",name,"/qn","/norestart","REBOOT=ReallySuppress"],log);
            if(!ExitPolicy.Success(code)&&code!=1605)success=false;
        }
        return success;
    }
    static void Check(int code){if(code!=0)throw new IOException("Windows Installer 清理数据库准备失败："+code);}
    [DllImport("msi.dll",CharSet=CharSet.Unicode)]static extern int MsiOpenDatabase(string path,IntPtr mode,out uint database);
    [DllImport("msi.dll",CharSet=CharSet.Unicode)]static extern int MsiDatabaseOpenView(uint database,string sql,out uint view);
    [DllImport("msi.dll")]static extern int MsiViewExecute(uint view,uint record);
    [DllImport("msi.dll")]static extern int MsiDatabaseCommit(uint database);
    [DllImport("msi.dll")]static extern int MsiCloseHandle(uint handle);
    [DllImport("msi.dll",CharSet=CharSet.Unicode)]static extern int MsiGetSummaryInformation(uint database,string? path,uint updates,out uint summary);
    [DllImport("msi.dll",CharSet=CharSet.Unicode)]static extern int MsiSummaryInfoSetProperty(uint summary,uint property,uint type,int number,IntPtr time,string? text);
    [DllImport("msi.dll")]static extern int MsiSummaryInfoPersist(uint summary);
}
