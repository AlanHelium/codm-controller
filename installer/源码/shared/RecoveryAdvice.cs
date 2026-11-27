namespace Burrito.Installer;
public static class RecoveryAdvice
{
    public static string For(Outcome result)
    {
        string all=result.Summary+" "+string.Join(" ",result.Details);
        if(result.RestartRequired)return "Windows 尚未释放驱动。保存工作后手动重启，卸载会在登录后继续复查；安装或修复请重启后重新打开。";
        if(result.Success)return result.HasWarnings?result.Summary:"";
        if(all.Contains("请先")&&(all.Contains("退出")||all.Contains("运行")))return "正常关闭转换器、游戏与模拟器，再点击重试。工具不会强行结束它们。";
        if(all.Contains("1618"))return "Windows 正在执行其他安装。等待该安装结束，然后点击重试。";
        if(all.Contains("磁盘")||all.Contains("空间不足"))return "释放安装盘空间，或在安装选项中更换位置，然后重试。";
        if(all.Contains("校验")||all.Contains("缺少安装清单")||all.Contains("内嵌文件"))return "重新下载官方完整安装包再运行；请勿只复制单个主程序EXE。";
        if(all.Contains("访问")||all.Contains("占用")||all.Contains("UnauthorizedAccess")||all.Contains("permission",StringComparison.OrdinalIgnoreCase))return "关闭其他手柄配置窗口和本软件窗口后重试；若系统明确拦截文件，请核对发布来源与系统拦截记录。";
        if(all.Contains("隔离")||all.Contains("白名单"))return "接入手柄并关闭游戏，然后运行安装目录的“修复工具.exe”。修复完成后重新打开游戏或测试页面。";
        if(all.Contains("驱动")||all.Contains("组件")||all.Contains("ViGEm")||all.Contains("HidHide"))return "运行安装目录的“修复工具.exe”自动检查；若目录文件也有损坏，重新运行新版完整安装包即可。";
        return "先正常关闭软件后点击重试。仍未完成时，展开详细记录并保存记录给作者，勿反复卸载驱动或清空设置。";
    }
}
