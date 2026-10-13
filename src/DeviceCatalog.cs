using System.Runtime.InteropServices;
using System.Text;

namespace CODMControllerProbe;

public sealed class ControllerDevice
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Service { get; set; } = "";
    public Guid Container { get; set; }
    public string Path { get; set; } = "";
    public List<string> Ancestors { get; set; } = new();
    public bool Virtual { get; set; }
    public ushort UsagePage { get; set; }
    public ushort Usage { get; set; }
    public ushort ReportLength { get; set; }
    public bool Xbox => Service.Equals("xusb22", StringComparison.OrdinalIgnoreCase) || Service.Equals("xusb21", StringComparison.OrdinalIgnoreCase) || Service.Equals("xboxgip", StringComparison.OrdinalIgnoreCase);
    public bool Protected => (UsagePage == 1 && Usage is 1 or 2 or 6 or 7) || Service.Contains("kbd", StringComparison.OrdinalIgnoreCase) || Service.Contains("mou", StringComparison.OrdinalIgnoreCase);
    public bool Gamepad => Xbox || (UsagePage == 1 && Usage is 4 or 5 or 8) || (!Protected && Id.Contains("&IG_", StringComparison.OrdinalIgnoreCase));
}

// 依据在场设备、HID usage、ContainerId 与整条父设备链识别，绝不依据 VID/PID 相同就扩大到所有设备。
public static class DeviceCatalog
{
    private static Guid HidGuid = new("4d1e55b2-f16f-11cf-88cb-001111000030");
    [StructLayout(LayoutKind.Sequential)] private struct Info { public uint Size; public Guid Class; public uint Node; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] private struct Interface { public uint Size; public Guid Class; public uint Flags; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { public Guid Format; public uint Id; }
    [StructLayout(LayoutKind.Sequential)] private struct Caps
    {
        public ushort Usage, Page, Input, Output, Feature;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort Links, InputButtons, InputValues, InputData, OutputButtons, OutputValues, OutputData, FeatureButtons, FeatureValues, FeatureData;
    }
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern IntPtr SetupDiGetClassDevs(IntPtr cls,string? enumerator,IntPtr parent,uint flags);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern IntPtr SetupDiGetClassDevs(ref Guid cls,string? enumerator,IntPtr parent,uint flags);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr list);
    [DllImport("setupapi.dll")] private static extern bool SetupDiEnumDeviceInfo(IntPtr list,uint index,ref Info info);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode)] private static extern bool SetupDiGetDeviceInstanceId(IntPtr list,ref Info info,StringBuilder text,uint count,out uint needed);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode)] private static extern bool SetupDiGetDeviceRegistryProperty(IntPtr list,ref Info info,uint prop,out uint type,byte[] buffer,uint size,out uint needed);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode)] private static extern bool SetupDiGetDeviceProperty(IntPtr list,ref Info info,ref PropertyKey key,out uint type,byte[] buffer,uint size,out uint needed,uint flags);
    [DllImport("setupapi.dll")] private static extern bool SetupDiEnumDeviceInterfaces(IntPtr list,IntPtr dev,ref Guid cls,uint index,ref Interface data);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode)] private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr list,ref Interface data,IntPtr buffer,uint size,out uint needed,ref Info info);
    [DllImport("cfgmgr32.dll")] private static extern uint CM_Get_Parent(out uint parent,uint node,uint flags);
    [DllImport("cfgmgr32.dll", CharSet=CharSet.Unicode)] private static extern uint CM_Get_Device_ID(uint node,StringBuilder text,uint size,uint flags);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern IntPtr CreateFile(string path,uint access,uint share,IntPtr security,uint mode,uint flags,IntPtr template);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("hid.dll")] private static extern bool HidD_GetPreparsedData(IntPtr handle,out IntPtr data);
    [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data,out Caps caps);

    public static bool IsVirtualChain(IEnumerable<string> chain) => chain.Any(id => id.Contains("VIGEM",StringComparison.OrdinalIgnoreCase) || id.Contains("VIREMU",StringComparison.OrdinalIgnoreCase) || id.Contains("VJOY",StringComparison.OrdinalIgnoreCase) || id.Contains("VHID",StringComparison.OrdinalIgnoreCase));

    public static List<ControllerDevice> Enumerate()
    {
        var nodes = new Dictionary<uint,ControllerDevice>();
        var list=SetupDiGetClassDevs(IntPtr.Zero,null,IntPtr.Zero,0x2|0x4);
        if(list==new IntPtr(-1)) return new();
        try
        {
            for(uint i=0;;i++)
            {
                var info=new Info{Size=(uint)Marshal.SizeOf<Info>()};
                if(!SetupDiEnumDeviceInfo(list,i,ref info)) break;
                var id=new StringBuilder(1024); if(!SetupDiGetDeviceInstanceId(list,ref info,id,1024,out _)) continue;
                var device=new ControllerDevice{Id=id.ToString(),Name=Property(list,ref info,12),Service=Property(list,ref info,4)};
                if(device.Name.Length==0) device.Name=Property(list,ref info,0);
                var key=new PropertyKey{Format=new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"),Id=2}; var bytes=new byte[16];
                if(SetupDiGetDeviceProperty(list,ref info,ref key,out _,bytes,16,out _,0)) device.Container=new Guid(bytes);
                uint current=info.Node;
                for(int depth=0;depth<16;depth++)
                {
                    var text=new StringBuilder(1024); if(CM_Get_Device_ID(current,text,1024,0)==0) device.Ancestors.Add(text.ToString());
                    if(CM_Get_Parent(out uint parent,current,0)!=0 || parent==current) break; current=parent;
                }
                nodes[info.Node]=device;
            }
        }
        finally { SetupDiDestroyDeviceInfoList(list); }
        foreach(var d in nodes.Values) d.Virtual=IsVirtualChain(d.Ancestors) || d.Ancestors.Any(id=>nodes.Values.Any(n=>n.Id==id && n.Service.Contains("ViGEm",StringComparison.OrdinalIgnoreCase)));
        list=SetupDiGetClassDevs(ref HidGuid,null,IntPtr.Zero,0x2|0x10);
        if(list!=new IntPtr(-1))
        {
            try
            {
                for(uint i=0;;i++)
                {
                    var data=new Interface{Size=(uint)Marshal.SizeOf<Interface>()}; if(!SetupDiEnumDeviceInterfaces(list,IntPtr.Zero,ref HidGuid,i,ref data)) break;
                    var info=new Info{Size=(uint)Marshal.SizeOf<Info>()}; SetupDiGetDeviceInterfaceDetail(list,ref data,IntPtr.Zero,0,out uint size,ref info); if(size==0) continue;
                    var detail=Marshal.AllocHGlobal((int)size);
                    try
                    {
                        Marshal.WriteInt32(detail,IntPtr.Size==8?8:6);
                        if(!SetupDiGetDeviceInterfaceDetail(list,ref data,detail,size,out _,ref info) || !nodes.TryGetValue(info.Node,out var d)) continue;
                        d.Path=Marshal.PtrToStringUni(detail+4) ?? "";
                        var handle=CreateFile(d.Path,0,7,IntPtr.Zero,3,0,IntPtr.Zero);
                        if(handle!=new IntPtr(-1))
                        {
                            try { if(HidD_GetPreparsedData(handle,out var pp)) { try { if(HidP_GetCaps(pp,out var caps)>=0) { d.Usage=caps.Usage; d.UsagePage=caps.Page; d.ReportLength=caps.Input; } } finally{HidD_FreePreparsedData(pp);} } }
                            finally {CloseHandle(handle);}
                        }
                    }
                    finally {Marshal.FreeHGlobal(detail);}
                }
            }
            finally {SetupDiDestroyDeviceInfoList(list);}
        }
        return nodes.Values.Where(d=>d.Gamepad || d.Path.Length>0).ToList();
    }

    public static List<string> HideCandidates(IEnumerable<ControllerDevice> devices)
    {
        var all=devices.ToList(); var containers=all.Where(d=>d.Gamepad&&!d.Virtual&&!d.Protected&&d.Container!=Guid.Empty).Select(d=>d.Container).ToHashSet();
        return all.Where(d=>!d.Virtual&&!d.Protected&&(d.Gamepad || (d.Container!=Guid.Empty&&containers.Contains(d.Container)&&d.Path.Length>0&&d.UsagePage>0))).Select(d=>d.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
    public static int TryOpen(string path)
    {
        var h=CreateFile(path,0x80000000,7,IntPtr.Zero,3,0,IntPtr.Zero);
        if(h==new IntPtr(-1)) return Marshal.GetLastWin32Error(); CloseHandle(h); return 0;
    }
    private static string Property(IntPtr list,ref Info info,uint key)
    { var bytes=new byte[4096]; return SetupDiGetDeviceRegistryProperty(list,ref info,key,out _,bytes,4096,out _)?Encoding.Unicode.GetString(bytes).Split('\0')[0]:""; }
}
