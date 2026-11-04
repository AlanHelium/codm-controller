using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace CODMControllerProbe;

public sealed record DriverDeviceStatus(bool Present,bool Started,uint Problem,string InstanceId);

// Driver files and service keys may survive an uninstall. Only a present,
// started PnP device can supply the bus/control interface used by the app.
public static class DriverDevices
{
    public static DriverDeviceStatus Inspect(string service)
        => Enumerate(service,false).OrderByDescending(d=>d.Started).FirstOrDefault()??new(false,false,0,"");

    public static List<DriverDeviceStatus> Enumerate(string service,bool includePhantoms=true)
    {
        string hardware=service switch{"ViGEmBus"=>@"Nefarius\ViGEmBus\Gen1","HidHide"=>@"root\HidHide",_=>throw new ArgumentException("Unsupported component")};
        IntPtr list=SetupDiGetClassDevs(IntPtr.Zero,null,IntPtr.Zero,includePhantoms?0x4u:0x2u|0x4u);
        if(list==new IntPtr(-1))throw new Win32Exception(Marshal.GetLastWin32Error());
        var result=new List<DriverDeviceStatus>();
        try
        {
            for(uint index=0;;index++)
            {
                var info=new DeviceInfo{Size=(uint)Marshal.SizeOf<DeviceInfo>()};
                if(!SetupDiEnumDeviceInfo(list,index,ref info))
                {int error=Marshal.GetLastWin32Error();if(error==259)break;throw new Win32Exception(error);}
                var bytes=new byte[1024];
                string bound=SetupDiGetDeviceRegistryProperty(list,ref info,4,out _,bytes,(uint)bytes.Length,out _)?Encoding.Unicode.GetString(bytes).TrimEnd('\0'):"";
                if(!bound.Equals(service,StringComparison.OrdinalIgnoreCase))
                {
                    // After an uninstall, an unbound root node can retain only
                    // its exact hardware ID. Never touch another bound service.
                    if(bound.Length>0||!SetupDiGetDeviceRegistryProperty(list,ref info,1,out _,bytes,(uint)bytes.Length,out _))continue;
                    if(!Encoding.Unicode.GetString(bytes).Split('\0',StringSplitOptions.RemoveEmptyEntries).Contains(hardware,StringComparer.OrdinalIgnoreCase))continue;
                }
                var id=new StringBuilder(1024);SetupDiGetDeviceInstanceId(list,ref info,id,1024,out _);
                if(!id.ToString().StartsWith(@"ROOT\",StringComparison.OrdinalIgnoreCase))continue;
                uint code=CM_Get_DevNode_Status(out uint flags,out uint problem,info.Node,0);
                result.Add(new(code==0,code==0&&problem==0&&(flags&0x8)!=0,code==0?problem:code,id.ToString()));
            }
            return result;
        }
        finally{SetupDiDestroyDeviceInfoList(list);}
    }
    [StructLayout(LayoutKind.Sequential)]struct DeviceInfo{public uint Size;public Guid Class;public uint Node;public IntPtr Reserved;}
    [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr SetupDiGetClassDevs(IntPtr cls,string? enumerator,IntPtr parent,uint flags);
    [DllImport("setupapi.dll",SetLastError=true)]static extern bool SetupDiEnumDeviceInfo(IntPtr list,uint index,ref DeviceInfo info);
    [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool SetupDiGetDeviceRegistryProperty(IntPtr list,ref DeviceInfo info,uint property,out uint type,byte[] buffer,uint size,out uint needed);
    [DllImport("setupapi.dll",CharSet=CharSet.Unicode)]static extern bool SetupDiGetDeviceInstanceId(IntPtr list,ref DeviceInfo info,StringBuilder text,uint size,out uint needed);
    [DllImport("setupapi.dll")]static extern bool SetupDiDestroyDeviceInfoList(IntPtr list);
    [DllImport("cfgmgr32.dll")]static extern uint CM_Get_DevNode_Status(out uint status,out uint problem,uint node,uint flags);
}
