using System;
using System.IO;

namespace CODMControllerProbe;

/// <summary>
/// 崩溃 / 启动日志：写 %LOCALAPPDATA%\CODMControllerProbe\crash.log。
/// 线上用户遇到"双击没反应"时，把该文件发回即可定位，不用再靠猜。
/// </summary>
public static class CrashLog
{
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CODMControllerProbe");
    private static readonly string LogFile = Path.Combine(LogDir, "crash.log");

    /// <summary>崩溃日志文件路径（诊断报告读取用）。</summary>
    public static string FilePath => LogFile;

    public static void Write(string what)
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            File.AppendAllText(LogFile,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {what}{Environment.NewLine}");
        }
        catch
        {
            // 日志写入失败不影响主流程
        }
    }
}
