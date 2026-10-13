using System.Runtime.InteropServices;

namespace CODMControllerProbe;

/// <summary>
/// Xbox One / Xbox Series S|X 手柄（VID 045E）HID 直读回退通道。
/// 微软原装 One/Series 手柄在 Windows 下通常走 XInput（xinput1_4，有线/接收器/蓝牙经 HidBthGip），
/// 但部分第三方"Xbox One 协议"手柄没有 XInput 驱动、XInputGetState 枚举不到，
/// 此时从 HID 输入报告直读（有线报告 ID 0x07，布局与微软手柄一致）。
/// 键位归一化为 XInput 语义位后进既有转发管线，与 Sony/XInput 通道完全统一。
/// </summary>
public static class XboxHidPad
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

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_SetOutputReport(IntPtr h, byte[] buffer, uint bufferLength);

    /// <summary>向 Xbox One/Series 手柄发送经典输出报告设置震动（0x03 报告：[3]=左马达大，[4]=右马达小）。</summary>
    public static void SetRumble(byte large, byte small)
    {
        if (!_opened || _h == IntPtr.Zero) return;
        var buf = new byte[13];
        buf[0] = 0x03;      // Xbox One 经典输出报告 ID
        buf[3] = large;     // 左马达（低频大）
        buf[4] = small;     // 右马达（高频小）
        try { HidD_SetOutputReport(_h, buf, (uint)buf.Length); }
        catch { /* 蓝牙连接可能不支持输出报告，忽略 */ }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct OVERLAPPED
    {
        public IntPtr Internal;
        public IntPtr InternalHigh;
        public int Offset;
        public int OffsetHigh;
        public IntPtr hEvent;
    }

    private static IntPtr _h = IntPtr.Zero;
    private static string _path = "";
    private static string _name = "";
    private static volatile bool _opened;
    private static volatile bool _stop;
    private static Thread? _thread;
    private static System.Threading.ManualResetEvent _evt = new(false);
    private static readonly object _lock = new();
    private static XInputNative.XInputGamepad _latest;
    private static bool _hasData;

    public static bool IsOpen => _opened;
    public static string DeviceName => _name;
    public static string DevicePath => _path;

    /// <summary>枚举 HID 接口路径，找第一个微软 Xbox 手柄游戏控制器 ig_ 集合（VID 045E）。</summary>
    public static string FindPath()
    {
        foreach (var path in HidEnum.EnumerateHidInterfacePathsPublic())
        {
            if (!path.StartsWith(@"\\?\hid#", StringComparison.OrdinalIgnoreCase)) continue;
            if (!path.Contains("&ig_", StringComparison.OrdinalIgnoreCase)) continue;
            var mid = path.Substring(@"\\?\hid#".Length);
            int hash = mid.IndexOf('#');
            if (hash <= 0) continue;
            var devPart = mid[..hash];
            var vp = System.Text.RegularExpressions.Regex.Match(devPart,
                @"vid_([0-9a-f]{4})&pid_([0-9a-f]{4})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (vp.Success && vp.Groups[1].Value.Equals("045e", StringComparison.OrdinalIgnoreCase))
                return path;
        }
        return "";
    }

    /// <summary>打开 Xbox One/Series 手柄 HID 接口并启动后台读取线程。返回是否成功。</summary>
    public static bool Open()
    {
        Close();
        _path = FindPath();
        if (string.IsNullOrEmpty(_path)) return false;

        _h = CreateFile(_path, GENERIC_READ | GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            IntPtr.Zero, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);
        if (_h == new IntPtr(-1))
        {
            _h = IntPtr.Zero;
            return false;
        }

        var sb = new System.Text.StringBuilder(256);
        _name = (HidD_GetProductString(_h, sb, 256) && sb.Length > 0)
            ? sb.ToString().Trim()
            : _path.Split('#')[1].ToUpperInvariant();

        _opened = true;
        _stop = false;
        _hasData = false;
        _thread = new Thread(ReadLoop) { IsBackground = true, Name = "XboxHidRead" };
        _thread.Start();
        return true;
    }

    public static void Close()
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
        _path = "";
    }

    private static void ReadLoop()
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
                if (WaitForSingleObject(_evt.SafeWaitHandle.DangerousGetHandle(), 80) == 0)
                    ok = GetOverlappedResult(_h, ref ov, out _, false);
                else
                    ok = false;
            }
            else
            {
                ok = false;
            }

            if (ok)
            {
                _evt.Reset();
                ParseReport(buf);
                if (_stop) break;
            }
            else if (Marshal.GetLastWin32Error() != ERROR_IO_PENDING && _stop)
            {
                break;
            }
        }

        var h = _h;
        if (h != IntPtr.Zero && h != new IntPtr(-1))
        {
            _h = IntPtr.Zero;
            CloseHandle(h);
        }
        _opened = false;
    }

    /// <summary>
    /// 解析 Xbox One 有线输入报告（report ID 0x07，64 字节）：
    /// [0]=0x07
    /// [1]=buttons1: bit0=Up bit1=Down bit2=Left bit3=Right bit4=Start bit5=Back bit6=LS bit7=RS
    /// [2]=buttons2: bit0=LB bit1=RB bit2=Logo bit3=A bit4=B bit5=X bit6=Y bit7=Share(Series)
    /// [3..6]=LX LY RX RY（0..255 中心 128）
    /// [7]=LT [8]=RT
    /// </summary>
    private static void ParseReport(byte[] buf)
    {
        int rid = buf[0];
        if (rid != 0x07) return; // 本通道只处理有线 Xbox One 报告格式

        int b1 = buf[1], b2 = buf[2];
        ushort wButtons = 0;
        // buttons1
        if ((b1 & 0x01) != 0) wButtons |= 0x0001; // Up
        if ((b1 & 0x02) != 0) wButtons |= 0x0002; // Down
        if ((b1 & 0x04) != 0) wButtons |= 0x0004; // Left
        if ((b1 & 0x08) != 0) wButtons |= 0x0008; // Right
        if ((b1 & 0x10) != 0) wButtons |= 0x0010; // Start
        if ((b1 & 0x20) != 0) wButtons |= 0x0020; // Back
        if ((b1 & 0x40) != 0) wButtons |= 0x0040; // LS → LeftThumb
        if ((b1 & 0x80) != 0) wButtons |= 0x0080; // RS → RightThumb
        // buttons2
        if ((b2 & 0x01) != 0) wButtons |= 0x0100; // LB → LeftShoulder
        if ((b2 & 0x02) != 0) wButtons |= 0x0200; // RB → RightShoulder
        if ((b2 & 0x04) != 0) wButtons |= 0x0400; // Logo(Home 西瓜键) → 切光标/按键映射触发位
        if ((b2 & 0x08) != 0) wButtons |= 0x1000; // A
        if ((b2 & 0x10) != 0) wButtons |= 0x2000; // B
        if ((b2 & 0x20) != 0) wButtons |= 0x4000; // X
        if ((b2 & 0x40) != 0) wButtons |= 0x8000; // Y

        // 摇杆/扳机：0..255（中心 128）→ XInput -32768..32767；Y 反转（Xbox 下=0 → XInput 上=+32767）
        short sLX = ClampShort((buf[3] - 128) * 257);
        short sLY = ClampShort((128 - buf[4]) * 257);
        short sRX = ClampShort((buf[5] - 128) * 257);
        short sRY = ClampShort((128 - buf[6]) * 257);

        var g = new XInputNative.XInputGamepad
        {
            wButtons = wButtons,
            bLeftTrigger = buf[7],
            bRightTrigger = buf[8],
            sThumbLX = sLX,
            sThumbLY = sLY,
            sThumbRX = sRX,
            sThumbRY = sRY,
        };

        lock (_lock)
        {
            _latest = g;
            _hasData = true;
        }
    }

    private static short ClampShort(int v) => (short)Math.Clamp(v, short.MinValue, short.MaxValue);

    /// <summary>取最新一帧手柄状态；尚无数据返回 false。</summary>
    public static bool Read(out XInputNative.XInputGamepad g)
    {
        g = default;
        if (!_opened) return false;
        lock (_lock)
        {
            if (!_hasData) return false;
            g = _latest;
            return true;
        }
    }

    /// <summary>状态显示名（取自产品名，如 "Xbox Wireless Controller"）。</summary>
    public static string KindName() => _name;
}
