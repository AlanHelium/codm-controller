namespace Burrito.Installer;

public static class SafeFiles
{
    public static bool Within(string path,string root)=>Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    public static string ValidateRoot(string input)
    {
        if(string.IsNullOrWhiteSpace(input)||!Path.IsPathFullyQualified(input)||input.StartsWith("\\\\")||input.StartsWith("\\?"))throw new IOException("请选择本机磁盘上的完整安装路径");
        string full=Path.TrimEndingDirectorySeparator(Path.GetFullPath(input));
        if(full.Length<4||full.Equals(Path.TrimEndingDirectorySeparator(Path.GetPathRoot(full)!),StringComparison.OrdinalIgnoreCase))throw new IOException("不能直接安装到磁盘根目录");
        foreach(string special in new[]{Environment.GetFolderPath(Environment.SpecialFolder.Windows),Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)})
        {
            if(full.Equals(Path.TrimEndingDirectorySeparator(special),StringComparison.OrdinalIgnoreCase))throw new IOException("请为软件选择独立子文件夹，不能使用系统或个人目录本身");
        }
        if(Within(full,Environment.GetFolderPath(Environment.SpecialFolder.Windows)))throw new IOException("不能安装到 Windows 系统目录");
        NoLinks(full);return full;
    }
    public static void NoLinks(string path)
    {
        string? at=Path.GetFullPath(path);
        while(!string.IsNullOrEmpty(at))
        {if((Directory.Exists(at)||File.Exists(at))&&(File.GetAttributes(at)&FileAttributes.ReparsePoint)!=0)throw new IOException("路径包含链接或重解析点，已停止："+at);at=Path.GetDirectoryName(at);}
    }
    public static string Child(string root,string relative)
    {
        if(Path.IsPathRooted(relative)||relative.Split('/','\\').Any(p=>p is ".." or "."))throw new IOException("文件清单路径越界");
        string full=Path.GetFullPath(Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar)));
        if(!Within(full,root))throw new IOException("文件清单路径越界");NoLinks(full);return full;
    }
    public static void CheckInstallTarget(string root)
    {
        ValidateRoot(root);
        if(Directory.Exists(root)&&Directory.EnumerateFileSystemEntries(root).Any())
        {if(!File.Exists(Path.Combine(root,Product.RecordName)))throw new IOException("这个目录已有其他文件，请选择空的独立子文件夹");_ = InstallRecord.Load(Path.Combine(root,Product.RecordName));}
    }
    public static void WriteAtomic(string path,string text)
    {
        NoLinks(path);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temp,text,new System.Text.UTF8Encoding(true));File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
    public static void DeleteOwnedTree(string target,string expected)
    {
        string full=Path.GetFullPath(target);
        if(!full.Equals(Path.GetFullPath(expected),StringComparison.OrdinalIgnoreCase))throw new IOException("清理目标与允许目录不一致");
        NoLinks(full);if(!Directory.Exists(full))return;
        var directories=new List<string>();var files=new List<string>();var queue=new Queue<string>();queue.Enqueue(full);
        while(queue.Count>0)
        {
            string dir=queue.Dequeue();NoLinks(dir);if(dir!=full&&!Within(dir,full))throw new IOException("清理目录越界");directories.Add(dir);
            foreach(string f in Directory.EnumerateFiles(dir)){NoLinks(f);if(!Within(f,full))throw new IOException("清理文件越界");files.Add(f);}
            foreach(string d in Directory.EnumerateDirectories(dir))queue.Enqueue(d);
        }
        foreach(string f in files)File.Delete(f);
        foreach(string d in directories.AsEnumerable().Reverse())Directory.Delete(d,false);
        if(Directory.Exists(full))throw new IOException("目录仍存在："+full);
    }
    public static List<string> RemoveInstalledFiles(string root,bool keepRecovery)
    {
        var record=InstallRecord.Load(Path.Combine(root,Product.RecordName));var retained=new List<string>();
        var extras=record.Portable?new[]{PortablePackage.Manifest,"准备组件.cmd","清理卸载.cmd"}:Array.Empty<string>();
        foreach(string name in Product.PayloadNames.Where(n=>n!=Product.UninstallName&&!n.StartsWith("components/")).Concat(new[]{"install.log","ViGEmBus-install.log","HidHide-install.log"}).Concat(extras))
        {
            string path=Child(root,name);try{if(File.Exists(path))File.Delete(path);}catch(Exception ex){retained.Add(name+"："+ex.Message);}
        }
        if(!keepRecovery&&retained.Count==0)
        {
            string components=Child(root,"components");
            var expected=new HashSet<string>(new[]{Product.RecordName,Product.UninstallName,"components"}.Select(n=>Child(root,n)),StringComparer.OrdinalIgnoreCase);
            var unknown=Directory.EnumerateFileSystemEntries(root).Where(p=>!expected.Contains(p)).ToList();
            if(Directory.Exists(components))unknown.AddRange(Directory.EnumerateFileSystemEntries(components).Where(p=>!Product.PayloadNames.Where(n=>n.StartsWith("components/")).Select(n=>Child(root,n)).Contains(p,StringComparer.OrdinalIgnoreCase)));
            if(unknown.Count>0)retained.AddRange(unknown.Select(p=>"保留非清单文件："+Path.GetFileName(p)));
            else
            {
                foreach(string name in Product.PayloadNames.Where(n=>n.StartsWith("components/")).Append(Product.UninstallName)){string f=Child(root,name);if(File.Exists(f))File.Delete(f);}
                if(Directory.Exists(components))Directory.Delete(components,false);
                File.Delete(Path.Combine(root,Product.RecordName));Directory.Delete(root,false);
            }
        }
        return retained;
    }
}
