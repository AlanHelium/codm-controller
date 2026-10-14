namespace CODMControllerProbe;

public partial class MainForm
{
    private bool _keyboardChordsEnabled,_experimentalCursorEnabled;
    private int _experimentalCursorKey=0x0040;
    private readonly List<PadKeyboardChord> _keyboardChords=new();
    private (int Source,GameKey First,GameKey Second)[] _chordSnapshot=Array.Empty<(int,GameKey,GameKey)>();
    private XInputNative.XInputGamepad _captureFrame;
    private long _captureFrameAt;
    private bool _mappingCaptureActive,_mappingAwaitRelease,_mappingConflictActive;
    private int _mappingRevision;
    private Func<string,string[],Task<bool>>? _mappingDecision=null;
    private Task<bool> ConfirmMappingChange(string title,IEnumerable<string> conflicts)
    {
        var lines=conflicts.Distinct().ToArray();if(lines.Length==0)return Task.FromResult(true);
        if(_mappingDecision!=null)return _mappingDecision(title,lines);
        var answer=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var form=new Form{Text=Lang.T("绑定冲突","Binding conflict"),Font=Assets.Ui(10)};var body=UiLayout.Stack(18);form.Controls.Add(body);
        UiLayout.Add(body,UiLayout.Text(title,true));foreach(var line in lines)UiLayout.Add(body,UiLayout.Text(line));
        UiLayout.Add(body,UiLayout.Text(Lang.T("应用后只保留新用途；旧的键盘、组合或光标绑定会被替换。游戏动作撞键时会交换两者的按键。","Only the new use will remain. Previous keyboard, chord or cursor bindings will be replaced. Conflicting game actions exchange their buttons.")));
        UiLayout.Action(body,Lang.T("替换 / 交换并应用","Replace / swap and apply"),()=>{answer.TrySetResult(true);form.Close();},true);
        UiLayout.Action(body,Lang.T("取消，保留旧设置","Cancel, keep current settings"),()=>{answer.TrySetResult(false);form.Close();});
        lock(_keyboardBindingGate){_mappingConflictActive=true;ResetMappingTransient();}
        form.FormClosed+=(_,_)=>{lock(_keyboardBindingGate){_mappingConflictActive=false;ResetMappingTransient();}answer.TrySetResult(false);};MountStudioForm(form,keepParentOnClose:true);return answer.Task;
    }
    private IEnumerable<string> ExistingUses(int source,int exceptAction=-1)
    {
        string name=KeyMap.BitName(source);
        for(int i=0;i<9;i++)if(i!=exceptAction&&_map.ActionBinding(i)==source)yield return name+Lang.T(" 当前用于："," currently used for: ")+MappingPolicy.ActionName(i);
        foreach(var use in OverrideUses(source))yield return use;
    }
    private IEnumerable<string> OverrideUses(int source)
    {
        string name=KeyMap.BitName(source);
        var owner=SourceOverrideName(source);if(owner.Length>0)yield return name+" · "+owner;
        if(_enableFastInteract&&_interactKey==source)yield return name+Lang.T(" · 长按交互 F"," · hold-to-interact F");
        if(_enableViewHoldTab&&source==0x0020)yield return name+Lang.T(" · 长按记分表 Tab"," · hold-to-scoreboard Tab");
        if(_enableRun&&source==0x0040)yield return name+Lang.T(" · L3 奔跑切换"," · L3 sprint toggle");
        if(_fireModeEnabled&&(_fireModeUseCombo?(_fireModeCombo&source)!=0:_fireModeKey==source))yield return name+Lang.T(" · 射击模式长按 B"," · fire-mode hold B");
    }
    private void RefreshRemapViews(){foreach(var form in _studioForms.OfType<RemapForm>())form.RefreshBindings();}

