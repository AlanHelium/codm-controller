using System.Runtime.InteropServices;

namespace CODMControllerProbe;

/// <summary>
/// winmm 多媒体 joystick API —— 枚举 DirectInput 通道上的手柄（部分杂牌/第三方 360 协议手柄只走这里）。
/// </summary>
public static class JoyNative
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct JOYCAPS
    {
        public ushort wMid;
        public ushort wPid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
        public uint wXmin;
        public uint wXmax;
        public uint wYmin;
        public uint wYmax;
        public uint wZmin;
        public uint wZmax;
        public uint wNumButtons;
        public uint wPeriodMin;
        public uint wPeriodMax;
        public uint wRmin;
        public uint wRmax;
        public uint wUmin;
        public uint wUmax;
        public uint wVmin;
        public uint wVmax;
        public uint wCaps;
        public uint wMaxAxes;
        public uint wNumAxes;
        public uint wMaxButtons;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szRegKey;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szOEMVxD;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct JOYINFOEX
    {
        public uint dwSize;
        public uint dwFlags;
        public uint dwXpos;
        public uint dwYpos;
        public uint dwZpos;
        public uint dwRpos;
        public uint dwUpos;
        public uint dwVpos;
        public uint dwButtons;
        public uint dwButtonNumber;
        public uint dwPOV;
        public uint dwReserved1;
        public uint dwReserved2;
    }

    private const uint JOY_RETURNALL = 0x000000FF;

    [DllImport("winmm.dll")]
    public static extern uint joyGetNumDevs();

    [DllImport("winmm.dll", CharSet = CharSet.Ansi)]
    public static extern uint joyGetDevCaps(uint uJoyID, out JOYCAPS pCaps, uint cbCaps);

    [DllImport("winmm.dll")]
    public static extern uint joyGetPosEx(uint uJoyID, ref JOYINFOEX pInfo);

    /// <summary>厂商 ID 转可读名称（常见的几个）。</summary>
    public static string VendorName(ushort vid) => vid switch
    {
        0x045E => "Microsoft",
        0x046D => "Logitech",
        0x0738 => "Mad Catz",
        0x054C => "Sony",
        0x2F24 or 0x1002 => "第三方",
        _ => $"VID_{vid:X4}",
    };
}
