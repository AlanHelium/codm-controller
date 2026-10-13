using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace CODMControllerProbe;

/// <summary>
/// HidHide 驱动配置封装（官方 IOCTL 协议）。
/// 控制设备：\\.\HidHide（无需管理员权限）；同一时刻仅一个句柄，用完即关。
/// 功能号（docs.nefarius.at/projects/HidHide/API-Documentation + DEVELOPER.md）：
///   2048/2049 whitelist get/set；2050/2051 blacklist get/set；
///   2052/2053 active get/set；2054/2055 whitelist-inverse get/set；
///   2056/2057 session-blacklist add/clear（仅新驱动支持，老驱动 1.4.181 返回错误 87，不可用）。
/// 列表 payload 为 MULTI_SZ（宽字符、双重 \0 结尾，字节数必须为偶数）。
/// 隐藏方案：持久黑名单（2050/2051，官方客户端同款，老驱动支持）；启动时备份原值，退出时恢复。
/// </summary>
public static class HidHideManager
{
    /// <summary>最近一次 HideDevices 的失败环节（空=成功/未执行），用于真实环境定位双输入根因。</summary>
    public static string LastError = "";

    private const uint FN_GET_WHITELIST = 2048, FN_SET_WHITELIST = 2049;
    private const uint FN_GET_BLACKLIST = 2050, FN_SET_BLACKLIST = 2051;
    private const uint FN_GET_ACTIVE = 2052, FN_SET_ACTIVE = 2053;
    private const uint FN_GET_WHITELIST_INVERSE = 2054, FN_SET_WHITELIST_INVERSE = 2055;
    private const uint FN_ADD_SESSION_BLACKLIST = 2056, FN_CLR_SESSION_BLACKLIST = 2057;

    // 启动前快照（HideDevices 前调用），Restore 时恢复
    private static bool _savedActive;
    private static bool _savedInverse;
    private static List<string> _savedBlacklist = new();
    private static List<string> _savedWhitelist = new();
    private static bool _saved;
    private static readonly object Gate = new();
    private static string SnapshotPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CODMControllerProbe", "hidhide-session.json");

    private sealed class PersistedSnapshot
    {
        public PersistedSnapshot() { }
        public int OwnerPid { get; set; }
        public long OwnerStartTicks { get; set; }
        public List<string> AppliedBlacklist { get; set; } = new();
        public List<string> AppliedWhitelist { get; set; } = new();
        public bool Active { get; set; }
        public bool Inverse { get; set; }
        public List<string> Blacklist { get; set; } = new();
        public List<string> Whitelist { get; set; } = new();
    }

    public sealed class SnapshotResult
    {
        public bool Success;
        public string Error = "";
        public bool Active;
        public bool Inverse;
        public List<string> Blacklist = new();
        public List<string> Whitelist = new();
    }

    /// <summary>读取当前 active/持久黑名单/白名单快照。</summary>
    public static SnapshotResult TakeSnapshot()
    {
        var result = new SnapshotResult();
        if (!TryGetActive(out result.Active))
        {
            result.Error = "读取 HidHide 启用状态失败";
            return result;
        }
        if (!TryGetInverse(out result.Inverse))
        {
            result.Error = "读取 HidHide 反向白名单状态失败";
            return result;
        }

        var black = QueryList(FN_GET_BLACKLIST, out bool blackOk);
        if (!blackOk || black == null)
        {
            result.Error = "读取 HidHide 黑名单失败";
            return result;
        }

        var white = QueryList(FN_GET_WHITELIST, out bool whiteOk);
        if (!whiteOk || white == null)
        {
            result.Error = "读取 HidHide 白名单失败";
            return result;
        }

        result.Blacklist = DecodeMultiSz(black);
        result.Whitelist = DecodeMultiSz(white);
        result.Success = true;
        return result;
    }

