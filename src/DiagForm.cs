namespace CODMControllerProbe;

public sealed class DiagForm:Form
{
    readonly Func<Task<StartupDiagnosticData>> _capture;
    readonly Label _summary;
    readonly UiStack _body;
    readonly (Label State,Label Detail)[] _rows;
    readonly Button _refresh,_start;
    readonly System.Windows.Forms.Timer _timer=new(){Interval=1800};
    bool _refreshing;
    public DiagForm(Func<Task<StartupDiagnosticData>> capture,Action startConversion,Action openGuide)
    {
        _capture=capture;Text=Lang.T("设备检测","Device diagnostics");Font=Assets.Ui(9.5f);Assets.Skin(this);
        ClientSize=new(Dpi.Px(850),Dpi.Px(650));
        var shell=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Padding=new Padding(Dpi.Px(18))};
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));shell.RowStyles.Add(new RowStyle(SizeType.Percent,100));shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));Controls.Add(shell);
        var viewport=new Panel{Dock=DockStyle.Fill,AutoScroll=true};shell.Controls.Add(viewport,0,0);
        _body=UiLayout.Stack();viewport.Controls.Add(_body);
        _summary=UiLayout.Text(Lang.T("正在读取当前设备与组件状态…","Reading current devices and components…"));UiLayout.Add(_body,_summary);
        string[] names=[Lang.T("ViGEmBus 虚拟手柄驱动","ViGEmBus virtual pad driver"),Lang.T("HidHide 实体输入隔离组件","HidHide isolation component"),Lang.T("实体手柄","Physical controller"),Lang.T("转换状态","Conversion status"),Lang.T("系统音频采集 · 枪声震动","System audio · shot rumble")];
        _rows=new (Label,Label)[names.Length];
        for(int i=0;i<names.Length;i++)
        {
            var card=UiLayout.Card(_body,names[i]);var state=UiLayout.Text(Lang.T("检测中…","Checking…"));var detail=UiLayout.Text("");
            state.Font=Assets.Ui(10,FontStyle.Bold);UiLayout.Add(card,state);UiLayout.Add(card,detail);_rows[i]=(state,detail);
        }
        var footer=UiLayout.Buttons(_body);_refresh=UiLayout.Choice(footer,Lang.T("刷新检测","Refresh"),()=>_=RefreshAsync());
        _start=UiLayout.Choice(footer,Lang.T("启动转换","Start conversion"),startConversion);_start.Enabled=false;
        UiLayout.Choice(footer,Lang.T("使用引导","Setup guide"),openGuide);UiLayout.Choice(footer,Lang.T("完成","Done"),Close);
        var note=UiLayout.Text(Lang.T("检测会自动刷新。只有 Windows 确实要求重启时才显示待重启；由你选择重启时间。","Checks refresh automatically. Restart is shown only when required; you choose when."));shell.Controls.Add(note,0,1);
        Shown+=async(_,_)=>{await RefreshAsync();if(!IsDisposed)_timer.Start();};
        _timer.Tick+=async(_,_)=>{if(Visible)await RefreshAsync();};
        Disposed+=(_,_)=>_timer.Dispose();viewport.SizeChanged+=(_,_)=>UiLayout.FitText(_body);
        UiLayout.FitText(shell);
    }
    internal async Task RefreshAsync()
    {
        if(_refreshing||IsDisposed)return;_refreshing=true;_refresh.Enabled=false;
        try
        {
            var data=await _capture();if(IsDisposed)return;
            _summary.Text=data.Components.Message;_summary.ForeColor=data.Components.Ready?Assets.OkGreen:StudioTheme.Accent;
            var rows=data.Rows();
            for(int i=0;i<rows.Length;i++)
            {
                var row=rows[i];_rows[i].State.Text=(row.Level==HealthLevel.Ready?"✓ ":row.Level==HealthLevel.Error?"✕ ":"• ")+row.Summary;
                _rows[i].State.ForeColor=row.Level==HealthLevel.Ready?Assets.OkGreen:row.Level==HealthLevel.Error?Assets.ErrRed:row.Level==HealthLevel.Waiting?StudioTheme.Accent:StudioTheme.Muted;
                _rows[i].Detail.Text=row.Detail;_rows[i].Detail.Visible=row.Detail.Length>0;
            }
            _start.Enabled=data.Components.Ready&&!data.Running&&!data.Preparing;UiLayout.FitText(_body);
        }
        catch(Exception ex){if(!IsDisposed){_summary.Text=Lang.T("检测尚未完成，请重试：","Check did not complete; retry: ")+ex.Message;_summary.ForeColor=StudioTheme.Accent;}}
        finally{_refreshing=false;if(!IsDisposed)_refresh.Enabled=true;}
    }
}
