using System.Text.RegularExpressions;

namespace CODMControllerProbe;

public sealed record ControllerVisual(string Asset,string Title,bool Connected,string Path="");

public static class ControllerVisuals
{
    public static ControllerVisual Disconnected=>new("xbox-generic",Lang.T("未连接手柄","No controller connected"),false);
    public static ControllerVisual? Identify(ControllerDevice device)
    {
        if(device.Virtual||device.Protected)return null;
        string ids=string.Join(" ",device.Ancestors.Prepend(device.Id));
        string name=device.Name;
        bool Sony(string pid)=>ids.Contains("VID_054C",StringComparison.OrdinalIgnoreCase)&&ids.Contains("PID_"+pid,StringComparison.OrdinalIgnoreCase);
        if(Sony("0DF2"))return new("dse","DualSense Edge",true,device.Path);
        if(Sony("0CE6")||Sony("0E5A"))return new("ds5","DualSense",true,device.Path);
        if(Sony("05C4")||Sony("09CC"))return new("ds4","DualShock 4",true,device.Path);
        if(!device.Gamepad)return null;
        // Only known physical product IDs select a generation; protocol alone is not a model.
        bool Microsoft(string pid)=>ids.Contains("VID_045E",StringComparison.OrdinalIgnoreCase)&&ids.Contains("PID_"+pid,StringComparison.OrdinalIgnoreCase);
        if(Microsoft("0291")||Microsoft("02A9")||Microsoft("0719"))return null; // A receiver alone is not a connected controller.
        if(Microsoft("0B12"))return new("xbox-series","Xbox Series X / S",true,device.Path);
        if(Microsoft("02D1")||Microsoft("02DD")||Microsoft("02EA"))return new("xbox-one","Xbox One",true,device.Path);
        if(Microsoft("028E")||Microsoft("028F"))return new("xbox-360","Xbox 360",true,device.Path);
        // Elite and receiver-only devices are deliberately not pictured as an ordinary model.
        return new("xbox-generic",Lang.T("通用手柄 · XInput / HID","Generic controller · XInput / HID"),true,device.Path);
    }
    public static bool HasPhysicalXInputCandidate(IEnumerable<ControllerDevice> devices)=>devices.Any(d=>!d.Virtual&&!d.Protected&&d.Gamepad&&
        !string.Join(" ",d.Ancestors.Prepend(d.Id)).Contains("VID_054C",StringComparison.OrdinalIgnoreCase));
    public static ControllerVisual Select(IEnumerable<ControllerDevice> devices,string activePath="",bool xinput=false)
    {
        var physical=devices.Where(d=>!d.Virtual&&!d.Protected).ToArray();
        if(activePath.Length>0)
        {
            var selected=physical.FirstOrDefault(d=>d.Path.Equals(activePath,StringComparison.OrdinalIgnoreCase));
            if(selected!=null&&Identify(selected) is {} exact)return exact;
        }
        var distinct=physical.Select(d=>(Device:d,Visual:Identify(d))).Where(x=>x.Visual!=null)
            .GroupBy(x=>x.Device.Container!=Guid.Empty?x.Device.Container.ToString():x.Device.Id,StringComparer.OrdinalIgnoreCase)
            .Select(g=>g.First().Visual!).ToArray();
        if(distinct.Length==0)return Disconnected;
        if(xinput)
        {
            var xbox=distinct.Where(v=>v.Asset.StartsWith("xbox",StringComparison.Ordinal)).ToArray();
            if(xbox.Length==1)return xbox[0];
            return new("xbox-generic",Lang.T("手柄已连接 · 型号待确认","Controller connected · model unconfirmed"),true);
        }
        return distinct.Length==1?distinct[0]:new("xbox-generic",Lang.T($"已连接 {distinct.Length} 个手柄",$"{distinct.Length} controllers connected"),true);
    }
}

// Discovery opens capability metadata only. It never reads input reports or touches drivers.
internal sealed class ControllerDiscovery : IDisposable
{
    private ControllerDevice[] _devices=Array.Empty<ControllerDevice>();
    private int _busy,_disposed;
    public ControllerDevice[] Devices=>Volatile.Read(ref _devices);
    public void Refresh()
    {
        if(Volatile.Read(ref _disposed)!=0||Interlocked.Exchange(ref _busy,1)!=0)return;
        _=Task.Run(()=>{try{var next=DeviceCatalog.Enumerate().ToArray();if(Volatile.Read(ref _disposed)==0)Volatile.Write(ref _devices,next);}catch{/* Preserve the last snapshot on a transient enumeration error. */}finally{Volatile.Write(ref _busy,0);}});
    }
    public void Dispose()=>Interlocked.Exchange(ref _disposed,1);
}