    /// <summary>
    /// 隐藏指定实体手柄：白名单追加本进程 + 黑名单合并设备 + 启用 active。
    /// 必须先于游戏启动调用；返回实际加入黑名单的设备数（0 表示无可隐藏设备或调用失败）。
    /// </summary>
    private static readonly HashSet<string> SessionIds = new(StringComparer.OrdinalIgnoreCase);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern uint QueryDosDevice(string drive,StringBuilder target,int count);
    public static string ToDriverImagePath(string path)
    {
        if(path.StartsWith(@"\Device\",StringComparison.OrdinalIgnoreCase)) return path;
        path=Path.GetFullPath(path);
        if(path.Length<3 || path[1]!=':') throw new IOException("不支持的程序路径："+path);
        var target=new StringBuilder(32768);
        if(QueryDosDevice(path[..2],target,target.Capacity)==0) throw new IOException("无法解析程序所在磁盘");
        return target.ToString().Split('\0')[0]+path[2..];
    }

    public static int HideDevices(IEnumerable<string> deviceIds)
    {
        lock (Gate) return HideDevicesCore(deviceIds);
    }

    private static int HideDevicesCore(IEnumerable<string> deviceIds)
    {
        var ids=deviceIds.Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        LastError="";
        if(ids.Count==0) { LastError="输入已连接，但没有找到对应的可隐藏实体设备节点"; return 0; }
        string self;
        try
        {
            var path=Environment.ProcessPath??throw new IOException("无法读取程序路径");
            if(Path.GetFileName(path).Equals("dotnet.exe",StringComparison.OrdinalIgnoreCase)) throw new IOException("请使用发布版 EXE 启动转换，不能将共享 dotnet 宿主加入白名单");
            self=ToDriverImagePath(path);
        }
        catch(Exception ex) { LastError=ex.Message; return 0; }
        if(!_saved)
        {
            if(File.Exists(SnapshotPath)) { LastError="上次会话尚待恢复"; return 0; }
            var snap=TakeSnapshot();
            if(!snap.Success) { LastError=snap.Error; return 0; }
            if(snap.Inverse) {LastError="HidHide 开启了反向白名单，请在配置客户端关闭该模式";return 0;}
            if(!TrySaveSnapshot(snap)) {LastError="无法保存恢复快照，设备配置未修改";return 0;}
            _savedActive=snap.Active; _savedInverse=snap.Inverse; _savedBlacklist=snap.Blacklist; _savedWhitelist=snap.Whitelist; _saved=true;
            SessionIds.Clear();
        }
        SessionIds.UnionWith(ids);
        var white=new List<string>(_savedWhitelist);
        if(!white.Contains(self,StringComparer.OrdinalIgnoreCase)) white.Add(self);
        var virtualIds=DeviceCatalog.Enumerate().Where(d=>d.Virtual).Select(d=>d.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var black=_savedBlacklist.Where(id=>!virtualIds.Contains(id)).Concat(SessionIds).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        int Fail(string error)
        {
            bool restored=Restore(); LastError=error+(restored?"；已恢复原配置":"；恢复失败："+LastError); return 0;
        }
        if (!TryLoadSnapshot(out var journal) || journal == null) return Fail("无法读取恢复记录");
        journal.AppliedBlacklist = black;
        journal.AppliedWhitelist = white;
        if (!WriteJournal(journal)) return Fail("无法更新恢复记录，未修改驱动配置");
        if(!SetWhitelist(white)) return Fail("写入转换器白名单失败");
        if(!SetBlacklist(black)) return Fail("写入实体设备隐藏名单失败");
        if(!SetActive(true)) return Fail("启用设备隐藏失败");
        var check=TakeSnapshot();
        if(!check.Success || !check.Active || check.Inverse || !check.Whitelist.Contains(self,StringComparer.OrdinalIgnoreCase) || SessionIds.Any(id=>!check.Blacklist.Contains(id,StringComparer.OrdinalIgnoreCase)))
            return Fail("隐藏规则写入后读回校验失败");
        return ids.Count;
    }

    /// <summary>恢复隐藏前状态（黑名单/白名单/active）。</summary>
    public static bool Restore()
    {
        lock (Gate) return RestoreCore();
    }

    private static bool RestoreCore()
    {
        if (!_saved)
        {
            if (!File.Exists(SnapshotPath)) { LastError = ""; return true; }
            if (!TryLoadSnapshot(out var diskSnapshot) || diskSnapshot == null)
            {
                LastError = "读取 HidHide 恢复快照失败";
                return false;
            }
            _savedActive = diskSnapshot.Active;
            _savedInverse = diskSnapshot.Inverse;
            _savedBlacklist = diskSnapshot.Blacklist;
            _savedWhitelist = diskSnapshot.Whitelist;
            _saved = true;
        }

        var current = TakeSnapshot();
        if (!current.Success || !TryLoadSnapshot(out var journal) || journal == null)
        {
            LastError = "读取恢复记录或当前驱动配置失败；快照已保留";
            return false;
        }
        var targetBlack = MergeRestore(current.Blacklist, _savedBlacklist, journal.AppliedBlacklist);
        var targetWhite = MergeRestore(current.Whitelist, _savedWhitelist, journal.AppliedWhitelist);
        bool foreignAdded = current.Blacklist.Except(journal.AppliedBlacklist, StringComparer.OrdinalIgnoreCase).Any();
        bool targetActive = foreignAdded ? current.Active : _savedActive;
        bool targetInverse = current.Inverse;
        bool blackOk = SetBlacklist(targetBlack);
        bool whiteOk = SetWhitelist(targetWhite);
        bool inverseOk = true;
        bool activeOk = SetActive(targetActive);
        if (!blackOk || !whiteOk || !inverseOk || !activeOk)
        {
            LastError = "恢复 HidHide 原设置失败；恢复快照已保留";
            return false;
        }

        var verify = TakeSnapshot();
        if (!verify.Success || verify.Active != targetActive || verify.Inverse != targetInverse ||
            !SameItems(verify.Blacklist, targetBlack) || !SameItems(verify.Whitelist, targetWhite))
        {
            LastError = "HidHide 原设置校验不一致；恢复快照已保留";
            return false;
        }

        try { if (File.Exists(SnapshotPath)) File.Delete(SnapshotPath); }
        catch
        {
            LastError = "HidHide 已恢复，但无法删除恢复快照；下次启动会再次校验";
            return false;
        }

        _saved = false;
        SessionIds.Clear();
        _savedBlacklist = new();
        _savedWhitelist = new();
        LastError = "";
        return true;
    }

    /// <summary>启动新会话前恢复上次崩溃留下的本工具快照，不触碰没有快照的用户配置。</summary>
    public static bool RecoverPreviousSession(out bool recovered, out string error)
    {
        recovered = File.Exists(SnapshotPath);
        if (!recovered) { error = ""; return true; }
        bool ok = Restore();
        error = ok ? "" : LastError;
        return ok;
    }

    private static bool TrySaveSnapshot(SnapshotResult snapshot)
    {
        string? dir = Path.GetDirectoryName(SnapshotPath);
        if (string.IsNullOrEmpty(dir)) return false;
        string temp = SnapshotPath + ".tmp";
        try
        {
            Directory.CreateDirectory(dir);
            var data = new PersistedSnapshot
            {
                OwnerPid = Environment.ProcessId,
                OwnerStartTicks = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks,
                AppliedBlacklist = snapshot.Blacklist,
                AppliedWhitelist = snapshot.Whitelist,
                Active = snapshot.Active,
                Inverse = snapshot.Inverse,
                Blacklist = snapshot.Blacklist,
                Whitelist = snapshot.Whitelist,
            };
            File.WriteAllText(temp, JsonSerializer.Serialize(data));
            File.Move(temp, SnapshotPath, overwrite: true);
            return true;
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            return false;
        }
    }

    private static bool TryLoadSnapshot(out PersistedSnapshot? snapshot)
    {
        snapshot = null;
        try
        {
            if (!File.Exists(SnapshotPath)) return false;
            snapshot = JsonSerializer.Deserialize<PersistedSnapshot>(File.ReadAllText(SnapshotPath));
            return snapshot != null && snapshot.Blacklist != null && snapshot.Whitelist != null;
        }
        catch { return false; }
    }

    public static bool RecoverOwner(int pid, long startTicks)
    {
        lock (Gate)
        {
            if (!TryLoadSnapshot(out var journal) || journal == null ||
                journal.OwnerPid != pid || journal.OwnerStartTicks != startTicks) return true;
            return RestoreCore();
        }
    }

    public static List<string> MergeRestore(List<string> current, List<string> original, List<string> applied)
        => current.Except(applied.Except(original, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase)
            .Concat(original.Except(applied, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static bool WriteJournal(PersistedSnapshot data)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SnapshotPath)!);
            File.WriteAllText(SnapshotPath + ".tmp", JsonSerializer.Serialize(data));
            File.Move(SnapshotPath + ".tmp", SnapshotPath, true);
            return true;
        }
        catch { return false; }
    }

    private static bool SameItems(List<string> a, List<string> b)
        => a.Count == b.Count && a.All(x => b.Contains(x, StringComparer.OrdinalIgnoreCase));

    // CTL_CODE(32769, fn, METHOD_BUFFERED(0), FILE_READ_DATA(1)) = (32769<<16)|(1<<14)|(fn<<2)
    private static uint Io(uint fn) => 0x80010000u | 0x4000u | (fn << 2);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(IntPtr h, uint code, byte[]? inBuf, uint inSize, byte[]? outBuf, uint outSize, out uint ret, IntPtr ov);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr h);

    private const uint GENERIC_READ = 0x80000000;
    private const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, FILE_SHARE_DELETE = 4;
    private const uint OPEN_EXISTING = 3, FILE_ATTRIBUTE_NORMAL = 0x80;

    /// <summary>检测 HidHide 驱动是否已安装（服务注册表项存在）。</summary>
    public static bool IsInstalled()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\HidHide");
            return k != null;
        }
        catch { return false; }
    }

    private static IntPtr Open()
        => CreateFile(@"\\.\HidHide", GENERIC_READ,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            IntPtr.Zero, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);

    // ---------- 状态 ----------

    public static bool GetActive()
        => TryGetActive(out bool active) && active;

    private static bool TryGetActive(out bool active)
    {
        active = false;
        var h = Open();
        if (h == new IntPtr(-1)) return false;
        try
        {
            var buf = new byte[1];
            if (!DeviceIoControl(h, Io(FN_GET_ACTIVE), null, 0, buf, 1, out _, IntPtr.Zero)) return false;
            active = buf[0] == 1;
            return true;
        }
        finally { CloseHandle(h); }
    }

    public static bool SetActive(bool on)
    {
        var h = Open();
        if (h == new IntPtr(-1)) return false;
        try
        {
            var buf = new[] { (byte)(on ? 1 : 0) };
            return DeviceIoControl(h, Io(FN_SET_ACTIVE), buf, 1, null, 0, out _, IntPtr.Zero);
        }
        finally { CloseHandle(h); }
    }

    public static bool TryGetInverse(out bool inverse)
    {
        inverse = false;
        var h = Open();
        if (h == new IntPtr(-1)) return false;
        try
        {
            var buf = new byte[1];
            if (!DeviceIoControl(h, Io(FN_GET_WHITELIST_INVERSE), null, 0, buf, 1, out _, IntPtr.Zero)) return false;
            inverse = buf[0] != 0;
            return true;
        }
        finally { CloseHandle(h); }
    }

    private static bool SetInverse(bool inverse)
    {
        var h = Open();
        if (h == new IntPtr(-1)) return false;
        try
        {
            var buf = new[] { (byte)(inverse ? 1 : 0) };
            return DeviceIoControl(h, Io(FN_SET_WHITELIST_INVERSE), buf, 1, null, 0, out _, IntPtr.Zero);
        }
        finally { CloseHandle(h); }
    }

    // ---------- 列表 ----------

    private static byte[]? QueryList(uint fn, out bool ok)
    {
        var h = Open();
        ok = false;
        if (h == new IntPtr(-1)) return null;
        try
        {
            DeviceIoControl(h, Io(fn), null, 0, null, 0, out uint required, IntPtr.Zero);
            if (required == 0 || required > 1 << 20) return null;
            var buf = new byte[required];
            ok = DeviceIoControl(h, Io(fn), null, 0, buf, required, out _, IntPtr.Zero);
            return ok ? buf : null;
        }
        finally { CloseHandle(h); }
    }

    private static bool SetList(uint fn, IEnumerable<string> items)
    {
        var payload = EncodeMultiSz(items);
        var h = Open();
        if (h == new IntPtr(-1)) return false;
        try
        {
            return DeviceIoControl(h, Io(fn), payload, (uint)payload.Length, null, 0, out _, IntPtr.Zero);
        }
        finally { CloseHandle(h); }
    }

    public static List<string> GetWhitelist()
    {
        var buf = QueryList(FN_GET_WHITELIST, out _);
        return buf == null ? new List<string>() : DecodeMultiSz(buf);
    }

    public static bool SetWhitelist(IEnumerable<string> paths) => SetList(FN_SET_WHITELIST, paths);

    public static List<string> GetBlacklist()
    {
        var buf = QueryList(FN_GET_BLACKLIST, out _);
        return buf == null ? new List<string>() : DecodeMultiSz(buf);
    }

    public static bool SetBlacklist(IEnumerable<string> ids) => SetList(FN_SET_BLACKLIST, ids);

    /// <summary>会话级隐藏：进程退出（含崩溃）时驱动自动清理，无需回滚。</summary>
    public static bool AddSessionBlacklist(IEnumerable<string> ids) => SetList(FN_ADD_SESSION_BLACKLIST, ids);

    public static bool ClearSessionBlacklist() => SetList(FN_CLR_SESSION_BLACKLIST, Array.Empty<string>());

    // ---------- 编码 ----------

    private static byte[] EncodeMultiSz(IEnumerable<string> items)
    {
        var list = items.Where(s => !string.IsNullOrEmpty(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var text = list.Count == 0 ? "\0\0" : string.Join("\0", list) + "\0\0";
        return Encoding.Unicode.GetBytes(text);
    }

    private static List<string> DecodeMultiSz(byte[] buf)
    {
        var text = Encoding.Unicode.GetString(buf);
        return text.Split('\0', StringSplitOptions.RemoveEmptyEntries).ToList();
    }
}
