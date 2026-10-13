using Burrito.Installer;
using System.Text;
using System.Text.Json;

static class ShortcutChecks
{
    static int count;
    static void Check(bool pass,string description){if(!pass)throw new Exception(description);count++;}
    [STAThread]static void Main(string[] args)
    {
        string report=Path.GetFullPath(args[0]);
        string home=Path.Combine(Path.GetTempPath(),"CODM-ShortcutChecks-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(home);
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);var traditional=Encoding.GetEncoding(950);
            Check(traditional.GetString(traditional.GetBytes("CODM 手柄转换器.lnk"))=="CODM 手柄??器.lnk","CP950 did not reproduce the reported filename");
            foreach(var apartment in new[]{ApartmentState.STA,ApartmentState.MTA})
            {
                Exception? failure=null;
                var worker=new Thread(()=>
                {
                    try
                    {
                        foreach(string name in new[]{"English","简体中文 转换器","繁體中文 轉換器","日本語 ユーザー 🎮"})
                        {
                            string root=Path.Combine(home,apartment.ToString(),name,"应用 文件");Directory.CreateDirectory(root);
                            File.WriteAllText(Path.Combine(root,Product.MainName),"test only");File.WriteAllText(Path.Combine(root,Product.UninstallName),"test only");
                            string link=Path.Combine(home,apartment.ToString(),name,"開始功能表","CODM 手柄转换器.lnk");
                            Check(UnicodeShortcut.TryCreate(root,Product.MainName,link)==null,"Unicode shortcut creation failed");
                            var first=UnicodeShortcut.Read(link);
                            Check(first.Target.Equals(Path.Combine(root,Product.MainName),StringComparison.OrdinalIgnoreCase),"Shortcut target was corrupted");
                            Check(first.WorkingDirectory.Equals(root,StringComparison.OrdinalIgnoreCase)&&first.Description==Product.Name,"Working directory or description was corrupted");
                            Check(!Directory.GetFiles(Path.GetDirectoryName(link)!,".codm-link-*").Any(),"Temporary shortcut remains after success");
                            Check(UnicodeShortcut.TryCreate(root,Product.UninstallName,link)==null,"Repeated registration failed");
                            Check(UnicodeShortcut.Read(link).Target.Equals(Path.Combine(root,Product.UninstallName),StringComparison.OrdinalIgnoreCase),"Recovery shortcut did not replace main target");
                            string? warning=UnicodeShortcut.TryCreate(root,Product.MainName,link,(_,_,_)=>throw new UnauthorizedAccessException("Permission denied (test)"));
                            Check(warning!=null&&warning.Contains("未创建")&&warning.Contains(Product.MainName),"Optional failure was not clearly reported");
                            Check(UnicodeShortcut.Read(link).Target==Path.Combine(root,Product.UninstallName),"Failed update changed existing valid shortcut");
                            // An invalid destination exercises actual Save/Move cleanup without changing system permissions.
                            string invalid=Path.Combine(Path.GetDirectoryName(link)!,"directory.lnk");Directory.CreateDirectory(invalid);
                            Check(UnicodeShortcut.TryCreate(root,Product.MainName,invalid)!=null,"Invalid shortcut destination was incorrectly successful");
                            Check(!Directory.GetFiles(Path.GetDirectoryName(link)!,".codm-link-*").Any(),"Temporary shortcut remains after failure");
                        }
                    }
                    catch(Exception ex){failure=ex;}
                });worker.SetApartmentState(apartment);worker.Start();worker.Join();if(failure!=null)throw failure;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(report)!);
            File.WriteAllText(report,JsonSerializer.Serialize(new{Passed=true,Assertions=count,ReportedCP950NameReproduced=true,UnicodePaths=true,STAAndMTA=true,RepeatedRegistration=true,FailurePreservesExistingLink=true,SystemShortcutChanges=false,DriverMutations=false},Product.Json));
            Console.WriteLine($"Shortcut checks passed: {count} assertions; system shortcuts and drivers unchanged");
        }
        finally{SafeFiles.DeleteOwnedTree(home,home);}
    }
}
