using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace CODMControllerProbe;

/// <summary>一个手柄/游戏控制器设备的摘要信息。</summary>
public class HidGameDevice
{
    public string Name = "";
    public string Vid = "";
    public string Pid = "";
    public string HardwareId = "";
    public string InstanceId = "";
    public bool IsXboxLike;
    public string DevClass = ""; // HID / XNA（XInput 复合设备类）
    public bool IsVirtualGamepad; // ViGEmBus 之类虚拟设备（需排除，不能隐藏）
}

/// <summary>
/// 通过 SetupAPI 枚举游戏控制器设备（能看到 VID/PID 和系统名称）。
/// 覆盖两类：HIDClass（HID-compliant game controller）与 XNACOMPOSITE（XInput 复合设备，如 Xbox 360 Controller）。
/// 第三方 360 协议手柄通常同时/至少出现在其中之一，必须两类都枚举才能被 HidHide 完整隐藏。
/// </summary>
public static class HidEnum
{
    private static readonly Guid GuidHidClass = new("745a17a0-74d3-11d0-b6fe-00a0c90f57da");
    private static readonly Guid GuidXnaComposite = new("d61ca365-5af4-4486-998b-9db4734c6ca3");
    private static readonly Guid GuidDevInterfaceHid = new("{4d1e55b2-f16f-11cf-88cb-001111000030}");

