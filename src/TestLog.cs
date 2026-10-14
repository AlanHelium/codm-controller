using System;
using System.Collections.Concurrent;
using System.Text;

namespace CODMControllerProbe;

/// <summary>
/// 测试记录日志：供「测试记录」窗口实时展示与一键复制，同时写文件供诊断读取。
/// 记录点：输入源切换、WASD 方向/角度/幅度、Shift 冲刺状态、触摸板按下/松开。
/// </summary>
public static class TestLog
{
    private static readonly ConcurrentQueue<string> _lines = new();
    private static readonly object _fileLock = new();
    private static long _lastFlush;
    private static readonly string _filePath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CODMControllerProbe", "test_log.txt");

    public static string FilePath => _filePath;

    public static void Add(string line)
    {
        if(UiPreview.Enabled)return;
        string entry = $"{DateTime.Now:HH:mm:ss.fff} {line}";
        _lines.Enqueue(entry);
        while (_lines.Count > 800) _lines.TryDequeue(out _);
        // 批量写盘（≥150ms 一次）：原来每次调用都同步 File.AppendAllText（打开/写/关文件），
        // 高频日志（WASD/扳机边沿/映射）会拖慢输出循环 → 延迟感、不跟手。
        long now = Environment.TickCount64;
        if (now - _lastFlush >= 150) { _lastFlush = now; FlushFile(); }
    }

    private static void FlushFile()
    {
        var batch = new List<string>();
        while (_lines.TryDequeue(out var line)) batch.Add(line);
        if (batch.Count == 0) return;
        try
        {
            lock (_fileLock)
            {
                var dir = System.IO.Path.GetDirectoryName(_filePath);
                if (dir != null) System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllLines(_filePath, batch);
            }
        }
        catch { /* 日志失败不影响主流程 */ }
    }

    /// <summary>拉取并清空内存队列（供窗口定时刷新展示）。</summary>
    public static string Drain()
    {
        FlushFile();
        try { return System.IO.File.ReadAllText(_filePath); } catch { return ""; }
    }

    /// <summary>清空日志文件（窗口打开时调用，便于每次测试从干净状态开始）。</summary>
    public static void ResetFile()
    {
        while (_lines.TryDequeue(out _)) { }
        try
        {
            lock (_fileLock)
            {
                var dir = System.IO.Path.GetDirectoryName(_filePath);
                if (dir != null) System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(_filePath, "—— 测试记录开始 ——\n");
            }
        }
        catch { /* 忽略 */ }
    }
}
