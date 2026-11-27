using System.Runtime.InteropServices;
using System.Text;

namespace CODMControllerProbe;

public partial class MainForm
{
    private GameKeyboardRouter _gameKeys=null!;
    private ForegroundKeyHold _wasdShiftHold=null!;
    private readonly object _keyboardBindingGate=new();
    private PadKeyBind[] _keyboardBindings=Array.Empty<PadKeyBind>();
    private bool _wasdShiftDesired;
    private float _wasdMagnitudePercent;
    private int _inputChannel;
    private string WasdLiveStatus()=>!_running?Lang.T("启动转换后显示实际推幅与冲刺状态","Start conversion to see stick travel and sprint state"):Lang.T($"当前推幅 {_wasdMagnitudePercent:0}% · 外圈冲刺 {(_wasdShiftDown?"已触发":"未触发")}",
        $"Stick {_wasdMagnitudePercent:0}% · sprint {(_wasdShiftDown?"active":"inactive")}");
    private GameKeyboardRouter CreateGameKeyRouter()=>new(
        ()=>ReadGameKeyTarget(FindGameWindow(_gameProcId)),
        (target,key,down)=>KeyInjection.Send(target,key,down),
        IsGameKeyTargetAlive,TestLog.Add,repeat:(target,key)=>KeyInjection.Send(target,key,true,repeat:true));
    /// <summary>切换键盘注入方式（0~4）。切换时释放已按住的键，避免游戏里残留按住状态。</summary>
    private void SetInputChannel(int channel)
    {
        if(!InputChannel.Valid(channel))return;
        _inputChannel=channel;
        KeyInjection.Channel=channel;
        _gameKeys?.Release();
        TestLog.Add($"输入方式切换：方式 {channel+1} · {InputChannel.Name(channel)}");
        SaveSettingsNow();
    }
    private ForegroundKeyHold CreateWasdShiftHold()=>new(IsGameKeyTargetForeground,
        (target,down)=>(!down||IsGameKeyTargetForeground(target))&&SendRealKey(KEY_LSHIFT_SCAN,down),TestLog.Add);
    private static bool IsGameKeyTargetForeground(GameKeyTarget target)
    {
        if(!IsGameKeyTargetAlive(target))return false;
        IntPtr foreground=GetForegroundWindow();if(foreground==IntPtr.Zero)return false;
        GetWindowThreadProcessId(foreground,out uint pid);
        return pid==target.ProcessId;
    }
    private static GameKeyTarget ReadGameKeyTarget(IntPtr handle)
    {
        if(handle==IntPtr.Zero)return default;
        GetWindowThreadProcessId(handle,out uint pid);var title=new StringBuilder(256);GetWindowText(handle,title,256);
        return new(handle,pid,title.ToString());
    }
    private static bool IsGameKeyTargetAlive(GameKeyTarget target)
    {
        if(target.Handle==IntPtr.Zero||!RepairIsWindow(target.Handle))return false;
        GetWindowThreadProcessId(target.Handle,out uint pid);
        return pid==target.ProcessId&&pid!=(uint)Environment.ProcessId;
    }
    [DllImport("user32.dll",EntryPoint="IsWindow")] private static extern bool RepairIsWindow(IntPtr handle);

    private void PublishKeyboardBindings()
    {
        _gameKeys.Release("pad:");
        lock(_keyboardBindingGate){Volatile.Write(ref _keyboardBindings,_padKeyMap.GroupBy(b=>b.PadBit).Select(g=>g.Last()).ToArray());}
    }
    private void ReplaceKeyboardBindings(IEnumerable<PadKeyBind> bindings)
    {
        var next=bindings.Select(PadKeyBind.Resolve).Where(b=>b!=null).Select(b=>b!).GroupBy(b=>b.PadBit).Select(g=>g.Last()).ToArray();
        lock(_keyboardBindingGate)
        {
            var changed=next.Where(b=>!_padKeyMap.Any(old=>old.PadBit==b.PadBit&&old.KeyName==b.KeyName)).Select(b=>b.PadBit).ToArray();
            // Publish an empty snapshot first, preventing the input loop from rearming old owners.
            Volatile.Write(ref _keyboardBindings,Array.Empty<PadKeyBind>());
            PauseOutput(); // Release old hold actions before the newly bound key can press the same keyboard key.
            _gameKeys.Release("pad:");ClearSourceOverrides(changed);_padKeyMap.Clear();_padKeyMap.AddRange(next);
            ResetMappingTransient();PublishMappingSnapshots();
        }
    }
    private XInputNative.XInputGamepad WithoutKeyboardBoundInputs(XInputNative.XInputGamepad input)
        =>KeyboardPadBindings.RemoveSources(input,Volatile.Read(ref _keyboardBindings));
}