    [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, string? enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr list);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr list, uint index, out SP_DEVINFO_DATA data);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr list, IntPtr devInfoData, ref Guid classGuid, uint index, ref SP_DEVICE_INTERFACE_DATA data);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr list, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, uint len, out uint need, ref SP_DEVINFO_DATA devInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool SetupDiGetDeviceRegistryProperty(IntPtr list, ref SP_DEVINFO_DATA data, uint prop,
        out uint regType, IntPtr buffer, uint bufferSize, out uint requiredSize);

    [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInstanceId(IntPtr list, ref SP_DEVINFO_DATA data,
        System.Text.StringBuilder instanceId, uint instanceIdSize, out uint requiredSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA
    {
        public uint cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SP_DEVICE_INTERFACE_DETAIL_DATA
    {
        public uint cbSize;
        public char DevicePath;
    }

    private const uint DIGCF_PRESENT = 0x2;
    private const uint DIGCF_DEVICEINTERFACE = 0x10;
    private const uint SPDRP_HARDWAREID = 0x1;
    private const uint SPDRP_DEVICEDESC = 0x0;
    private const uint SPDRP_FRIENDLYNAME = 0xC;

    /// <summary>按在场设备的 HID Usage、ContainerId 和父设备链建立实体隐藏名单。
    /// 不依赖 IG_ 字符串或相同 VID/PID；排除虚拟设备、键盘和鼠标。</summary>
    public static List<string> GetGamepadInstanceIds()
        => DeviceCatalog.HideCandidates(DeviceCatalog.Enumerate());

    /// <summary>枚举全部 HID 设备接口路径 + 每个接口对应的设备级实例 ID（用于区分实体/虚拟：ROOT\ = 虚拟）。
    /// 公开版本：Sony/Xbox HID 直读通道用它排除 ViGEm 虚拟手柄，避免把虚拟 DS4 当成实体手柄打开。</summary>
    public static List<(string Path, string DevInstance)> EnumerateHidInterfacePathsWithInstancePublic()
        => EnumerateHidInterfacePathsWithInstance();

    /// <summary>枚举全部 HID 设备接口路径 + 每个接口对应的设备级实例 ID（用于区分实体/虚拟：ROOT\ = 虚拟）。</summary>
    private static List<(string Path, string DevInstance)> EnumerateHidInterfacePathsWithInstance()
    {
        var result = new List<(string, string)>();
        var guid = GuidDevInterfaceHid;
        // 加 DIGCF_PRESENT：只枚举当前在场设备。否则会返回所有历史连接过的手柄接口（实测 43 个），
        // 黑名单 payload 膨胀，可能导致 SetBlacklist 失败 → 实体手柄完全没隐藏 → 双输入/转向。
        var list = SetupDiGetClassDevs(ref guid, null, IntPtr.Zero, DIGCF_DEVICEINTERFACE | DIGCF_PRESENT);
        if (list == new IntPtr(-1)) return result;

        try
        {
            var data = new SP_DEVICE_INTERFACE_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
            for (uint i = 0; SetupDiEnumDeviceInterfaces(list, IntPtr.Zero, ref guid, i, ref data); i++)
            {
                data.cbSize = (uint)Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>();
                var probeInfo = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>() };
                SetupDiGetDeviceInterfaceDetail(list, ref data, IntPtr.Zero, 0, out uint need, ref probeInfo);
                if (need == 0) continue;
                var detail = Marshal.AllocHGlobal((int)need);
                try
                {
                    // 传入 devInfoData → 反查该接口所属设备的实例 ID（虚拟设备 = ROOT\VIREMU\...）
                    var devInfo = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>() };
                    Marshal.WriteInt32(detail, Marshal.SizeOf<SP_DEVICE_INTERFACE_DETAIL_DATA>());
                    if (SetupDiGetDeviceInterfaceDetail(list, ref data, detail, need, out _, ref devInfo))
                    {
                        int off = Marshal.OffsetOf<SP_DEVICE_INTERFACE_DETAIL_DATA>("DevicePath").ToInt32();
                        var path = Marshal.PtrToStringUni(detail + off);
                        if (!string.IsNullOrEmpty(path))
                        {
                            string devInst = "";
                            try
                            {
                                SetupDiGetDeviceInstanceId(list, ref devInfo, null!, 0, out uint size);
                                if (size > 0)
                                {
                                    var sb = new System.Text.StringBuilder((int)size);
                                    SetupDiGetDeviceInstanceId(list, ref devInfo, sb, size, out _);
                                    devInst = sb.ToString();
                                }
                            }
                            catch { /* 忽略单设备失败 */ }
                            result.Add((path, devInst));
                        }
                    }
                }
                finally { Marshal.FreeHGlobal(detail); }
                data = new SP_DEVICE_INTERFACE_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
            }
        }
        finally { SetupDiDestroyDeviceInfoList(list); }
        return result;
    }

    private static List<string> EnumerateHidInterfacePaths()
    {
        return EnumerateHidInterfacePathsPublic();
    }

    /// <summary>公开入口：枚举全部 HID 设备接口路径（SonyHidPad 读取通道复用）。</summary>
    public static List<string> EnumerateHidInterfacePathsPublic()
    {
        var result = new List<string>();
        var guid = GuidDevInterfaceHid;
        var list = SetupDiGetClassDevs(ref guid, null, IntPtr.Zero, DIGCF_DEVICEINTERFACE | DIGCF_PRESENT);
        if (list == new IntPtr(-1)) return result;

        try
        {
            var data = new SP_DEVICE_INTERFACE_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
            for (uint i = 0; SetupDiEnumDeviceInterfaces(list, IntPtr.Zero, ref guid, i, ref data); i++)
            {
                data.cbSize = (uint)Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>();
                var devInfo = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>() };
                SetupDiGetDeviceInterfaceDetail(list, ref data, IntPtr.Zero, 0, out uint need, ref devInfo);
                if (need == 0) continue;
                var detail = Marshal.AllocHGlobal((int)need);
                try
                {
                    Marshal.WriteInt32(detail, Marshal.SizeOf<SP_DEVICE_INTERFACE_DETAIL_DATA>());
                    if (SetupDiGetDeviceInterfaceDetail(list, ref data, detail, need, out _, ref devInfo))
                    {
                        int off = Marshal.OffsetOf<SP_DEVICE_INTERFACE_DETAIL_DATA>("DevicePath").ToInt32();
                        var path = Marshal.PtrToStringUni(detail + off);
                        if (!string.IsNullOrEmpty(path)) result.Add(path);
                    }
                }
                finally { Marshal.FreeHGlobal(detail); }
                data = new SP_DEVICE_INTERFACE_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
            }
        }
        finally { SetupDiDestroyDeviceInfoList(list); }
        return result;
    }

    public static List<HidGameDevice> Enumerate()
    {
        var result = new List<HidGameDevice>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (cls, label) in new[] { (GuidHidClass, "HID"), (GuidXnaComposite, "XNA") })
        {
            foreach (var dev in EnumerateClass(cls, label))
            {
                if (!string.IsNullOrEmpty(dev.InstanceId) && !seen.Add(dev.InstanceId))
                    continue;
                result.Add(dev);
            }
        }

        return result;
    }

    private static List<HidGameDevice> EnumerateClass(Guid cls, string label)
    {
        var result = new List<HidGameDevice>();
        var list = SetupDiGetClassDevs(ref cls, null, IntPtr.Zero, DIGCF_PRESENT);
        if (list == new IntPtr(-1)) return result;

        try
        {
            var data = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>() };
            for (uint i = 0; SetupDiEnumDeviceInfo(list, i, out data); i++)
            {
                string hwId = GetString(list, ref data, SPDRP_HARDWAREID);
                if (cls == GuidHidClass && !hwId.Contains("IG_", StringComparison.OrdinalIgnoreCase))
                    continue; // HID 类只保留游戏控制器（joystick/gamepad usage）

                string name = GetString(list, ref data, SPDRP_FRIENDLYNAME);
                if (string.IsNullOrEmpty(name))
                    name = GetString(list, ref data, SPDRP_DEVICEDESC);
                if (string.IsNullOrEmpty(name))
                    name = hwId;

                var dev = new HidGameDevice { Name = name, HardwareId = hwId, DevClass = label };
                dev.InstanceId = GetInstanceId(list, ref data);
                var m = Regex.Match(hwId, @"VID_([0-9A-Fa-f]{4}).*PID_([0-9A-Fa-f]{4})",
                    RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    dev.Vid = m.Groups[1].Value.ToUpperInvariant();
                    dev.Pid = m.Groups[2].Value.ToUpperInvariant();
                }

                dev.IsXboxLike =
                    (dev.Vid == "045E") || // Microsoft
                    name.Contains("Xbox", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("360", StringComparison.OrdinalIgnoreCase);

                // ViGEmBus 等虚拟设备（"Virtual ..." 或 VIREMU 根枚举）绝不能进隐藏名单
                dev.IsVirtualGamepad =
                    name.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("虚拟", StringComparison.OrdinalIgnoreCase) ||
                    hwId.Contains("VIREMU", StringComparison.OrdinalIgnoreCase) ||
                    hwId.StartsWith("ROOT\\", StringComparison.OrdinalIgnoreCase);

                result.Add(dev);
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(list);
        }

        return result;
    }

    private static string GetInstanceId(IntPtr list, ref SP_DEVINFO_DATA data)
    {
        // 第一次调用传空 buffer 是探测：返回 false(122 缓冲区不够) 属预期，size 才是所需字节数
        SetupDiGetDeviceInstanceId(list, ref data, null!, 0, out uint size);
        if (size == 0) return "";
        var sb = new System.Text.StringBuilder((int)size);
        SetupDiGetDeviceInstanceId(list, ref data, sb, size, out _);
        return sb.ToString();
    }

    private static string GetString(IntPtr list, ref SP_DEVINFO_DATA data, uint prop)
    {
        // 探测调用：忽略返回值（122 缓冲区不够是预期），size 为所需字节数
        SetupDiGetDeviceRegistryProperty(list, ref data, prop, out _, IntPtr.Zero, 0, out uint size);
        if (size == 0) return "";

        IntPtr buf = Marshal.AllocHGlobal((int)size);
        try
        {
            if (SetupDiGetDeviceRegistryProperty(list, ref data, prop, out _, buf, size, out _))
                return Marshal.PtrToStringUni(buf) ?? "";
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }

        return "";
    }
}
