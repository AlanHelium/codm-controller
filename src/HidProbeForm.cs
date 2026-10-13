using System.Runtime.InteropServices;

namespace CODMControllerProbe;

/// <summary>
/// 原始 HID 报告查看器（实验工具）：枚举全部游戏手柄 HID 接口路径，
/// 后台线程实时读取输入报告，以 HEX 显示最近若干帧，用于定位国产手柄的陀螺仪字节布局。
/// 用法：Xbox 模式抓一帧看基础布局 → 切到「模拟 DS4 / Switch」模式再抓，对比找陀螺仪。
/// </summary>
public class HidProbeForm : Form
{
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, FILE_SHARE_DELETE = 4;
    private const uint OPEN_EXISTING = 3, FILE_ATTRIBUTE_NORMAL = 0x80;
    private const uint ERROR_IO_PENDING = 997;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr h);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(IntPtr h, byte[] buf, uint count, out uint read, ref OVERLAPPED ov);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetOverlappedResult(IntPtr h, ref OVERLAPPED ov, out uint read, bool wait);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr h, uint ms);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ResetEvent(IntPtr h);

    [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool HidD_GetProductString(IntPtr h, System.Text.StringBuilder buffer, uint bufferLength);

    [StructLayout(LayoutKind.Sequential)]
    private struct OVERLAPPED
    {
        public IntPtr Internal;
        public IntPtr InternalHigh;
        public int Offset;
        public int OffsetHigh;
        public IntPtr hEvent;
    }

    private readonly ComboBox _devices = new();
    private readonly Button _refresh = new();
    private readonly Label _meta = new();
    private readonly TextBox _dump = new();
    private readonly Button _copy = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 100 };

    private IntPtr _h = IntPtr.Zero;
    private Thread? _thread;
    private volatile bool _stop;
    private volatile bool _opened;
    private readonly object _lock = new();
    private readonly byte[] _last = new byte[64];
    private readonly List<string> _frames = new();
    private readonly List<(string Path, string Name)> _devList = new();

    public HidProbeForm()
    {
        Dpi.Scale = Math.Max(1f, DeviceDpi / 96f);
        Text = Lang.T("原始 HID 报告查看（实验）", "Raw HID Report Viewer (experimental)");
        ClientSize = new Size(Dpi.Px(760), Dpi.Px(560));
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        Font = Assets.Ui(9.5F);
        Assets.Skin(this);

        int m = Dpi.Px(20), w = ClientSize.Width - Dpi.Px(40);

        var lbl = new Label
        {
            Text = Lang.T("选择手柄设备：", "Select pad device:"),
            Font = Assets.Ui(9.5F, FontStyle.Bold), ForeColor = Assets.MainText,
            Location = new Point(m, Dpi.Px(16)), Size = new Size(Dpi.Px(130), Dpi.Px(26)),
        };
        Controls.Add(lbl);

        _devices.Location = new Point(m + Dpi.Px(136), Dpi.Px(14));
        _devices.Size = new Size(w - Dpi.Px(280), Dpi.Px(28));
        _devices.DropDownStyle = ComboBoxStyle.DropDownList;
        _devices.SelectedIndexChanged += (_, _) => OpenSelected();
        Controls.Add(_devices);

        _refresh.Text = Lang.T("刷新设备", "Refresh");
        _refresh.Location = new Point(m + w - Dpi.Px(144), Dpi.Px(14));
        _refresh.Size = new Size(Dpi.Px(144), Dpi.Px(28));
        _refresh.FlatStyle = FlatStyle.Flat;
        Assets.StyleOutlineDark(_refresh, 8);
        _refresh.Click += (_, _) => RefreshDevices();
        Controls.Add(_refresh);

        _meta.Location = new Point(m, Dpi.Px(50));
        _meta.Size = new Size(w, Dpi.Px(40));
        _meta.ForeColor = Assets.WarnOrange;
        _meta.Font = Assets.Ui(9F);
        Controls.Add(_meta);

        _dump.Location = new Point(m, Dpi.Px(96));
        _dump.Size = new Size(w, Dpi.Px(400));
        _dump.Multiline = true;
        _dump.ReadOnly = true;
        _dump.ScrollBars = ScrollBars.Vertical;
        _dump.Font = new Font("Consolas", 9.5F);
        _dump.BackColor = Color.FromArgb(18, 24, 38);
        _dump.ForeColor = Color.FromArgb(210, 220, 235);
        _dump.WordWrap = false;
        Controls.Add(_dump);

        _copy.Text = Lang.T("复制最近报告（发给我分析）", "Copy recent reports (send for analysis)");
        _copy.Location = new Point(m, Dpi.Px(508));
        _copy.Size = new Size(w, Dpi.Px(36));
        _copy.FlatStyle = FlatStyle.Flat;
        Assets.StyleOutlineDark(_copy, 10);
        _copy.Click += (_, _) => CopyReports();
        Controls.Add(_copy);

        var tip = new Label
        {
            Text = Lang.T("提示：Xbox 模式无陀螺仪字节。请先连上手柄 → 选设备 → 看基础报告；然后把国产手柄切到「模拟 DS4 / Switch」模式再连一次，重新选设备抓报告对比。",
                "Tip: Xbox mode carries no gyro bytes. Connect the pad, pick it, watch the base report; then switch the pad to \"DS4 emulation / Switch\" mode, reconnect, and re-pick to compare."),
            Font = Assets.Ui(8F), ForeColor = Assets.SubText,
            AutoSize = true,
            MaximumSize = new Size(w, 0),
            Location = new Point(m, Dpi.Px(524 + 40)),
        };
        Controls.Add(tip);

        _timer.Tick += (_, _) => RefreshDump();
        _timer.Start();
        FormClosed += (_, _) => CloseDevice();

        RefreshDevices();
    }

    private void RefreshDevices()
    {
        _devList.Clear();
        _devices.Items.Clear();
        // 用 HidEnum.Enumerate() 拿设备名称/VID/PID（并排除虚拟手柄），再匹配 HID 接口路径
        var paths = HidEnum.EnumerateHidInterfacePathsPublic();
        var devNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dev in HidEnum.Enumerate())
        {
            if (dev.IsVirtualGamepad) continue;
            if (dev.Vid.Length > 0 && dev.Pid.Length > 0)
                devNames["VID_" + dev.Vid + " PID_" + dev.Pid] = dev.Name;
        }

        foreach (var path in paths)
        {
            if (!path.StartsWith(@"\\?\hid#", StringComparison.OrdinalIgnoreCase)) continue;
            if (!path.Contains("&ig_", StringComparison.OrdinalIgnoreCase)) continue;
            var vp = System.Text.RegularExpressions.Regex.Match(path,
                @"vid_([0-9a-f]{4})&pid_([0-9a-f]{4})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            string key = vp.Success
                ? "VID_" + vp.Groups[1].Value.ToUpperInvariant() + " PID_" + vp.Groups[2].Value.ToUpperInvariant()
                : "";
            string name = (key.Length > 0 && devNames.TryGetValue(key, out var dn)) ? dn : key;
            if (name.Length == 0) name = path;
            // ViGEmBus 虚拟手柄（伪装 Sony VID_054C）直接剔除：名称含 ViGEm / Scp VBus
            if (name.Contains("ViGEm", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Scp VBus", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Virtual", StringComparison.OrdinalIgnoreCase)) continue;
            _devList.Add((path, name));
            _devices.Items.Add(name);
        }
        if (_devices.Items.Count > 0) _devices.SelectedIndex = 0;
        else _meta.Text = Lang.T("未发现可用的手柄 HID 设备。请确认手柄已连接；Xbox 模式手柄请先切换到「模拟 DS4 / Switch」模式。",
            "No readable pad HID devices found. Make sure the pad is connected; Xbox-mode pads must be switched to \"DS4 / Switch emulation\" first.");
    }

    private static string ShortName(string path)
    {
        var vp = System.Text.RegularExpressions.Regex.Match(path,
            @"vid_([0-9a-f]{4})&pid_([0-9a-f]{4})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return vp.Success
            ? $"VID_{vp.Groups[1].Value.ToUpperInvariant()} PID_{vp.Groups[2].Value.ToUpperInvariant()}"
            : path;
    }

    private void OpenSelected()
    {
        CloseDevice();
        _frames.Clear();
        _dump.Text = "";
        if (_devices.SelectedIndex < 0 || _devices.SelectedIndex >= _devList.Count) return;

        var (path, name) = _devList[_devices.SelectedIndex];
        _h = CreateFile(path, GENERIC_READ | GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            IntPtr.Zero, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);
        if (_h == new IntPtr(-1))
        {
            // 部分手柄拒绝写权限：退化为只读重试
            _h = CreateFile(path, GENERIC_READ,
                FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                IntPtr.Zero, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);
        }
        if (_h == new IntPtr(-1))
        {
            _h = IntPtr.Zero;
            int err = Marshal.GetLastWin32Error();
            string reason = err switch
            {
                5 => Lang.T("拒绝访问（Access denied）", "Access denied"),
                32 => Lang.T("被其他程序独占（Sharing violation）", "Exclusively held by another program"),
                204 or 206 => Lang.T("设备正忙/已被占用（Device busy/in use）", "Device busy / in use"),
                _ => "Win32 " + err,
            };
            _meta.Text = Lang.T("无法打开该设备：" + reason + "。\nXbox360 模式手柄的 HID 接口被系统 XInput 驱动独占——请把天健 Pro 等手柄切换到「模拟 DS4 / Switch」模式后，点「刷新设备」再选。",
                "Cannot open: " + reason + ".\nXbox360-mode pads have their HID interface locked by Windows' XInput driver — switch the pad to \"DS4 / Switch emulation\" mode, then hit Refresh and re-pick.");
            return;
        }

        var sb = new System.Text.StringBuilder(256);
        string prod = HidD_GetProductString(_h, sb, 256) && sb.Length > 0
            ? sb.ToString().Trim() : "";
        _meta.Text = name + (prod.Length > 0 ? " · " + prod : "") +
            Lang.T("　· 读取中（摇动/按键试试）", " · reading (wiggle/press to see frames)");

        _stop = false;
        _opened = true;
        _thread = new Thread(ReadLoop) { IsBackground = true, Name = "HidProbeRead" };
        _thread.Start();
    }

    private void ReadLoop()
    {
        _evt.Reset();
        var ov = new OVERLAPPED { hEvent = _evt.SafeWaitHandle.DangerousGetHandle() };
        while (!_stop)
        {
            var buf = new byte[64];
            bool ok;
            if (ReadFile(_h, buf, 64, out _, ref ov))
            {
                ok = true;
            }
            else if (Marshal.GetLastWin32Error() == ERROR_IO_PENDING)
            {
                if (WaitForSingleObject(_evt.SafeWaitHandle.DangerousGetHandle(), 120) == 0)
                    ok = GetOverlappedResult(_h, ref ov, out _, false);
                else ok = false;
            }
            else ok = false;

            if (ok)
            {
                _evt.Reset();
                lock (_lock)
                {
                    Array.Copy(buf, _last, 64);
                    string hex = string.Join(" ", buf.Select(b => b.ToString("X2")));
                    _frames.Add(DateTime.Now.ToString("HH:mm:ss.fff") + "  " + hex);
                    if (_frames.Count > 30) _frames.RemoveAt(0);
                }
                if (_stop) break;
            }
            else if (_stop) break;
        }
        var h = _h;
        if (h != IntPtr.Zero && h != new IntPtr(-1))
        {
            _h = IntPtr.Zero;
            CloseHandle(h);
        }
        _opened = false;
    }

    private System.Threading.ManualResetEvent _evt = new(false);

    private void RefreshDump()
    {
        lock (_lock)
        {
            if (_frames.Count == 0) return;
            _dump.Text = string.Join("\r\n", _frames);
            _dump.SelectionStart = _dump.TextLength;
            _dump.ScrollToCaret();
        }
    }

    private void CopyReports()
    {
        lock (_lock)
        {
            if (_frames.Count == 0) return;
            Clipboard.SetText(string.Join("\r\n", _frames));
        }
        MessageBox.Show(this,
            Lang.T("已复制最近 " + _frames.Count + " 帧报告到剪贴板，直接粘贴发给作者分析即可。",
                "Copied last " + _frames.Count + " frames to clipboard; paste and send to the author."),
            Lang.T("原始 HID 报告", "Raw HID Reports"), MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void CloseDevice()
    {
        _stop = true;
        _evt.Set();
        if (_thread != null)
        {
            if (_thread.IsAlive && _thread.ManagedThreadId != Environment.CurrentManagedThreadId)
                _thread.Join(120);
            _thread = null;
        }
        if (_h != IntPtr.Zero && _h != new IntPtr(-1))
        {
            CloseHandle(_h);
            _h = IntPtr.Zero;
        }
        _opened = false;
    }
}
