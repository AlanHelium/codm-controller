using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace Burrito.Installer;

// Use the wide-character Shell Link interface throughout, including IPersistFile.Save.
// WScript.Shell can round-trip names through the system ANSI code page on some systems.
public static class UnicodeShortcut
{
    private static readonly Guid ShellLinkClass=new("00021401-0000-0000-C000-000000000046");

    public static void Create(string root,string exe,string link)
    {
        string path=Path.GetFullPath(link),directory=Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        string temporary=Path.Combine(directory,".codm-link-"+Guid.NewGuid().ToString("N")+".lnk");
        object? instance=null;
        try
        {
            instance=Activator.CreateInstance(Type.GetTypeFromCLSID(ShellLinkClass,true)!)!;
            var shortcut=(IShellLinkW)instance;
            shortcut.SetPath(Path.GetFullPath(Path.Combine(root,exe)));
            shortcut.SetWorkingDirectory(Path.GetFullPath(root));
            shortcut.SetDescription(Product.Name);
            shortcut.SetShowCmd(1);
            ((IPersistFile)instance).Save(temporary,true);
            if(!File.Exists(temporary)||new FileInfo(temporary).Length==0)throw new IOException("快捷方式未保存");
            // Preserve an existing valid shortcut if saving fails; replace only after successful save.
            File.Move(temporary,path,true);
        }
        finally
        {
            if(instance!=null&&Marshal.IsComObject(instance))Marshal.FinalReleaseComObject(instance);
            if(File.Exists(temporary))File.Delete(temporary);
        }
    }

    public static string? TryCreate(string root,string exe,string link,Action<string,string,string>? create=null)
    {
        try{(create??Create)(root,exe,link);return null;}
        catch(Exception ex){return "开始菜单快捷方式未创建："+ex.Message+"；可从安装目录运行 "+exe+"。";}
    }

    // Read without Resolve: never launch the target or try to find a moved executable.
    public static (string Target,string WorkingDirectory,string Description) Read(string path)
    {
        object instance=Activator.CreateInstance(Type.GetTypeFromCLSID(ShellLinkClass,true)!)!;
        try
        {
            ((IPersistFile)instance).Load(Path.GetFullPath(path),0);
            var shortcut=(IShellLinkW)instance;
            var target=new StringBuilder(32768);var directory=new StringBuilder(32768);var description=new StringBuilder(1024);
            shortcut.GetPath(target,target.Capacity,IntPtr.Zero,4);
            shortcut.GetWorkingDirectory(directory,directory.Capacity);
            shortcut.GetDescription(description,description.Capacity);
            return(target.ToString(),directory.ToString(),description.ToString());
        }
        finally{Marshal.FinalReleaseComObject(instance);}
    }

    // Method order follows IShellLinkW's COM vtable, not the alphabetic documentation index.
    [ComImport,Guid("000214F9-0000-0000-C000-000000000046"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder path,int capacity,IntPtr findData,uint flags);
        void GetIDList(out IntPtr list);
        void SetIDList(IntPtr list);
        void GetDescription([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder text,int capacity);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)]string text);
        void GetWorkingDirectory([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder path,int capacity);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)]string path);
        void GetArguments([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder text,int capacity);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)]string text);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int command);
        void SetShowCmd(int command);
        void GetIconLocation([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder path,int capacity,out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)]string path,int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)]string path,uint reserved);
        void Resolve(IntPtr window,uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)]string path);
    }
}
