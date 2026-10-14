namespace CODMControllerProbe;

public sealed class PadShortcutForm : Form
{
    private readonly CheckBox _enabled;
    private readonly ComboBox _mode,_first,_second,_target;
    private readonly Label _hint;
    private readonly Func<PadShortcut,Task<bool>> _commit;
    private readonly Func<XInputNative.XInputGamepad?> _read;
    private readonly Action<bool> _captureChanged;
    private readonly System.Windows.Forms.Timer _timer=new(){Interval=16};
    private int _hold,_candidate;
    private long _candidateAt;
    private bool _recording,_released,_applying;
    public PadShortcutForm(PadShortcut initial,Func<PadShortcut,Task<bool>> commit,Func<XInputNative.XInputGamepad?> read,Action<bool> captureChanged)
    {
        _commit=commit;_read=read;_captureChanged=captureChanged;_hold=initial.HoldMs;
        Text=Lang.T("手柄快捷键 → 键盘单键","Pad shortcut → keyboard key");Font=Assets.Ui(10);Assets.Skin(this);
        var page=new Panel{Dock=DockStyle.Fill,AutoScroll=true};var body=UiLayout.Stack(20);page.Controls.Add(body);Controls.Add(page);
        _enabled=UiLayout.Check(body,Lang.T("启用手柄快捷键","Enable pad shortcut"),initial.Enabled,_=>{});
        UiLayout.Add(body,UiLayout.Text(Lang.T("启用后选中的手柄键专用于此快捷键，不再触发原游戏动作；双键组合需要两键同时按住。冲突会在应用前列出。","Selected pad buttons are reserved for this shortcut and no longer trigger original actions. A two-button combo requires both buttons held together. Conflicts are shown before applying.")));
        _mode=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Font=Assets.Ui(10)};_mode.Items.AddRange(new object[]{Lang.T("两个手柄键 → 一个键盘键","Two pad buttons → one keyboard key"),Lang.T("一个手柄键长按 → 一个键盘键","One held pad button → one keyboard key")});_mode.SelectedIndex=initial.UseCombo?0:1;
        UiLayout.Add(body,UiLayout.Text(Lang.T("触发方式","Trigger method")));UiLayout.Add(body,_mode);
        var sources=initial.Sources.ToArray();_first=KeyboardChordForm.PadPicker(sources.FirstOrDefault(initial.Source));_second=KeyboardChordForm.PadPicker(sources.Length>1?sources[1]:0x0020);
        UiLayout.Add(body,UiLayout.Text(Lang.T("实体手柄键一","First physical pad button")));UiLayout.Add(body,_first);
        UiLayout.Add(body,UiLayout.Text(Lang.T("实体手柄键二（双键模式）","Second physical pad button (combo mode)")));UiLayout.Add(body,_second);_second.Enabled=initial.UseCombo;_mode.SelectedIndexChanged+=(_,_)=>{StopRecording();_second.Enabled=_mode.SelectedIndex==0;};
        UiLayout.Action(body,Lang.T("录制实体手柄键","Record physical pad buttons"),StartRecording);
        UiLayout.Add(body,UiLayout.Text(Lang.T("要输出的键盘键","Keyboard output key")));_target=KeyboardChordForm.KeyPicker(initial.Target);UiLayout.Add(body,_target);
        UiLayout.Slider(body,Lang.T("触发前按住时间","Hold before activation"),_hold,0,1200,v=>v==0?Lang.T("立即","Immediate"):v+" ms",v=>_hold=v);
        _hint=UiLayout.Text("");UiLayout.Add(body,_hint);
        UiLayout.Action(body,Lang.T("应用设置","Apply settings"),async()=>{
            if(_applying)return;StopRecording();int first=MappingPolicy.PadSources[_first.SelectedIndex],second=MappingPolicy.PadSources[_second.SelectedIndex];
            var next=new PadShortcut(_enabled.Checked,_mode.SelectedIndex==0,first,first|second,KeyboardKeys.All[_target.SelectedIndex].Name,_hold);
            if(next.Enabled&&!next.Valid){_hint.Text=Lang.T("双键组合请选择两个不同的手柄键。","Choose two different pad buttons for the combo.");return;}
            _applying=true;try{if(await _commit(next)&&!IsDisposed)Close();else if(!IsDisposed)_hint.Text=Lang.T("未应用，旧设置保持不变。","Not applied; previous settings remain.");}finally{_applying=false;}
        },true);
        UiLayout.Action(body,Lang.T("取消","Cancel"),()=>Close());_timer.Tick+=(_,_)=>Poll();
    }
    private void StartRecording(){StopRecording();_recording=true;_released=false;_candidate=0;_captureChanged(true);_hint.Text=Lang.T("先松开所有手柄键，再按下需要的一个键或同时按住两个键。","Release all pad buttons, then press one button or hold two together.");_timer.Start();}
    private void Poll()
    {
        var frame=_read();if(frame==null){_hint.Text=Lang.T("请连接手柄并启动转换；只读取当前实体手柄。","Connect a pad and start conversion; only the current physical pad is read.");return;}
        int pressed=MappingPolicy.Pressed(frame.Value);if(!_released){if(pressed==0)_released=true;return;}
        int count=System.Numerics.BitOperations.PopCount((uint)pressed);int expected=_mode.SelectedIndex==0?2:1;
        if(count!=expected){_candidate=0;return;}if(_candidate!=pressed){_candidate=pressed;_candidateAt=Environment.TickCount64;return;}
        if(Environment.TickCount64-_candidateAt<150)return;
        var sources=MappingPolicy.PadSources.Where(s=>(pressed&s)!=0).ToArray();if(sources.Length!=expected)return;
        _first.SelectedIndex=Array.IndexOf(MappingPolicy.PadSources,sources[0]);if(expected==2)_second.SelectedIndex=Array.IndexOf(MappingPolicy.PadSources,sources[1]);
        StopRecording();_hint.Text=Lang.T("已录制：","Recorded: ")+string.Join(" + ",sources.Select(KeyMap.BitName))+Lang.T("。点击应用后保存。",". Apply to save.");
    }
    private void StopRecording(){_timer.Stop();if(_recording){_recording=false;_captureChanged(false);}}
    protected override void Dispose(bool disposing){if(disposing){StopRecording();_timer.Dispose();}base.Dispose(disposing);}
}
