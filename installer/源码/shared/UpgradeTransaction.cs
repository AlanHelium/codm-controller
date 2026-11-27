namespace Burrito.Installer;

// Back up only exact files written by this update; never delete user directory trees.
public sealed class UpgradeTransaction:IDisposable
{
    readonly string root,backup;
    readonly Action<string> log;
    readonly Dictionary<string,string?> saved=new(StringComparer.OrdinalIgnoreCase);
    bool committed;
    public UpgradeTransaction(string target,Action<string> logger)
    {
        root=SafeFiles.ValidateRoot(target);log=logger;
        backup=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CODMControllerProbe","maintenance","upgrade-"+Guid.NewGuid().ToString("N"));
    }
    public void Capture(string relative)
    {
        string target=SafeFiles.Child(root,relative);if(saved.ContainsKey(target))return;
        if(File.Exists(target)){string prior=SafeFiles.Child(backup,relative);Directory.CreateDirectory(Path.GetDirectoryName(prior)!);File.Copy(target,prior,false);saved[target]=prior;}else saved[target]=null;
    }
    public void Replace(string source,string relative)
    {
        Capture(relative);string target=SafeFiles.Child(root,relative),temp=target+".update-"+Guid.NewGuid().ToString("N");Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        try
        {
            File.Copy(source,temp,false);
            for(int attempt=0;;attempt++)try{File.Move(temp,target,true);break;}catch(IOException)when(attempt<3){Thread.Sleep(350);}
        }
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
    public void Commit(){committed=true;if(Directory.Exists(backup))try{SafeFiles.DeleteOwnedTree(backup,backup);}catch(Exception ex){log("升级已完成，旧文件备份暂时保留："+ex.Message);}}
    public void Dispose()
    {
        if(committed)return;
        bool okay=true;
        foreach(var entry in saved.Reverse())try
        {
            SafeFiles.NoLinks(entry.Key);
            if(entry.Value==null){if(File.Exists(entry.Key))File.Delete(entry.Key);}
            else{string temp=entry.Key+".rollback-"+Guid.NewGuid().ToString("N");try{File.Copy(entry.Value,temp,false);File.Move(temp,entry.Key,true);}finally{if(File.Exists(temp))File.Delete(temp);}}
        }
        catch(Exception ex){okay=false;log("旧文件恢复未完成："+Path.GetFileName(entry.Key)+"；"+ex.Message);}
        log(okay?"本次文件更新已回退，原有配置未改动":"部分文件需继续恢复；备份保留在："+backup);
        if(Directory.Exists(backup))log("更新前文件备份："+backup);
    }
}
