namespace CODMControllerProbe;

public partial class MainForm
{
    private string _fireModeTarget="B";
    private int _shortcutMask;
    private KeyboardKey _shortcutKeyboard=KeyboardKeys.B;
    private PadShortcut ReadPadShortcut()=>new(_fireModeEnabled,_fireModeUseCombo,_fireModeKey,_fireModeCombo,_fireModeTarget,_fireModeHoldMs);
    private void ResolveShortcutOwnership()
    {
        if(!_fireModeEnabled)return;
        var shortcut=ReadPadShortcut();
        int occupied=_padKeyMap.Aggregate(0,(mask,b)=>mask|b.PadBit);
        if(_keyboardChordsEnabled)occupied=_keyboardChords.Aggregate(occupied,(mask,c)=>mask|c.PadBit);
        if(_experimentalCursorEnabled)occupied|=_experimentalCursorKey;
        if(!shortcut.Valid||(occupied&shortcut.Mask)!=0)_fireModeEnabled=false;
    }
    private async Task<bool> SetPadShortcut(PadShortcut next)
    {
        if(next.Enabled&&!next.Valid)return false;
        var previous=ReadPadShortcut();
        bool purposeChanged=next.Enabled&&(!previous.Enabled||previous.Mask!=next.Mask||previous.Target!=next.Target||previous.UseCombo!=next.UseCombo);
        if(purposeChanged&&!await ConfirmMappingChange(Lang.T("手柄快捷键：","Pad shortcut: ")+next.Description+"\n"+Lang.T("启用后这些手柄键专用于此快捷键，单独按也不再触发原动作。","These pad buttons are reserved for this shortcut, including individual presses."),next.Sources.SelectMany(s=>ExistingUses(s))))return false;
        MappingEdited(()=>{if(next.Enabled)ClearSourceOverrides(next.Sources);_fireModeEnabled=next.Enabled;_fireModeUseCombo=next.UseCombo;_fireModeKey=next.Source;_fireModeCombo=next.Combo;_fireModeTarget=KeyboardKeys.ByName(next.Target)?.Name??"B";_fireModeHoldMs=Math.Clamp(next.HoldMs,0,1200);});
        RefreshRemapViews();return true;
    }
    private uint SendNamedKeyState(string name,ushort vk,ushort scan,string keyName,bool down,out string target,bool extended=false)
    {
        string owner="event:"+name;var key=new GameKey(vk,scan,extended);
        _gameKeys.SetDesired(owner,key,down);_gameKeys.Pump();var destination=_gameKeys.CurrentTarget;
        bool success=down?_gameKeys.Delivered(owner):!_gameKeys.ReleasePending(key);
        target=success?$"已投递 {keyName} 到「{destination.Title}」":$"投递待重试 {keyName}";return success?1u:0u;
    }
}
