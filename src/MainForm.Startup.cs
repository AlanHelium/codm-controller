namespace CODMControllerProbe;

public partial class MainForm
{
    private volatile bool _componentWaiting,_componentReadyObserved;
    private Task<StartupDiagnosticData> CaptureStartupDiagnosticsAsync()=>Task.Run(()=>
    {
        var components=ComponentProbe.Capture(AppContext.BaseDirectory);
        _componentReadyObserved=components.Ready;
        var visual=ControllerVisuals.Select(DeviceCatalog.Enumerate(),SonyHidPad.IsOpen?SonyHidPad.DevicePath:XboxHidPad.IsOpen?XboxHidPad.DevicePath:"");
        return new StartupDiagnosticData(components,visual.Connected,visual.Title,_running,_startingConversion,_sourceSlot>=0,_ds4!=null,_physicalSessionReady,_componentWaiting&&components.Ready?"":_conversionError,_running&&_rumbleByShot,_shotRumble.Running);
    });
    private static List<(string Name,int Id)> BlockingGameProcesses()
    {
        var matches=new List<(string,int)>();
        try
        {
            foreach(var p in System.Diagnostics.Process.GetProcesses())using(p)
            {
                try{if(p.Id!=Environment.ProcessId&&StartupProcessPolicy.BlocksConversion(p.ProcessName,_gameProcKeywords))matches.Add((p.ProcessName,p.Id));}
                catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}
            }
        }
        catch{}
        return matches;
    }
}
