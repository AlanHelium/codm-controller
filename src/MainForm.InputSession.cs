namespace CODMControllerProbe;

public partial class MainForm
{
    private readonly object _inputSessionGate=new();
    private readonly HashSet<string> _isolatedPhysicalIds=new(StringComparer.OrdinalIgnoreCase);
    private volatile bool _physicalSessionReady;
    private volatile bool _physicalSessionPreparing;
    private volatile bool _physicalXInputCandidate;
    private long _nextSourceProbe;
    private bool _startingConversion,_closingInputSession,_watchdogStarted,_isolationApplied;
    private string _conversionError="";
    private long _nextIsolationRetry;
    private bool PreparePhysicalInputSession(ControllerDevice[]? discovered=null)
    {
        lock(_inputSessionGate)
        {
            _physicalSessionReady=false;
            bool changed=false;
            try
            {
                if(_closingInputSession)return false;
                var games=BlockingGameProcesses();
                if(games.Count>0)throw new IOException(Lang.T("请先退出游戏及模拟器，再启动转换：","Close the game and emulator before conversion: ")+string.Join(", ",games.Select(p=>$"{p.Name} (PID {p.Id})")));
                var components=ComponentProbe.Capture(AppContext.BaseDirectory);
                _componentReadyObserved=components.Ready;_componentWaiting=!components.Ready;
                if(!components.Ready)throw new IOException(components.Message);
                if(!_isolationApplied&&!HidHideManager.RecoverPreviousSession(out _,out string recoveryError))throw new IOException(recoveryError);
                var devices=discovered??DeviceCatalog.Enumerate().ToArray();
                _physicalXInputCandidate=ControllerVisuals.HasPhysicalXInputCandidate(devices);
                var candidates=DeviceCatalog.HideCandidates(devices);
                if(candidates.Count>0)
                {
                    if(HidHideManager.HideDevices(candidates)==0)throw new IOException(HidHideManager.LastError);
                    changed=true;_isolationApplied=true;
                    var check=IsolationProbe.Verify(candidates,requireVirtual:false);
                    if(!check.Verified)throw new IOException(check.Message);
                    _isolatedPhysicalIds.UnionWith(candidates);
                    if(!_watchdogStarted){IsolationProbe.StartWatchdog();_watchdogStarted=true;}
                }
                if(_closingInputSession){HidHideManager.Restore();return false;}
                _conversionError="";_physicalSessionReady=true;
                TestLog.Add("启动检查完成：物理设备已隔离或等待接入；输入循环不执行驱动配置调用");
                return true;
            }
            catch(Exception ex)
            {
                if(changed){HidHideManager.Restore();_isolatedPhysicalIds.Clear();_isolationApplied=false;}_conversionError=ex.Message;
                TestLog.Add("转换准备失败："+ex.Message);return false;
            }
        }
    }
    private void CheckNewPhysicalDevices()
    {
        if(!_running||_physicalSessionPreparing||_closingInputSession||Environment.TickCount64<_nextIsolationRetry)return;
        var candidates=DeviceCatalog.HideCandidates(_controllerDiscovery.Devices);
        _physicalXInputCandidate=ControllerVisuals.HasPhysicalXInputCandidate(_controllerDiscovery.Devices);
        lock(_inputSessionGate)if(candidates.All(id=>_isolatedPhysicalIds.Contains(id)))return;
        _physicalSessionPreparing=true;_physicalSessionReady=false;
        _=Task.Run(()=>
        {
            // Game may have cached the new raw device before isolation: require a clean restart.
            bool ready=PreparePhysicalInputSession(_controllerDiscovery.Devices);
            _nextIsolationRetry=Environment.TickCount64+(ready?0:5000);
            _physicalSessionPreparing=false;
        });
    }
    private void RestorePhysicalInputSession()
    {
        _physicalSessionReady=false;
        lock(_inputSessionGate)
        {
            bool restored=HidHideManager.Restore();_isolatedPhysicalIds.Clear();
            if(restored)_isolationApplied=false;
            if(!restored){_conversionError=Lang.T("停止后设备恢复待完成：","Device restoration incomplete: ")+HidHideManager.LastError;TestLog.Add(_conversionError);}
        }
    }
}
