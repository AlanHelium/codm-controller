namespace CODMControllerProbe;

// Owns one system modifier for WASD. Down is allowed only in the selected foreground
// game; owned Up must still be sent after focus changes or the game exits.
public sealed class ForegroundKeyHold
{
    private readonly object _gate=new();
    private readonly Func<GameKeyTarget,bool> _foreground;
    private readonly Func<GameKeyTarget,bool,bool> _send;
    private readonly Func<long> _clock;
    private readonly Action<string> _log;
    private GameKeyTarget _target,_heldTarget;
    private bool _desired,_held;
    private long _retryDown,_retryUp;
    public ForegroundKeyHold(Func<GameKeyTarget,bool> foreground,Func<GameKeyTarget,bool,bool> send,Action<string>? log=null,Func<long>? clock=null)
    {_foreground=foreground;_send=send;_log=log??(_=>{});_clock=clock??(()=>Environment.TickCount64);}
    public bool Delivered {get{lock(_gate)return _held;}}
    public void SetDesired(bool down,GameKeyTarget target){lock(_gate){_desired=down;_target=target;PumpCore(false);}}
    public void Pump(){lock(_gate)PumpCore(false);}
    public void Release(){lock(_gate){_desired=false;PumpCore(true);}}
    private void PumpCore(bool force)
    {
        long now=_clock();bool wanted=_desired&&_foreground(_target);
        if(_held&&(!wanted||_target.Handle!=_heldTarget.Handle||_target.ProcessId!=_heldTarget.ProcessId))
        {
            if(!force&&now<_retryUp)return;
            if(!_send(_heldTarget,false)){_retryUp=now+125;_log("WASD 系统 Shift 抬起失败，保留状态重试");return;}
            _held=false;_retryUp=0;_log("WASD 系统 Shift OFF（已释放）");
        }
        if(!wanted||_held||(!force&&now<_retryDown))return;
        if(!_send(_target,true)){_retryDown=now+125;_log("WASD 系统 Shift 按下失败，未记为已按下");return;}
        _held=true;_heldTarget=_target;_retryDown=0;_log("WASD 系统 Shift ON（仅游戏前台）");
    }
}
