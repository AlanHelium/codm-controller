using System.Diagnostics;

namespace CODMControllerProbe;

public partial class MainForm
{
    private void OpenConnectionRepair()
    {
        string helper=Path.Combine(AppContext.BaseDirectory,"CODMControllerProbeUninstaller.exe");
        if(!File.Exists(helper))
        {MessageBox.Show(this,Lang.T("未找到配套修复工具，请使用完整安装包或独立连接修复包。","The maintenance helper is missing. Use the complete installer or connection repair kit."),Lang.T("连接修复","Connection repair"));return;}
        try
        {
            if(!Version.TryParse(FileVersionInfo.GetVersionInfo(helper).FileVersion,out var version)||version<new Version(3,5,15,0))
            {MessageBox.Show(this,Lang.T("配套修复工具版本过旧，请使用完整新版安装包或独立连接修复包。","The maintenance helper is outdated. Use the complete new package or standalone repair kit."),Lang.T("连接修复","Connection repair"));return;}
            var start=new ProcessStartInfo(helper){UseShellExecute=true};
            start.ArgumentList.Add("--repair");
            start.ArgumentList.Add("--wait-pid");start.ArgumentList.Add(Environment.ProcessId.ToString());
            start.ArgumentList.Add("--wait-start");start.ArgumentList.Add(Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks.ToString());
            // Normal FormClosing saves preferences and restores the input
            // session. The helper waits for exit before maintenance begins.
            using var launched=Process.Start(start)??throw new IOException("未能打开修复工具");
            Close();
        }
        catch(Exception ex){MessageBox.Show(this,Lang.T("修复工具尚未启动：","The repair tool did not start: ")+ex.Message);}
    }
}
