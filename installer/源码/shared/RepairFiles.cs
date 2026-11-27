using System.Security.Cryptography;
using System.Text.Json;
namespace Burrito.Installer;

// Only restore the exact product payload. Personal configuration is never reset.
public static class RepairFiles
{
    public const string Manifest="repair-files.json";
    public static List<PortableFile> Read(string root)
    {
        string file=SafeFiles.Child(root,Manifest);
        if(!File.Exists(file))return [];
        var files=JsonSerializer.Deserialize<List<PortableFile>>(File.ReadAllText(file))??throw new IOException("程序修复清单无效");
        if(files.Count!=Product.PayloadNames.Length||!files.Select(f=>f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(Product.PayloadNames))throw new IOException("程序修复清单不匹配，请重新解压完整修复包");
        return files;
    }
    static bool Matches(string path,string hash)
    {
        if(!File.Exists(path))return false;
        using var read=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(read)).Equals(hash,StringComparison.OrdinalIgnoreCase);
    }
    public static List<PortableFile> Inspect(string source,InstallRecord? record,Action<string> log)
    {
        if(record==null){log("未找到已登记的程序目录，仅检查共享组件；不会猜测或覆盖其他文件夹");return [];}
        // An older kit must not overwrite a newer app. Its driver repair remains usable.
        if(Version.TryParse(record.Version,out var installed)&&installed>Version.Parse(Product.Version))
        {log("已安装程序版本较新，本工具不覆盖程序文件");return [];}
        var files=Read(source);
        if(files.Count==0)files=Read(record.Root);
        if(files.Count==0){log("旧版没有程序文件清单；可修复组件，程序损坏请使用最新版完整修复包");return [];}
        var damaged=files.Where(f=>!Matches(SafeFiles.Child(record.Root,f.Name),f.Hash)).ToList();
        foreach(var f in damaged)log("程序文件缺失或不匹配："+f.Name);
        return damaged;
    }
    public static bool Restore(string source,InstallRecord record,List<PortableFile> damaged,Action<string> log)
    {
        if(damaged.Count==0)return true;
        var candidates=damaged.Select(f=>(File:f,Source:SafeFiles.Child(source,f.Name.StartsWith("components/")?f.Name:"recovery/"+f.Name))).ToArray();
        foreach(var item in candidates)if(!Matches(item.Source,item.File.Hash))
        {log("当前目录不含完整程序恢复文件，请完整解压新版独立修复包后运行；未修改程序文件");return false;}
        string backup=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CODMControllerProbe","maintenance","file-backup-"+Guid.NewGuid().ToString("N"));
        foreach(var item in candidates)
        {
            string target=SafeFiles.Child(record.Root,item.File.Name),temp=target+".repair-"+Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            try
            {
                File.Copy(item.Source,temp,false);
                if(!Matches(temp,item.File.Hash))throw new IOException("恢复文件校验失败："+item.File.Name);
                if(File.Exists(target)){string prior=SafeFiles.Child(backup,item.File.Name);Directory.CreateDirectory(Path.GetDirectoryName(prior)!);File.Copy(target,prior,false);}
                File.Move(temp,target,true);log("已恢复程序文件："+item.File.Name);
            }
            finally{if(File.Exists(temp))File.Delete(temp);}
        }
        var manifest=Read(source);if(manifest.Count>0)SafeFiles.WriteAtomic(SafeFiles.Child(record.Root,Manifest),JsonSerializer.Serialize(manifest,Product.Json));
        record.Version=Product.Version;record.Save();
        if(!record.Portable)WindowsIntegration.Register(record);
        log("原文件备份位置："+backup);return true;
    }
}
