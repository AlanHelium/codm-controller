using System.Runtime.InteropServices;
using System.Threading;

namespace CODMControllerProbe;

static class Program
{
    private static Mutex? _mutex;

    // 输出循环用 Thread.Sleep 按 1ms 节拍（默认 1000Hz）。
    // Windows 默认计时器粒度 ~15.6ms，Sleep(1) 实际睡 ~15.6ms → 频率虚标、帧抖动大；
    // timeBeginPeriod(1) 把全局计时精度提到 1ms，Sleep(1) 才真正 ~1ms（进程退出由系统自动恢复）。
    [DllImport("winmm.dll", SetLastError = true)]
    private static extern uint timeBeginPeriod(uint uMilliseconds);

    [STAThread]
    static void Main(string[] args)
    {
        UiPreview.Enabled = args.Contains("--ui-preview");
        if (IsolationProbe.HandleArguments(args)) return;
        // 启动轨迹：单文件版真机崩溃定位（写公共目录，管理员 TEMP 不混淆）
        try { File.Delete(@"C:\Users\Public\codm_boot.txt"); } catch { }
        void Trace(string s)
        {
            try { File.AppendAllText(@"C:\Users\Public\codm_boot.txt", DateTime.Now.ToString("HH:mm:ss.fff") + " " + s + "\n"); } catch { }
        }
        // 捕获所有类型的未处理异常（含 UI 线程 / 非 UI 线程 / 早期静态构造）
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Trace("AppDomain.UnhandledException: " + e.ExceptionObject);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
            Trace("UnobservedTask: " + e.Exception);

        Trace("Main 进入 args=" + (args.Length > 0 ? args[0] : "无") + " PID=" + Environment.ProcessId + " 版本=v3.4.3");
        // 1ms 计时精度：让输出循环的 1000Hz 节拍真实生效（否则 Sleep(1) 实际 15.6ms）
        try { timeBeginPeriod(1); } catch { }

        // 诊断模式：dotnet run -- --diag（不启动 WinForms，打印真实环境状态）
        if (args.Length > 0 && args[0] == "--diag")
        {
            Trace("进入 --diag");
            RunDiag();
            Trace("--diag 完成，正常退出");
            return;
        }

        // 单实例互斥：多个工具实例会互相清/写 HidHide 黑名单，
        // 导致实体手柄时隐时现、检测不到（已多次踩坑）。第二个实例直接提示退出。
        Trace("创建单实例 Mutex...");
        _mutex = new Mutex(true, @"Global\CODMControllerProbe_SingleInstance", out bool createdNew);
        Trace("Mutex createdNew=" + createdNew);
        if (!createdNew)
        {
            Trace("被单实例拦截（已有实例运行）→ 弹提示后退出");
            MessageBox.Show(
                "工具已在运行中。\n\n" +
                "请关闭已打开的 CODM 手柄转换窗口后再启动（多开会导致实体手柄隐藏互相干扰）。",
                "CODM 手柄转换", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            if (args.Length == 2 && args[0] == "--hardware-check")
            { RepairHardwareCheck.Run(args[1]); return; }
            if (args.Length == 2 && args[0] == "--hold-isolation-test")
            { RepairHardwareCheck.HoldForRecoveryTest(args[1]); return; }
            Trace("配置 WinForms（DPI/样式）...");
            // PerMonitorV2：字体按窗口所在显示器实际 DPI 渲染；布局由 Dpi.Px() 按同一因子等比缩放
            // （多显示器/虚拟屏环境：窗口在哪块屏，就按那块屏的比例放大，永不错位）。
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (_, e) => Trace("Application.ThreadException: " + e.Exception);
            Trace("启动 MainForm（Application.Run）...");
            Application.Run(new MainForm());
            Trace("Application.Run 正常返回（用户关窗）");
        }
        catch (Exception ex)
        {
            Trace("全局 catch: " + ex);
            // 全局兜底：任何未处理异常都写 crash.log + 弹窗，绝不再"双击无反应"
            CrashLog.Write("未处理异常（全局兜底）: " + ex);
            try
            {
                MessageBox.Show(
                    "程序遇到意外错误，已记录到：\n" +
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                 "CODMControllerProbe", "crash.log") +
                    "\n\n错误信息：\n" + ex.Message,
                    "CODM 手柄转换", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { /* 弹窗失败不再处理 */ }
        }
        finally
        {
            Trace("进入 finally，释放 Mutex");
            _mutex.ReleaseMutex();
        }
    }

    /// <summary>诊断：打印真实环境的语言配置 / HidHide 状态 / 设备枚举，写入 %TEMP%\codm_diag.txt。</summary>
    private static void RunDiag()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("配置路径: " + SettingsStore.Path_);
        var s = SettingsStore.Load();
        sb.AppendLine("Language=" + s.Language + " (0=中文,1=英文)");
        sb.AppendLine("VersionLevel=" + s.VersionLevel + " FirstRun=" + s.FirstRun + " ProUnlocked=" + s.ProUnlocked);
        sb.AppendLine("NudgeCursor=" + s.NudgeCursor + " RumbleFocusOnly=" + s.RumbleFocusOnly + " CleanDriversOnExit=" + s.CleanDriversOnExit);
        sb.AppendLine("EnableFastProne=" + s.EnableFastProne + " HoldProneDelayMs=" + s.HoldProneDelayMs);
        sb.AppendLine("--- HidHide ---");
        sb.AppendLine("Installed=" + HidHideManager.IsInstalled());
        sb.AppendLine("Active=" + HidHideManager.GetActive());
        var bl = HidHideManager.GetBlacklist();
        sb.AppendLine("BlacklistCount=" + bl.Count);
        foreach (var b in bl) sb.AppendLine("  BL=" + b);
        sb.AppendLine("--- 设备枚举（待隐藏 + 全部）---");
        var ids = HidEnum.GetGamepadInstanceIds();
        sb.AppendLine("待隐藏设备数=" + ids.Count);
        foreach (var i in ids) sb.AppendLine("  HIDE=" + i);
        foreach (var d in HidEnum.Enumerate())
            sb.AppendLine($"  DEV name='{d.Name}' class={d.DevClass} VID={d.Vid} PID={d.Pid} virtual={d.IsVirtualGamepad} inst='{d.InstanceId}'");
        string outPath = @"C:\Users\Public\codm_diag.txt";
        System.IO.File.WriteAllText(outPath, sb.ToString());
        Console.WriteLine("DIAG_WRITTEN " + outPath);
    }
}
