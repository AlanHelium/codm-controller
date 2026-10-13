using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
namespace CODMControllerProbe;

public sealed class IsolationAccess
{
    public string Id { get; set; } = "";
    public string Path { get; set; } = "";
    public bool Virtual { get; set; }
    public int Error { get; set; }
    public string ReportHex { get; set; } = "";
}
public sealed class IsolationResult
{
    public bool Verified { get; set; }
    public string Message { get; set; } = "";
    public List<IsolationAccess> Access { get; set; } = new();
    public List<int> XInputSlots { get; set; } = new();
}
public static class IsolationProbe
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, IncludeFields = true };
    private static string? _helper, _folder;
    private static readonly object Gate = new();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr sec, uint mode, uint flags, IntPtr template);

    public static IsolationResult Capture(IEnumerable<string> expectedIds,bool requireVirtual=true)
    {
        var expected = expectedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new IsolationResult();
        var devices = DeviceCatalog.Enumerate();
        foreach (var d in devices.Where(d => d.Path.Length > 0 && !d.Protected && (d.Gamepad || expected.Contains(d.Id)))) {
            var a = new IsolationAccess { Id = d.Id, Path = d.Path, Virtual = d.Virtual, Error = DeviceCatalog.TryOpen(d.Path) };
            if (requireVirtual && a.Error == 0 && d.Virtual && d.Id.Contains("VID_054C&PID_05C4", StringComparison.OrdinalIgnoreCase)) {
                a.ReportHex = ReadVirtual(d.Path);
            }
            result.Access.Add(a);
        }
        for (int i = 0; i < 4; i++) if (XInputNative.XInputGetState(i, out _) == 0) result.XInputSlots.Add(i);
        if(requireVirtual)return Evaluate(result, expected);
        var physical=result.Access.Where(a=>!a.Virtual&&expected.Contains(a.Id)).ToArray();
        result.Verified=physical.Length>0&&physical.All(a=>a.Error==5)&&!result.Access.Any(a=>!a.Virtual&&a.Error==0)&&result.XInputSlots.Count==0;
        result.Message=result.Verified?"实体输入隔离检查通过":"实体输入隔离尚未生效，请重新插拔手柄后重试；仍失败请检查连接组件";
        return result;
    }
    public static IsolationResult Evaluate(IsolationResult result, IEnumerable<string> expectedIds)
    {
        var expected = expectedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        // 只统计「探针实际枚举到且属于隐藏名单」的实体设备：USB 父接口（MI_00/IG_01 等）没有 HID 路径，
        // 探针测不到、永远无法满足 All(Error==5)——把不可探测的设备也算进判据会导致隔离永远误报失败、输出永久暂停。
        var probed = result.Access.Where(a => !a.Virtual && expected.Contains(a.Id)).ToList();
        bool allDenied = probed.Count > 0 && probed.All(a => a.Error == 5);
        bool leaks = result.Access.Any(a => !a.Virtual && a.Error == 0) || result.XInputSlots.Count > 0;
        bool virtualReadable = result.Access.Any(a => a.Virtual && a.ReportHex.Length > 0);
        result.Verified = allDenied && !leaks && virtualReadable;
        result.Message = result.Verified ? "隔离检查通过：实体全部拒绝访问，虚拟 DS4 报告可读" :
            leaks ? "隔离失败：实体输入仍可访问，请重新插拔手柄后重试；仍失败请检查 HidHide 白名单/驱动" :
            !allDenied ? "隔离未确认：实体设备未全部明确拒绝访问" : "隔离失败：虚拟 DS4 报告不可读";
        return result;
    }
    private static string ReadVirtual(string path)
    {
        using var h = CreateFile(path, 0x80000000, 7, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
        if (h.IsInvalid) return "";
        try {
            using var stream = new FileStream(h, FileAccess.Read, 128, true);
            using var cancel = new CancellationTokenSource(700);
            var buffer = new byte[128];
            int count = stream.ReadAsync(buffer, cancel.Token).AsTask().GetAwaiter().GetResult();
            return count >= 10 && buffer[0] == 1 ? Convert.ToHexString(buffer, 0, count) : "";
        } catch { return ""; }
    }
    public static IsolationResult Verify(List<string> expectedIds,bool requireVirtual=true)
    {
        lock (Gate) {
            try {
                string helper = EnsureHelper();
                string request = Path.Combine(_folder!, "request.json"), result = Path.Combine(_folder!, "result.json");
                File.WriteAllText(request, JsonSerializer.Serialize(expectedIds));
                if (File.Exists(result)) File.Delete(result);
                var start = StartInfo(helper);
                start.ArgumentList.Add(requireVirtual?"--isolation-probe":"--physical-isolation-probe"); start.ArgumentList.Add(request); start.ArgumentList.Add(result);
                using var process = Process.Start(start) ?? throw new IOException("无法启动隔离验证进程");
                if (!process.WaitForExit(7000)) { process.Kill(); throw new IOException("隔离检查超时"); }
                if (!File.Exists(result)) throw new IOException("隔离验证未返回报告");
                return JsonSerializer.Deserialize<IsolationResult>(File.ReadAllText(result)) ?? throw new IOException("隔离报告无效");
            } catch (Exception ex) { return new() { Message = "隔离验证失败：" + ex.Message }; }
        }
    }
    private static ProcessStartInfo StartInfo(string executable) => new(executable) {
        UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
    };
    private static string EnsureHelper()
    {
        if (_helper != null && File.Exists(_helper)) return _helper;
        string self = Environment.ProcessPath ?? throw new IOException("找不到程序路径");
        if (Path.GetFileName(self).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
            throw new IOException("请从自包含测试版 EXE 运行，不能使用 dotnet 宿主验证隔离");
        _folder = Path.Combine(Path.GetTempPath(), "CODMIsolation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        _helper = Path.Combine(_folder, "IsolationProbe.exe");
        File.Copy(self, _helper); // different executable path: deliberately NOT whitelisted
        return _helper;
    }
    public static void StartWatchdog()
    {
        lock (Gate) {
            var start = StartInfo(EnsureHelper());
            start.ArgumentList.Add("--isolation-watchdog");
            start.ArgumentList.Add(Environment.ProcessId.ToString());
            start.ArgumentList.Add(Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks.ToString());
            start.ArgumentList.Add(_folder!);
            Process.Start(start)?.Dispose();
        }
    }
    public static bool HandleArguments(string[] args)
    {
        if (args.Length == 2 && args[0] == "--driver-state") {
            File.WriteAllText(args[1], JsonSerializer.Serialize(HidHideManager.TakeSnapshot(), Json));
            return true;
        }
        if (args.Length == 3 && args[0] is "--isolation-probe" or "--physical-isolation-probe") {
            try {
                var ids = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(args[1])) ?? new();
                File.WriteAllText(args[2], JsonSerializer.Serialize(Capture(ids,requireVirtual:args[0]=="--isolation-probe"), Json));
            } catch (Exception ex) { File.WriteAllText(args[2], JsonSerializer.Serialize(new IsolationResult { Message = ex.Message }, Json)); }
            return true;
        }
        if (args.Length == 4 && args[0] == "--isolation-watchdog") {
            int pid = int.Parse(args[1]); long ticks = long.Parse(args[2]);
            try {
                using var parent = Process.GetProcessById(pid);
                if (parent.StartTime.ToUniversalTime().Ticks == ticks) parent.WaitForExit();
            } catch (ArgumentException) { }
            for (int i = 0; i < 3; i++) {
                if (HidHideManager.RecoverOwner(pid, ticks)) break;
                Thread.Sleep(300);
            }
            // Delete only files in the exact helper directory created by this session.
            try {
                string folder = Path.GetFullPath(args[3]);
                string self = Path.GetFullPath(Environment.ProcessPath!);
                if (Path.GetDirectoryName(self) == folder && Path.GetFileName(folder).StartsWith("CODMIsolation-", StringComparison.Ordinal)) {
                    foreach (string file in Directory.GetFiles(folder))
                        if (!file.Equals(self, StringComparison.OrdinalIgnoreCase)) File.Delete(file);
                    // Windows locks the running EXE; remove old helper directories on next startup.
                }
            } catch { }
            return true;
        }
        return false;
    }
}

