using System.Security.Cryptography;
namespace Burrito.Installer;

public static class LocalRecovery
{
    public const string RepairExe="修复工具.exe",UninstallExe="卸载程序.exe";
    public static void Install(string source,string root,IEnumerable<PortableFile> files,UpgradeTransaction? transaction=null)
    {
        foreach(var f in files.Where(f=>!f.Name.StartsWith("components/")))
        {
            string input=SafeFiles.Child(source,f.Name),output=SafeFiles.Child(root,"recovery/"+f.Name);
            using(var read=File.OpenRead(input))if(!Convert.ToHexString(SHA256.HashData(read)).Equals(f.Hash,StringComparison.OrdinalIgnoreCase))throw new IOException("本地恢复文件校验失败："+f.Name);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);if(transaction!=null)transaction.Replace(input,"recovery/"+f.Name);else File.Copy(input,output,true);
        }
        foreach(var name in new[]{RepairExe,UninstallExe}){if(transaction!=null)transaction.Replace(SafeFiles.Child(source,Product.UninstallName),name);else File.Copy(SafeFiles.Child(source,Product.UninstallName),SafeFiles.Child(root,name),true);}
    }
    public static List<string> Remove(string root)
    {
        var retained=new List<string>();
        foreach(var name in new[]{RepairExe,UninstallExe}.Concat(Product.PayloadNames.Where(n=>!n.StartsWith("components/")).Select(n=>"recovery/"+n)))
        {string file=SafeFiles.Child(root,name);try{if(File.Exists(file))File.Delete(file);}catch(Exception ex){retained.Add(name+"："+ex.Message);}}
        string folder=SafeFiles.Child(root,"recovery");
        if(Directory.Exists(folder))
        {
            if(Directory.EnumerateFileSystemEntries(folder).Any())retained.Add("恢复目录仍有未移除项目，已保留，详见："+folder);
            else Directory.Delete(folder,false);
        }
        return retained;
    }
}
