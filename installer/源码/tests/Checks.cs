using Burrito.Installer;
using System.Text.Json;

static class Checks
{
    static int count;
    static void Check(bool condition,string label){count++;if(!condition)throw new Exception(label);}
    static void Reject(Action action,string label){bool threw=false;try{action();}catch(IOException){threw=true;}Check(threw,label);}
    static void Files(string root){new InstallRecord{Root=root}.Save();foreach(string name in Product.PayloadNames){string path=SafeFiles.Child(root,name);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,"test payload");}}
    static void Main()
    {
        string home=Path.Combine(Path.GetTempPath(),"CODM-InstallerChecks-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(home);
        try
        {
            foreach(string path in new[]{@"C:\","relative",Environment.GetFolderPath(Environment.SpecialFolder.Windows),Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)})Reject(()=>SafeFiles.ValidateRoot(path),"Protected root refused: "+path);
            string target=Path.Combine(home,"安装目录 with spaces");Directory.CreateDirectory(target);Check(SafeFiles.ValidateRoot(target)==target,"Chinese/spaced custom location accepted");
            File.WriteAllText(Path.Combine(target,"personal.txt"),"keep");Reject(()=>SafeFiles.CheckInstallTarget(target),"Unmarked nonempty directory refused");Check(File.Exists(Path.Combine(target,"personal.txt")),"Foreign file unchanged on refusal");File.Delete(Path.Combine(target,"personal.txt"));
            Reject(()=>SafeFiles.Child(target,"../outside.txt"),"Traversal refused");Reject(()=>SafeFiles.Child(target,@"C:\Windows\system.ini"),"Absolute child refused");
            SafeFiles.CheckInstallTarget(target);Files(target);Check(InstallRecord.Load(Path.Combine(target,Product.RecordName)).Root==target,"Record tracks exact custom root");
            string wrong=Path.Combine(home,"wrong");Directory.CreateDirectory(wrong);File.WriteAllText(Path.Combine(wrong,"keep.txt"),"keep");Reject(()=>SafeFiles.DeleteOwnedTree(wrong,target),"Mismatched deletion scope refused");Check(File.Exists(Path.Combine(wrong,"keep.txt")),"Outside target preserved");
            File.WriteAllText(Path.Combine(target,"user-added.txt"),"keep");var retained=SafeFiles.RemoveInstalledFiles(target,false);Check(retained.Any(),"Unknown file explicitly reported");Check(File.Exists(Path.Combine(target,"user-added.txt")),"Unknown install-folder file preserved");Check(File.Exists(Path.Combine(target,Product.UninstallName))&&File.Exists(Path.Combine(target,Product.RecordName)),"Retry entry retained when cleanup incomplete");
            File.Delete(Path.Combine(target,"user-added.txt"));retained=SafeFiles.RemoveInstalledFiles(target,false);Check(retained.Count==0&&!Directory.Exists(target),"Owned files and empty install directory fully removed");
            Directory.CreateDirectory(target);Files(target);retained=SafeFiles.RemoveInstalledFiles(target,true);Check(!File.Exists(Path.Combine(target,Product.MainName))&&File.Exists(Path.Combine(target,Product.UninstallName)),"Pending reboot removes main but preserves recovery");Check(File.Exists(SafeFiles.Child(target,"components/HidHide_1.5.230_x64.exe")),"Official recovery package retained while pending");
            var record=InstallRecord.Load(Path.Combine(target,Product.RecordName));record.UninstallPending=true;record.PendingBoot=ExitPolicy.Boot;record.RemoveDrivers=false;record.DeleteData=false;record.Save();var loaded=InstallRecord.Load(Path.Combine(target,Product.RecordName));Check(loaded.UninstallPending&&!loaded.RemoveDrivers&&!loaded.DeleteData,"Deferred cleanup preserves user's choices");
            Reject(()=>SafeFiles.Child(target,"components/../../outside"),"Nested traversal refused");
            Check(ExitPolicy.Success(0)&&ExitPolicy.Success(3010)&&!ExitPolicy.Success(1603),"Official result classification");Check(ExitPolicy.Reboot(3010)&&ExitPolicy.Reboot(1641)&&!ExitPolicy.Reboot(0),"Restart requested only by actual codes");
            Check(Drivers.IsTargetInf("; Nefarius\nAddService = HidHide, 0, Service","HidHide"),"Exact vendor/service INF identified");Check(!Drivers.IsTargetInf("; Another vendor\nAddService = HidHide,0,Service","HidHide"),"Same service with another provider rejected");Check(!Drivers.IsTargetInf("; Nefarius\nAddService=NotHidHide,0,Service","HidHide"),"Similar service name rejected");
            Check(Drivers.MatchesProduct("HidHide","Nefarius Software Solutions e.U.","HidHide"),"Exact official product identified");Check(!Drivers.MatchesProduct("HidHide Helper","Nefarius","HidHide"),"Helper apps not targeted");Check(!Drivers.MatchesProduct("HidHide","Unknown publisher","HidHide"),"Foreign publisher not targeted");
            var unloaded=new DriverState("HidHide",true,true,true,false,[],[]);
            Check(Drivers.IsOrphanCandidate(unloaded),"Unregistered unreferenced driver qualifies for additional checks");
            Check(!Drivers.IsOrphanCandidate(unloaded with{Filter=true}),"Active filter excludes orphan cleanup");
            Check(!Drivers.IsOrphanCandidate(unloaded with{InfPackages=["oem1.inf"]}),"Registered driver package excludes orphan cleanup");
            Check(!Drivers.IsOrphanCandidate(unloaded with{Products=["registered"]}),"MSI registration excludes orphan cleanup");
            Check(Drivers.NeedsRebootAfterRemoval(unloaded,true),"Loaded driver without package registration requires reboot");
            Check(!Drivers.NeedsRebootAfterRemoval(unloaded,false),"Stopped residual service is not classified as complete or reboot");
            Check(!Drivers.NeedsRebootAfterRemoval(unloaded with{Filter=true},true),"Active filter does not count as completed official removal");
            Check(!Drivers.NeedsRebootAfterRemoval(unloaded with{Products=["registered"]},true),"Registered driver is not treated as pending removal");
            var live=Drivers.Packages.Select(p=>Drivers.Inspect(p.Name)).ToArray();
            string cleanupMsi=Path.Combine(home,"scope-check.msi");string cleanupProduct=OrphanFileCleanup.Create(cleanupMsi);Check(Guid.TryParse(cleanupProduct,out _)&&new FileInfo(cleanupMsi).Length>0,"Fixed-file Windows Installer cleanup database builds without installation");
            string portable=Path.Combine(home,"portable package");Directory.CreateDirectory(portable);
            var portableFiles=new List<PortableFile>();foreach(string name in Product.PayloadNames){string path=SafeFiles.Child(portable,name);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,"portable payload");portableFiles.Add(new(name,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)))));}
            File.WriteAllText(Path.Combine(portable,PortablePackage.Manifest),JsonSerializer.Serialize(portableFiles));
            File.AppendAllText(Path.Combine(portable,Product.MainName),"tampered");Reject(()=>PortablePackage.Initialize(portable),"Tampered portable package refuses initialization");Check(!File.Exists(Path.Combine(portable,Product.RecordName)),"No deletion authority recorded for tampered package");
            File.WriteAllText(Path.Combine(portable,Product.MainName),"portable payload");var portableRecord=PortablePackage.Initialize(portable);Check(portableRecord.Portable&&portableRecord.Root==portable,"Verified portable package records its actual extraction location");
            File.WriteAllText(Path.Combine(portable,"准备组件.cmd"),"test command");File.WriteAllText(Path.Combine(portable,"清理卸载.cmd"),"test command");
            var portableLeft=SafeFiles.RemoveInstalledFiles(portable,false);Check(portableLeft.Count==0&&!Directory.Exists(portable),"Portable manifest and entry commands are removed with owned files");
            string json=JsonSerializer.Serialize(new{Passed=true,Assertions=count,ActualDriverMutations=false,UserDataMutations=false,ReadonlyDriverInventory=live},Product.Json);File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"checks.json"),json);Console.WriteLine(json);
        }
        finally{SafeFiles.DeleteOwnedTree(home,home);}
        string temp=Path.Combine(Path.GetTempPath(),"CODM-Uninstall-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);File.WriteAllText(Path.Combine(temp,"owned.txt"),"test cleanup");File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"exit-cleanup-target.txt"),temp);ExitCleanup.Start(temp,false);
    }
}
