namespace CODMControllerProbe;

public readonly record struct GameKey(ushort Vk, ushort Scan, bool Extended=false)
{
    public GameKey Canonical => new(KeyMessage.VirtualKey(Vk),Scan,Extended);
}
public readonly record struct GameKeyTarget(IntPtr Handle,uint ProcessId,string Title);

// WM_* messages use generic modifier VKs; scan code + extended bit select the side.
public static class KeyMessage
{
    public static ushort VirtualKey(ushort vk)=>vk switch {0xA0 or 0xA1=>0x10,0xA2 or 0xA3=>0x11,0xA4 or 0xA5=>0x12,_=>vk};
    public static uint Flags(GameKey key,bool down,bool repeat=false)=>1u|((uint)(key.Scan&0xff)<<16)|(key.Extended?0x01000000u:0u)|(down?(repeat?0x40000000u:0u):0xC0000000u);
}

// Owns only game-window messages: no SendInput fallback and no global keyboard state.
// Desired state is separate from successfully delivered state. Multiple owners share a key.
public sealed class GameKeyboardRouter
{
    private readonly object _gate=new();
    private readonly Func<GameKeyTarget> _resolve;
    private readonly Func<GameKeyTarget,GameKey,bool,bool> _send;
    private readonly Func<GameKeyTarget,bool> _alive;
    private readonly Func<long> _clock;
    private readonly Action<string> _log;
    private readonly Func<GameKeyTarget,GameKey,bool>? _repeat;
    private readonly HashSet<string> _repeatOwners=new();
    private readonly Dictionary<GameKey,long> _repeatDue=new();
    private readonly Dictionary<string,GameKey> _desired=new();
    private readonly Dictionary<GameKey,GameKeyTarget> _held=new();
    private readonly Dictionary<(GameKey,bool),long> _retry=new();
    private GameKeyTarget _target;
    private long _targetChecked=long.MinValue/2,_pumped=long.MinValue/2,_lastFailure=long.MinValue/2;
    private bool _dirty;
    public GameKeyboardRouter(Func<GameKeyTarget> resolve,Func<GameKeyTarget,GameKey,bool,bool> send,
        Func<GameKeyTarget,bool> alive,Action<string>? log=null,Func<long>? clock=null,Func<GameKeyTarget,GameKey,bool>? repeat=null)
    {_resolve=resolve;_send=send;_alive=alive;_log=log??(_=>{});_clock=clock??(()=>Environment.TickCount64);_repeat=repeat;}
    public GameKeyTarget CurrentTarget {get{lock(_gate)return _target;}}
    public void SetDesired(string owner,GameKey key,bool down,bool repeat=false)
    {
        lock(_gate){key=key.Canonical;if(down){if(!_desired.TryGetValue(owner,out var previous)||previous!=key){_desired[owner]=key;_dirty=true;}}
            else if(_desired.Remove(owner))_dirty=true;
            if(down&&repeat)_repeatOwners.Add(owner);else _repeatOwners.Remove(owner);}
    }
    public bool Delivered(string owner)
    {lock(_gate)return _desired.TryGetValue(owner,out var key)&&_held.TryGetValue(key,out var target)&&target.Handle==_target.Handle&&target.ProcessId==_target.ProcessId;}
    public string MappingStatus()
    {
        lock(_gate)
        {
            var owners=_desired.Where(p=>p.Key.StartsWith("pad:",StringComparison.Ordinal)).ToArray();
            if(owners.Length==0)return Lang.T("等待按下已映射的手柄键","Waiting for a mapped pad button");
            if(_target.Handle==IntPtr.Zero)return Lang.T("未找到游戏窗口，尚未投递","Game window not found; delivery pending");
            int delivered=owners.Count(p=>_held.TryGetValue(p.Value,out var target)&&target.Handle==_target.Handle&&target.ProcessId==_target.ProcessId);
            return delivered<owners.Length?Lang.T("部分按键投递失败，正在重试","Some key deliveries failed; retrying"):
                Lang.T($"已投递到「{_target.Title}」",$"Delivered to “{_target.Title}”");
        }
    }
    public void Release(string prefix="")
    {lock(_gate){foreach(var owner in _desired.Keys.Where(x=>x.StartsWith(prefix,StringComparison.Ordinal)).ToArray()){_desired.Remove(owner);_repeatOwners.Remove(owner);}_dirty=true;PumpCore(true);}}
    public void RefreshRepeatingKeys()
    {lock(_gate){foreach(var owner in _repeatOwners)if(_desired.TryGetValue(owner,out var key)&&_held.ContainsKey(key))_repeatDue[key]=_clock()+16;}}
    public void Pump(){lock(_gate)PumpCore(false);}
    private void PumpCore(bool force)
    {
        if(_desired.Count==0&&_held.Count==0)return;
        long now=_clock();if(!force&&!_dirty&&now-_pumped<8)return;_pumped=now;_dirty=false;
        if(force||now-_targetChecked>=200||(_target.Handle!=IntPtr.Zero&&!_alive(_target))){_target=_resolve();_targetChecked=now;}
        foreach(var pair in _held.ToArray())
        {
            bool sameTarget=pair.Value.Handle==_target.Handle&&pair.Value.ProcessId==_target.ProcessId;
            if(sameTarget&&_desired.Values.Contains(pair.Key))continue;
            if(!_alive(pair.Value)){_held.Remove(pair.Key);_retry.Remove((pair.Key,false));_repeatDue.Remove(pair.Key);continue;}
            if(!force&&_retry.TryGetValue((pair.Key,false),out long retryUp)&&now<retryUp)continue;
            if(_send(pair.Value,pair.Key,false)){_held.Remove(pair.Key);_retry.Remove((pair.Key,false));_repeatDue.Remove(pair.Key);_log($"键盘投递 OFF VK=0x{pair.Key.Vk:X2} →「{pair.Value.Title}」");}
            else{_retry[(pair.Key,false)]=now+125;Failure(now,$"抬起失败 VK=0x{pair.Key.Vk:X2}，保留状态重试");}
        }
        foreach(var key in _desired.Values.Distinct())
        {
            if(_held.ContainsKey(key))continue;
            if(_target.Handle==IntPtr.Zero){Failure(now,"无游戏窗口，映射待投递；不会触发转换器界面");continue;}
            if(!force&&_retry.TryGetValue((key,true),out long retryDown)&&now<retryDown)continue;
            if(_send(_target,key,true)){_held[key]=_target;_retry.Remove((key,true));_repeatDue[key]=now+400;string owners=string.Join(" / ",_desired.Where(p=>p.Value==key).Select(p=>p.Key.StartsWith("pad:")&&int.TryParse(p.Key[4..],out int bit)?KeyMap.BitName(bit):p.Key));_log($"键盘投递 {owners} ON VK=0x{key.Vk:X2} Scan=0x{key.Scan:X2} Ext={key.Extended} →「{_target.Title}」");}
            else{_retry[(key,true)]=now+125;Failure(now,$"按下失败 VK=0x{key.Vk:X2} →「{_target.Title}」，未记为已按下");}
        }
        if(_repeat!=null)foreach(var key in _repeatOwners.Where(_desired.ContainsKey).Select(owner=>_desired[owner]).Distinct())
        {
            if(key.Vk is 0x10 or 0x11 or 0x12 || !_held.TryGetValue(key,out var heldTarget) || heldTarget.Handle!=_target.Handle || heldTarget.ProcessId!=_target.ProcessId)continue;
            if(!_repeatDue.TryGetValue(key,out long due)){_repeatDue[key]=now+400;continue;}
            if(now<due)continue;
            bool sent=_repeat(heldTarget,key);_repeatDue[key]=now+(sent?50:125);
            if(!sent)Failure(now,$"长按重复投递失败 VK=0x{key.Vk:X2}");
        }
    }
    private void Failure(long now,string message){if(now-_lastFailure>=1000){_lastFailure=now;_log(message);}}
}

public static class KeyboardPadBindings
{
    public static XInputNative.XInputGamepad RemoveSources(XInputNative.XInputGamepad input,ReadOnlySpan<PadKeyBind> bindings)
    {
        foreach(var binding in bindings){input.wButtons=(ushort)(input.wButtons&~(binding.PadBit&0xffff));
            if((binding.PadBit&KeyMap.LT)!=0)input.bLeftTrigger=0;if((binding.PadBit&KeyMap.RT)!=0)input.bRightTrigger=0;}
        return input;
    }
}

public static class WasdSprint
{
    public static float MagnitudePercent(short x,short y)=>Math.Min(100,MathF.Sqrt((float)x*x+(float)y*y)/32767f*100);
    public static bool WantsShift(short x,short y,int threshold,bool wasDown)
    {
        // A small release hysteresis prevents repeated ON/OFF at the ring boundary.
        float magnitude=MagnitudePercent(x,y);
        return magnitude>=Math.Clamp(threshold-(wasDown?4:0),50,99);
    }
}
