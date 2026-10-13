using Microsoft.Win32.SafeHandles;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
namespace CODMControllerProbe;

public static class SonyHidPad
{
    private sealed class Session
    {
        public required ControllerDevice Device;
        public required FileStream Stream;
        public readonly CancellationTokenSource Cancel = new();
        public readonly ManualResetEventSlim FirstFrame = new();
        public SonyFrame Frame;
        public long LastTick, Frames;
        public byte ReportId;
        public SonyModel Model;
        public bool Bluetooth;
    }
    private static readonly object Gate = new(), Lifecycle = new();
    private static Session? _session;
    private static readonly ConcurrentQueue<string> Lines = new();
    private static readonly string LogPath = Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "CODMControllerProbe", "sony_diag.log");
    /// <summary>诊断日志路径（诊断报告读取用）。</summary>
    public static string DiagLogPath => LogPath;
    public static string LastProbeInfo = "", LastReportHex = "";
    public static bool IsExclusive => false;
    public static bool SharedFallback => false;
    public static bool IsOpen { get { lock (Gate) return _session != null; } }
    public static string DeviceName { get { lock (Gate) return _session?.Device.Name ?? ""; } }
    public static string DevicePath { get { lock (Gate) return _session?.Device.Path ?? ""; } }
    public static string DeviceId { get { lock (Gate) return _session?.Device.Id ?? ""; } }
    public static long FramesReceived { get { lock (Gate) return _session?.Frames ?? 0; } }
    public static bool TouchpadPressed { get { lock (Gate) return Fresh(_session) && _session!.Frame.Touchpad; } }
    private static bool Fresh(Session? s) => s != null && s.LastTick != 0 && Environment.TickCount64 - s.LastTick <= 600;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security,
        uint mode, uint flags, IntPtr template);
    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_SetOutputReport(SafeFileHandle handle, byte[] report, uint length);

    public static bool Supported(ControllerDevice d) => !d.Virtual && !d.Protected && d.Gamepad && d.Path.Length > 0 &&
        d.Id.Contains("VID_054C", StringComparison.OrdinalIgnoreCase) &&
        new[] { "05C4", "09CC", "0CE6", "0E5A", "0DF2" }.Any(pid => d.Id.Contains("PID_" + pid, StringComparison.OrdinalIgnoreCase));
    public static List<string> FindPaths() => DeviceCatalog.Enumerate().Where(Supported).Select(d => d.Path).ToList();

    public static bool Open()
    {
        lock (Lifecycle)
        {
            Close(); ResetDiagLog();
            var candidates = DeviceCatalog.Enumerate().Where(Supported).ToList();
            LastProbeInfo = $"Sony HID 游戏集合：{candidates.Count}";
            foreach (var device in candidates)
            {
                var handle = CreateFile(device.Path, 0xc0000000, 7, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
                if (handle.IsInvalid) {
                    handle.Dispose();
                    handle = CreateFile(device.Path, 0x80000000, 7, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
                }
                if (handle.IsInvalid) {
                    int error = Marshal.GetLastWin32Error(); handle.Dispose();
                    LastProbeInfo += $"\n{device.Id}: 打开失败 {error}"; continue;
                }
                FileStream stream;
                try { stream = new(handle, FileAccess.Read, Math.Max(78, (int)device.ReportLength), true); }
                catch (Exception ex) { handle.Dispose(); LastProbeInfo += "\n" + ex.Message; continue; }
                var s = new Session { Device = device, Stream = stream,
                    Model = device.Id.Contains("PID_0DF2", StringComparison.OrdinalIgnoreCase) ? SonyModel.Edge :
                        device.Id.Contains("PID_05C4", StringComparison.OrdinalIgnoreCase) || device.Id.Contains("PID_09CC", StringComparison.OrdinalIgnoreCase) ? SonyModel.Ds4 : SonyModel.DualSense,
                    Bluetooth = device.Ancestors.Any(id => id.StartsWith("BTH", StringComparison.OrdinalIgnoreCase)) };
                lock (Gate) _session = s;
                _ = ReadLoop(s);
                if (s.FirstFrame.Wait(1200) && Read(out _)) {
                    LastProbeInfo += $"\n{device.Id}: OK，报告长度 {device.ReportLength}";
                    TestLog.Add($"Sony 输入已打开：{KindName()} · 共享读取 · 直连输出 · {device.Id}");
                    return true;
                }
                Close(); LastProbeInfo += $"\n{device.Id}: 未收到有效游戏报告";
            }
            return false;
        }
    }

    private static async Task ReadLoop(Session s)
    {
        var buffer = new byte[Math.Max(78, (int)s.Device.ReportLength)];
        long lastLog = 0; SonyFrame prior = default;
        try {
            while (!s.Cancel.IsCancellationRequested) {
                int count = await s.Stream.ReadAsync(buffer, s.Cancel.Token);
                if (count == 0) break;
                if (s.Device.ReportLength > 0 && count != s.Device.ReportLength) continue;
                if (!SonyReportParser.TryParse(buffer.AsSpan(0, count), s.Model, s.Bluetooth, out var frame)) continue;
                long now = Environment.TickCount64;
                lock (Gate) {
                    if (_session != s) break;
                    s.Frame = frame; s.LastTick = now; s.Frames++; s.ReportId = buffer[0];
                    LastReportHex = Convert.ToHexString(buffer, 0, Math.Min(count, 36));
                    s.FirstFrame.Set();
                }
                bool changed = prior.Gamepad.wButtons != frame.Gamepad.wButtons || prior.Touchpad != frame.Touchpad ||
                    Math.Abs(prior.Gamepad.bLeftTrigger - frame.Gamepad.bLeftTrigger) >= 8 ||
                    Math.Abs(prior.Gamepad.bRightTrigger - frame.Gamepad.bRightTrigger) >= 8;
                if (changed || now - lastLog >= 1000) {
                    string line = $"{DateTime.Now:HH:mm:ss.fff} ID={buffer[0]:X2} LEN={count} " + LastReportHex +
                        $" | btn={frame.Gamepad.wButtons:X4} L2={frame.Gamepad.bLeftTrigger} R2={frame.Gamepad.bRightTrigger} touch={frame.Touchpad}";
                    Lines.Enqueue(line); while (Lines.Count > 400) Lines.TryDequeue(out _);
                    try { File.AppendAllText(LogPath, line + Environment.NewLine); } catch { }
                    lastLog = now; prior = frame;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (IOException ex) { TestLog.Add("Sony 输入已断开：" + ex.Message); }
        catch (Exception ex) { CrashLog.Write("Sony 输入读取失败：" + ex); }
        finally {
            lock (Gate) { if (_session == s) _session = null; }
            s.Stream.Dispose(); s.Cancel.Dispose();
        }
    }
    public static void Close()
    {
        lock (Lifecycle) {
            Session? s;
            lock (Gate) { s = _session; _session = null; }
            if (s == null) return;
            try { s.Cancel.Cancel(); } catch (ObjectDisposedException) { }
            s.Stream.Dispose();
        }
    }
    public static bool Read(out XInputNative.XInputGamepad gamepad)
    {
        lock (Gate) { gamepad = default; if (!Fresh(_session)) return false; gamepad = _session!.Frame.Gamepad; return true; }
    }
    public static bool GyroRead(out int x, out int y, out int z)
    {
        lock (Gate) {
            x = y = z = 0;
            if (!Fresh(_session) || !_session!.Frame.GyroValid) return false;
            var f = _session.Frame; x = f.GyroX; y = f.GyroY; z = f.GyroZ; return true;
        }
    }
    public static void SetRumble(byte large, byte small)
    {
        lock (Gate) {
            if (_session == null || _session.Model != SonyModel.Ds4 || _session.Bluetooth || _session.ReportId != 1) return;
            byte[] report = new byte[32]; report[0] = 5; report[1] = 1; report[4] = small; report[5] = large;
            try { HidD_SetOutputReport(_session.Stream.SafeFileHandle, report, 32); } catch (ObjectDisposedException) { }
        }
    }
    public static string KindName() {
        lock (Gate) return _session?.Model switch { SonyModel.Ds4 => "DualShock 4", SonyModel.Edge => "DualSense Edge", SonyModel.DualSense => "DualSense", _ => "Sony" };
    }
    public static string ModeNote() => Lang.T("共享读取 · 直连输出", "shared read · direct-drive output");
    public static void ResetDiagLog() {
        while (Lines.TryDequeue(out _)) { }
        LastReportHex = "";
        try { Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!); File.WriteAllText(LogPath, $"Sony HID v3.1.0 {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n"); } catch { }
    }
    public static string DrainDiagLines() {
        var sb = new System.Text.StringBuilder(); while (Lines.TryDequeue(out var line)) sb.AppendLine(line); return sb.ToString();
    }
}