    private XInputNative.XInputGamepad? ReadPhysicalCapture()
    {lock(_keyboardBindingGate)return _captureFrameAt!=0&&Environment.TickCount64-_captureFrameAt<1500?_captureFrame:null;}
    private void SetMappingCapture(bool active)
    {lock(_keyboardBindingGate){if(_mappingCaptureActive==active)return;_mappingCaptureActive=active;ResetMappingTransient();}}
    private void ResetMappingTransient()
    {
        _mappingRevision++;
        _cursorMoveTimer.Stop();
        _outputPaused=false;PauseOutput();_gameKeys.Release();
        _bWasDown=false;_rsComboDown=false;_dpadLeftWasDown=false;_lastDpadLeftPressTick=0;
        _mappingAwaitRelease=MappingPolicy.Pressed(_captureFrame)!=0;
    }
    private void MappingEdited(Action edit)
    {lock(_keyboardBindingGate){ResetMappingTransient();edit();PublishMappingSnapshots();}SaveSettingsNow();}
    private void PublishMappingSnapshots()
    {
        Volatile.Write(ref _keyboardBindings,_padKeyMap.GroupBy(b=>b.PadBit).Select(g=>g.Last()).ToArray());
        _chordSnapshot=_keyboardChordsEnabled?_keyboardChords.Select(c=>(c.PadBit,KeyboardKeys.ByName(c.KeyNames[0])!,KeyboardKeys.ByName(c.KeyNames[1])!)).Select(c=>(c.PadBit,new GameKey(c.Item2.Vk,c.Item2.Scan,c.Item2.Extended),new GameKey(c.Item3.Vk,c.Item3.Scan,c.Item3.Extended))).ToArray():Array.Empty<(int,GameKey,GameKey)>();
    }
    private void ClearSourceOverrides(IEnumerable<int> sources)
    {
        var set=sources.ToHashSet();_padKeyMap.RemoveAll(b=>set.Contains(b.PadBit));if(_keyboardChordsEnabled)_keyboardChords.RemoveAll(c=>set.Contains(c.PadBit));
        if(_experimentalCursorEnabled&&set.Contains(_experimentalCursorKey))_experimentalCursorEnabled=false;
        if(_expRsCombo&&set.Contains(0x0080))_expRsCombo=false;
        if(_enableFastInteract&&set.Contains(_interactKey))_enableFastInteract=false;
        if(set.Contains(0x0020))_enableViewHoldTab=false;
        if(set.Contains(0x0040))_enableRun=false;
        if(_fireModeEnabled&&(_fireModeUseCombo?set.Any(s=>(_fireModeCombo&s)!=0):set.Contains(_fireModeKey)))_fireModeEnabled=false;
    }
    private async void AssignPhysicalAction(int index,int source)
    {
        if(!SettingsStore.IsValidBind(source))return;
        int old=_map.ActionBinding(index);
        var conflicts=ExistingUses(source,index).Concat(old!=source?OverrideUses(old):Array.Empty<string>());
        if(!await ConfirmMappingChange(MappingPolicy.ActionName(index)+" → "+KeyMap.BitName(source),conflicts)){RefreshRemapViews();return;}
        MappingEdited(()=>{_map.AssignAction(index,source);ClearSourceOverrides(new[]{source,old});});
        RefreshRemapViews();
    }
    private async void SetShoulderPreset(bool enabled)
    {
        if(_swapShoulderTriggers==enabled)return;
        var sources=new[]{0x0100,0x0200,KeyMap.LT,KeyMap.RT};
        string result=string.Join(" · ",Enumerable.Range(4,4).Select(i=>MappingPolicy.ActionName(i)+" → "+KeyMap.BitName(MappingPolicy.SwapSource(_map.ActionBinding(i)))));
        if(!await ConfirmMappingChange(Lang.T(enabled?"开启肩键 / 扳机交换":"关闭肩键 / 扳机交换",enabled?"Enable shoulder / trigger swap":"Disable shoulder / trigger swap")+"\n"+result,sources.SelectMany(OverrideUses))){RefreshRemapViews();return;}
        MappingEdited(()=>{MappingPolicy.SwapActions(_map);_interactKey=MappingPolicy.SwapSource(_interactKey);_swapShoulderTriggers=enabled;ClearSourceOverrides(new[]{0x0100,0x0200,KeyMap.LT,KeyMap.RT});});
        RefreshRemapViews();
    }
    private async void RestoreActionBindings()
    {
        var defaults=new KeyMap();var sources=Enumerable.Range(0,9).Select(defaults.ActionBinding).ToArray();
        if(!await ConfirmMappingChange(Lang.T("恢复游戏动作默认键位","Restore default game actions"),sources.SelectMany(OverrideUses)))return;
        MappingEdited(()=>{var d=new KeyMap();_map.Jump=d.Jump;_map.Crouch=d.Crouch;_map.Reload=d.Reload;_map.Switch=d.Switch;_map.Aim=d.Aim;_map.Shoot=d.Shoot;_map.Tactical=d.Tactical;_map.Lethal=d.Lethal;_map.MeleePing=d.MeleePing;_swapShoulderTriggers=false;ClearSourceOverrides(Enumerable.Range(0,9).Select(_map.ActionBinding));});
        RefreshRemapViews();
    }
    private async Task<bool> SetKeyboardChords(bool enabled,IEnumerable<PadKeyboardChord> bindings)
    {
        var next=bindings.Select(c=>c.Normalize()).Where(c=>c!=null).Select(c=>c!).GroupBy(c=>c.PadBit).Select(g=>g.Last()).ToArray();
        var changed=next.Where(c=>!_keyboardChordsEnabled||!_keyboardChords.Any(old=>old.PadBit==c.PadBit&&old.Name==c.Name));
        if(enabled&&!await ConfirmMappingChange(Lang.T("应用实验键盘组合：","Apply experimental keyboard chords: ")+string.Join("；",next.Select(c=>KeyMap.BitName(c.PadBit)+" → "+c.Name)),changed.SelectMany(c=>ExistingUses(c.PadBit))))return false;
        MappingEdited(()=>{if(enabled)ClearSourceOverrides(next.Select(c=>c.PadBit));_keyboardChords.Clear();_keyboardChords.AddRange(next);_keyboardChordsEnabled=enabled;});
        return true;
    }
    private async Task<bool> SetExperimentalCursor(bool enabled,int key)
    {
        if(!SettingsStore.IsValidBind(key))return false;
        if(enabled&&!await ConfirmMappingChange(Lang.T("将 ","Use ")+KeyMap.BitName(key)+Lang.T(" 用于光标切换"," for cursor toggle"),ExistingUses(key)))return false;
        MappingEdited(()=>{if(enabled)ClearSourceOverrides(new[]{key});_experimentalCursorKey=key;_experimentalCursorEnabled=enabled;});
        return true;
    }
    private Task<bool> ConfirmKeyboardBindings(List<PadKeyBind> candidate)
    {
        var changed=candidate.Where(b=>!_padKeyMap.Any(old=>old.PadBit==b.PadBit&&old.KeyName==b.KeyName));
        return ConfirmMappingChange(Lang.T("手柄 → 键盘：","Pad → keyboard: ")+string.Join("；",changed.Select(b=>KeyMap.BitName(b.PadBit)+" → "+b.KeyName)),changed.SelectMany(b=>ExistingUses(b.PadBit)));
    }
    private string SourceOverrideName(int source)
    {
        var keyboard=_padKeyMap.LastOrDefault(b=>b.PadBit==source);if(keyboard!=null)return Lang.T("键盘接管：","Keyboard: ")+keyboard.KeyName;
        var chord=_keyboardChordsEnabled?_keyboardChords.LastOrDefault(c=>c.PadBit==source):null;if(chord!=null)return Lang.T("组合接管：","Chord: ")+chord.Name;
        return _experimentalCursorEnabled&&source==_experimentalCursorKey?Lang.T("光标接管","Cursor override"):"";
    }
    private void LoadExperimentalMappings(AppSettings s)
    {
        _keyboardChords.Clear();foreach(var chord in s.KeyboardChords??new()){var resolved=chord?.Normalize();if(resolved!=null&&!_keyboardChords.Any(c=>c.PadBit==resolved.PadBit))_keyboardChords.Add(resolved);}
        _keyboardChordsEnabled=s.EnableKeyboardChords;_experimentalCursorEnabled=s.ExperimentalCursorEnabled;
        _experimentalCursorKey=SettingsStore.IsValidBind(s.ExperimentalCursorKey)?s.ExperimentalCursorKey:0x0040;
        // Legacy malformed configs retain the keyboard owner and never emit a second purpose.
        var keyboardSources=_padKeyMap.Select(b=>b.PadBit).ToHashSet();_keyboardChords.RemoveAll(c=>keyboardSources.Contains(c.PadBit));
        if(_experimentalCursorEnabled&&(keyboardSources.Contains(_experimentalCursorKey)||(_keyboardChordsEnabled&&_keyboardChords.Any(c=>c.PadBit==_experimentalCursorKey))))_experimentalCursorEnabled=false;
        PublishMappingSnapshots();
    }
    private void MoveCursorToGameCorner()
    {
        var target=_gameKeys.CurrentTarget;if(!IsGameKeyTargetForeground(target))return;
        var point=new POINT{X=3,Y=3};if(MappingClientToScreen(target.Handle,ref point))SetCursorPos(point.X,point.Y);
    }
    [System.Runtime.InteropServices.DllImport("user32.dll",EntryPoint="ClientToScreen")]private static extern bool MappingClientToScreen(IntPtr window,ref POINT point);
    private void ProcessPhysicalFrame(XInputNative.XInputGamepad raw,bool isDs)
    {
        lock(_keyboardBindingGate)
        {
            _captureFrame=raw;_captureFrameAt=Environment.TickCount64;
            if(_mappingCaptureActive||_mappingConflictActive||(_mappingAwaitRelease&&MappingPolicy.Pressed(raw)!=0)){PauseOutput();return;}
            _mappingAwaitRelease=false;
            if(!OutputReady())return;
            LogTriggerState(raw);TrackKeyMapInject(raw);
            foreach(var chord in _chordSnapshot){bool down=KeyMap.Hit(chord.Source,raw.wButtons,raw.bLeftTrigger,raw.bRightTrigger);_gameKeys.SetDesired("chord:"+chord.Source+":0",chord.First,down);_gameKeys.SetDesired("chord:"+chord.Source+":1",chord.Second,down);}
            _gameKeys.Pump();
            var game=WithoutKeyboardBoundInputs(raw);
            foreach(var chord in _chordSnapshot)game=RemoveSource(game,chord.Source);
            TrackComboPress(game,isDs);
            if(_experimentalCursorEnabled)game=RemoveSource(game,_experimentalCursorKey);
            else if(_cursorSwitchMode==0&&_comboWasDown)game.wButtons=(ushort)(game.wButtons&~_comboMask);
            TrackDpadLeftDoublePress(_map.UsesSource(4)?RemoveSource(game,4):game);TrackHoldBProne(game);TrackHoldShoulderCombo(game);TrackHoldFastInteract(game);TrackHoldViewTab(_map.UsesSource(0x0020)?RemoveSource(game,0x0020):game);
            if(isDs)TrackTouchpadTab();TrackLeftStickRun(game);TrackExpRsCombo(game);TrackFireModeSwap(game);ForwardToDs4(game);
        }
    }
    private static XInputNative.XInputGamepad RemoveSource(XInputNative.XInputGamepad g,int source)
    {g.wButtons=(ushort)(g.wButtons&~source);if(source==KeyMap.LT)g.bLeftTrigger=0;if(source==KeyMap.RT)g.bRightTrigger=0;return g;}
}
