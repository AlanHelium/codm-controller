using System.Drawing;
using System.Windows.Forms;

namespace CODMControllerProbe;

/// <summary>
/// 设置总窗口（v0.3 二级菜单结构）：左侧分类导航，右侧只显示当前分类内容，不再把全部设置堆在一页。
///   灵敏度      → 封装 / 档位 / ADS 倍率 / 曲线预览 / 微调手感
///   手感与键位  → 键位修正 / 快速趴下 / 长按触发时长 / 推左摇杆光标 / 退出清理驱动
///   功能设置    → Pro 专属：语音设置 / 震动设置（非 Pro 显示升级提示）
///   工具与支持  → 按键测试 / 诊断 / 导出 / 导入 / 恢复默认 / 语言 / 使用引导
/// 按版本裁剪：免费版灵敏度只显示出厂锁定说明；高级版/Pro 全开放。
/// </summary>
public sealed class SettingsForm : Form
{
    private readonly int _versionLevel;
    private readonly Action<int> _onSensLevel;      // 0..19
    private readonly Action<bool, int> _onProne;    // 快速趴下（开关, 时长 ms）
    private readonly Action<int> _onNukeHoldChanged = _ => { };      // 核弹长按时长
    private readonly Action<int> _onInteractHoldChanged = _ => { };  // 快速交互长按时长
    private readonly string _activePack;            // 当前激活封装 Key（如 "cod20-dyn"）
    private readonly Action<string> _onSwitchPack;  // 切换封装
    private readonly Action<float> _onAdsMult;      // ADS 开镜倍率
    private readonly Func<FeelCurve[]> _preview;    // 曲线预览数据提供者（左/腰射/开镜 三条显示曲线）

    private readonly List<Button> _sensBtns = new();
    private readonly List<Button> _packBtns = new();
    private readonly List<Button> _navBtns = new();

    private readonly Panel _previewPanel = new();

    private Panel _body = null!;
    private int proneVal = 150;
    private int nukeVal = 400;
    private int interactVal = 150;
    private readonly bool _nudgeCursor;
    private readonly Action<bool> _onNudgeChanged = _ => { };
    private readonly bool _cleanDriversOnExit;
    private readonly Action<bool> _onCleanChanged = _ => { };
    private readonly Action _onUnlock;
    private readonly bool _swapShoulderTriggers;
    private readonly Action<bool> _onSwapChanged = _ => { };
    private readonly int _cursorSwitchMode;
    private readonly Action<bool> _onCursorModeChanged = _ => { };
    private readonly bool _fastInteract;
    private readonly Action<bool> _onFastInteractChanged = _ => { };
    private readonly bool _enableRun;
    private readonly bool _autoRun;
    private readonly bool _gameDefaultRun;
    private readonly bool _returnWalk;
    private readonly int _runThreshold;
    private readonly Action<bool, int, bool, bool, bool> _onRunChanged = (_, _, _, _, _) => { };

    // ---- Gyro 实验项（默认关）----
    private readonly bool _gyroEnabled;
    private readonly int _gyroSens, _gyroDz, _gyroSm;
    private readonly bool _gyroInvX, _gyroInvY;
    private readonly Action<bool, int, int, int, bool, bool> _onGyroChanged = (_, _, _, _, _, _) => { };

    // ---- 实验：左摇杆移动替代（触屏保留实验；WASD 已转正移入手感与键位区）----
    private readonly bool _expWasdOn;
    private readonly int _expWasdThr;
    private readonly bool _isDsSource;   // 当前输入源是否为 DS 系手柄
    private readonly int _meleeKey = 0x0080;        // 近战/标点绑定键（默认 R3）
    private readonly Action _onMeleeRemap = () => { };
    // v3.1：按键映射区 + 实验 R3 组合 + 切换武器射击模式
    private List<PadKeyBind> _padKeyMap = new();
    private readonly Action<List<PadKeyBind>> _onKeyMapChanged = _ => { };
    private bool _expRsCombo;
    private readonly Action<bool> _onExpRsChanged = _ => { };
    private bool _fireModeEnabled;
    private int _fireModeHoldMs = 250;
    private bool _fireModeUseCombo = true;
    private int _fireModeKey = 0x0010;
    private ushort _fireModeCombo;
    private readonly Action<bool, int, bool, int, ushort> _onFireModeChanged = (_, _, _, _, _) => { };
    private readonly bool _expTouchOn;
    private readonly int _expTouchRadius;
    private readonly int _expTouchSens;
    private readonly Action<bool, int, bool, int, int> _onExpChanged2 = (_, _, _, _, _) => { };

    // ---- 视图键长按 → Tab（打开记分表）----
    private readonly bool _viewHoldTab;
    private readonly int _viewHoldVal;
    private readonly Action<bool, int> _onViewHoldChanged = (_, _) => { };

    public SettingsForm(
        int versionLevel, int sensLevel,
        bool fastProne, int proneMs, int nukeMs, int interactMs,
        string activePack, Action<string> onSwitchPack,
        Action<int> onSensLevel, Action<float> onAdsMult, float adsMult,
        Func<FeelCurve[]> preview,
        Action<bool, int> onProne,
        Action<int> onNukeHoldChanged, Action<int> onInteractHoldChanged,
        Action onRemap, Action onFeelFine,
        Action onVoice, Action onRumble, Action onDiag,
        Action onExport, Action onImport, Action onReset,
        Action onLang, Action onTestKeys, Action onGuide,
        Action onHidProbe, Action onTestLog,
        Action onUnlock,
        bool nudgeCursor, Action<bool> onNudgeChanged,
        bool swapShoulderTriggers, Action<bool> onSwapChanged,
        int cursorSwitchMode, Action<bool> onCursorModeChanged,
        bool fastInteract, Action<bool> onFastInteractChanged,
        bool enableRun, int runThreshold, bool autoRun, bool gameDefaultRun, bool returnWalk, Action<bool, int, bool, bool, bool> onRunChanged,
        bool cleanDriversOnExit, Action<bool> onCleanChanged,
        bool gyroEnabled, float gyroSens, int gyroDeadzone, float gyroSmooth, bool gyroInvertX, bool gyroInvertY, Action<bool, int, int, int, bool, bool> onGyroChanged,
        bool expWasdOn, int expWasdThreshold, bool expTouchOn, int expTouchRadius, int expTouchSens, Action<bool, int, bool, int, int> onExpChanged2,
        bool viewHoldTab, int viewHoldMs, Action<bool, int> onViewHoldChanged,
        int meleeKey, Action onMeleeRemap,
        bool isDsSource,
        // ---- v3.1：按键映射区 + 实验 R3 组合 + 切换武器射击模式 ----
        List<PadKeyBind> padKeyMap, Action<List<PadKeyBind>> onKeyMapChanged,
        bool expRsCombo, Action<bool> onExpRsChanged,
        bool fireModeEnabled, int fireModeHoldMs, bool fireModeUseCombo, int fireModeKey, ushort fireModeCombo,
        Action<bool, int, bool, int, ushort> onFireModeChanged, int initialSection = -1, Action? onReport = null, Func<string>? movementStatus = null, Func<string>? mappingStatus = null)
    {
        _versionLevel = versionLevel;
        _onSensLevel = onSensLevel;
        _onProne = onProne;
        _onNukeHoldChanged = onNukeHoldChanged;
        _onInteractHoldChanged = onInteractHoldChanged;
        _activePack = activePack;
        _onSwitchPack = onSwitchPack;
        _onAdsMult = onAdsMult;
        _preview = preview;
        proneVal = proneMs;
        nukeVal = nukeMs;
        interactVal = interactMs;
        _nudgeCursor = nudgeCursor;
        _onNudgeChanged = onNudgeChanged;
        _cleanDriversOnExit = cleanDriversOnExit;
        _onCleanChanged = onCleanChanged;
        _onUnlock = onUnlock;
        _swapShoulderTriggers = swapShoulderTriggers;
        _onSwapChanged = onSwapChanged;
        _cursorSwitchMode = cursorSwitchMode;
        _onCursorModeChanged = onCursorModeChanged;
        _fastInteract = fastInteract;
        _onFastInteractChanged = onFastInteractChanged;
        _enableRun = enableRun;
        _runThreshold = runThreshold;
        _autoRun = autoRun;
        _gameDefaultRun = gameDefaultRun;
        _returnWalk = returnWalk;
        _onRunChanged = onRunChanged;
        _gyroEnabled = gyroEnabled;
        _gyroSens = (int)Math.Round(gyroSens * 100f);
        _gyroDz = gyroDeadzone;
        _gyroSm = (int)Math.Round(gyroSmooth * 100f);
        _gyroInvX = gyroInvertX;
        _gyroInvY = gyroInvertY;
        _onGyroChanged = onGyroChanged;
        _expWasdOn = expWasdOn;
        _expWasdThr = expWasdThreshold;
        _expTouchOn = expTouchOn;
        _expTouchRadius = expTouchRadius;
        _expTouchSens = expTouchSens;
        _onExpChanged2 = onExpChanged2;
        _viewHoldTab = viewHoldTab;
        _viewHoldVal = viewHoldMs;
        _isDsSource = isDsSource;
        _onViewHoldChanged = onViewHoldChanged;
        _meleeKey = meleeKey;
        _onMeleeRemap = onMeleeRemap;
        _padKeyMap = padKeyMap;
        _onKeyMapChanged = onKeyMapChanged;
        _expRsCombo = expRsCombo;
        _onExpRsChanged = onExpRsChanged;
        _fireModeEnabled = fireModeEnabled;
        _fireModeHoldMs = fireModeHoldMs;
        _fireModeUseCombo = fireModeUseCombo;
        _fireModeKey = fireModeKey;
        _fireModeCombo = fireModeCombo;
        _onFireModeChanged = onFireModeChanged;

Dpi.Scale = Math.Max(1f, DeviceDpi / 96f);
        Text = Lang.T("设置 · CODM 手柄转换", "Settings · CODM Controller Bridge");
        AutoScaleMode = AutoScaleMode.None;
        var area = Screen.FromControl(this).WorkingArea;
        ClientSize = new Size(Math.Min(Dpi.Px(960),area.Width-Dpi.Px(40)),Math.Min(Dpi.Px(720),area.Height-Dpi.Px(80)));
        MinimumSize = new Size(Math.Min(Dpi.Px(760),ClientSize.Width),Math.Min(Dpi.Px(500),ClientSize.Height));
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterParent;
        Font=Assets.Ui(9.5f); Assets.Skin(this);
        BackColor=Color.FromArgb(244,246,249);
        var shell=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=3,Margin=Padding.Empty};
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,Dpi.Px(184)));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(shell);
        var navigation=UiLayout.Stack(14); navigation.BackColor=Color.White;
        UiLayout.Add(navigation,UiLayout.Text(Lang.T("设置中心","Settings"),true));
        UiLayout.Add(navigation,UiLayout.Text(VersionName(versionLevel)));
        for(int i=0;i<SectionNames.Length;i++) {
            int idx=i;
            var button=UiLayout.Action(navigation,SectionNames[i],()=>SelectSection(idx));
            button.AccessibleName=SectionNames[i]; _navBtns.Add(button);
        }
        var navViewport=new Panel {Dock=DockStyle.Fill,AutoScroll=true,BackColor=Color.White,Margin=Padding.Empty};
        navViewport.Controls.Add(navigation); shell.Controls.Add(navViewport,0,0); shell.SetRowSpan(navViewport,3);
        var heading=UiLayout.Stack(20); heading.BackColor=Color.White; heading.Dock=DockStyle.Fill;
        _sectionTitle=UiLayout.Text("",true); _sectionTitle.Font=Assets.Ui(15f,FontStyle.Bold);
        _sectionHint=UiLayout.Text(""); UiLayout.Add(heading,_sectionTitle); UiLayout.Add(heading,_sectionHint);
        shell.Controls.Add(heading,1,0);
        _body=new Panel {Dock=DockStyle.Fill,Margin=Padding.Empty};
        shell.Controls.Add(_body,1,1);
        foreach(var name in SectionNames) {
            var page=new Panel {Dock=DockStyle.Fill,AutoScroll=true,Padding=new Padding(Dpi.Px(20)),Visible=false,AccessibleName=name};
            var stack=UiLayout.Stack(); page.Controls.Add(stack); _pages.Add((page,stack)); _body.Controls.Add(page);
            page.SizeChanged+=(_,_)=> { UiLayout.FitText(stack); };
        }
        var footer=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Padding=new Padding(Dpi.Px(20),Dpi.Px(8),Dpi.Px(20),Dpi.Px(8)),BackColor=Color.White};
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.Controls.Add(UiLayout.Text(Lang.T("调整即时生效并自动保存","Changes apply and save immediately")),0,0);
        var done=new StudioButton {Text=Lang.T("完成","Done"),AutoSize=true,MinimumSize=new Size(Dpi.Px(100),Dpi.Px(40)),DialogResult=DialogResult.OK,Font=Assets.Ui(10f,FontStyle.Bold)};
        Assets.StylePrimary(done); UiLayout.Responsive(done); footer.Controls.Add(done,1,0); shell.Controls.Add(footer,1,2); AcceptButton=done; CancelButton=done;

        // These controls share live local values, so changing one page never restores stale values from another.
        bool wasdOn=_expWasdOn,touchOn=_expTouchOn; int wasdThreshold=_expWasdThr,touchRadius=_expTouchRadius,touchSensitivity=_expTouchSens;
        void SendMovement()=>_onExpChanged2(wasdOn,wasdThreshold,touchOn,touchRadius,touchSensitivity);
        if(initialSection<0 || initialSection==0) {
        var feel=_pages[0].Stack;
        var preset=UiLayout.Card(feel,Lang.T("预设与灵敏度","Preset & sensitivity"),Lang.T("先选择曲线风格，再调节整体快慢。","Choose a response style, then set your overall sensitivity."));
        var packs=UiLayout.Buttons(preset);
        foreach(var style in new[]{("dyn",Lang.T("动态","Dynamic")),("lin",Lang.T("线性","Linear")),("std",Lang.T("标准","Standard"))}) {
            string key=style.Item1;
            var button=UiLayout.Choice(packs,style.Item2,()=>{_onSwitchPack(key);RefreshPackHighlight(key);PreviewInvalidate();},key==_activePack);
            button.Tag=key; _packBtns.Add(button);
        }
        UiLayout.Add(preset,UiLayout.Text(Lang.T("灵敏度档位 · 1 慢 / 20 快","Sensitivity · 1 slow / 20 fast")));
        var tiers=UiLayout.Buttons(preset);
        for(int i=0;i<20;i++) {
            int idx=i; _sensBtns.Add(UiLayout.Choice(tiers,(i+1).ToString(),()=>{_onSensLevel(idx);RefreshSensHighlight(idx);PreviewInvalidate();},idx==sensLevel));
        }
        UiLayout.Slider(preset,Lang.T("开镜灵敏度倍率","ADS sensitivity multiplier"),(int)(adsMult*100),50,150,v=>(v/100f).ToString("0.00")+"×",v=>{_onAdsMult(v/100f);PreviewInvalidate();},Lang.T("只调整开镜时的右摇杆响应。","Affects the right stick while aiming down sights."),5);
        var curves=UiLayout.Card(feel,Lang.T("响应曲线","Response curves"),Lang.T("灰：左摇杆 · 橙：腰射 · 蓝：开镜","Gray: left stick · orange: hip · blue: ADS"));
        _previewPanel.Size=new Size(Dpi.Px(210),Dpi.Px(210)); _previewPanel.BackColor=Assets.CanvasDark;
        _previewPanel.Paint+=(_,e)=>DrawPreview(e.Graphics,_previewPanel.ClientSize);
        var graphRow=UiLayout.Buttons(curves); graphRow.Controls.Add(_previewPanel);
        UiLayout.Action(curves,Lang.T("打开曲线编辑器 →","Open curve editor →"),onFeelFine,true);

        }
        if(initialSection<0 || initialSection==1) {
        var keys=_pages[1].Stack;
        var actions=UiLayout.Card(keys,Lang.T("游戏动作键位","Game actions"),Lang.T("跳跃、下蹲、换弹、切枪、近战，以及肩键/扳机交换。","Jump, crouch, reload, switch weapon, melee, and shoulder/trigger swap."));
        UiLayout.Action(actions,Lang.T("设置游戏动作与组合键 →","Set game actions & combos →"),onRemap,true);
        var keyboard=UiLayout.Card(keys,Lang.T("手柄 → 键盘映射","Pad → keyboard mapping"),Lang.T("为指定手柄键选择一个键盘按键。","Choose a keyboard key for each pad button."));
        var bindings=UiLayout.Text(Lang.T($"已设置 {_padKeyMap.Count} 条映射",$"{_padKeyMap.Count} bindings configured")); UiLayout.Add(keyboard,bindings);
        UiLayout.Action(keyboard,Lang.T("编辑键盘映射 →","Edit keyboard mapping →"),()=>{
            var form=new KeyBindForm(new List<PadKeyBind>(_padKeyMap),list=>{_padKeyMap=list;_onKeyMapChanged(list);bindings.Text=Lang.T($"已设置 {list.Count} 条映射",$"{list.Count} bindings configured");});
            OpenInline(form, Lang.T("键盘映射","Keyboard mapping"));
        },true);
        if(mappingStatus!=null)
        {
            var status=UiLayout.Text(mappingStatus());status.ForeColor=StudioTheme.Accent;UiLayout.Add(keyboard,status);
            var deliveryTimer=new System.Windows.Forms.Timer{Interval=150};deliveryTimer.Tick+=(_,_)=>{if(Visible)status.Text=mappingStatus();};
            Shown+=(_,_)=>deliveryTimer.Start();Disposed+=(_,_)=>deliveryTimer.Dispose();
        }
        var fire=UiLayout.Card(keys,Lang.T("武器射击模式","Weapon fire mode"),Lang.T("长按指定按键或组合，发送键盘 B。","Hold a chosen button or combo to send keyboard B."));
        int fireMs=_fireModeHoldMs; bool fireOn=_fireModeEnabled;
        void SendFire()=>_onFireModeChanged(fireOn,fireMs,_fireModeUseCombo,_fireModeKey,_fireModeCombo);
        UiLayout.Check(fire,Lang.T("启用切换射击模式","Enable fire-mode switch"),fireOn,v=>{fireOn=v;_fireModeEnabled=v;SendFire();});
        var mode=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Font=Assets.Ui(9.5f),Margin=new Padding(0,0,0,Dpi.Px(12))};
        mode.Items.AddRange(new object[]{Lang.T("双键组合","Two-button combo"),Lang.T("单键长按","Single-button hold")}); mode.SelectedIndex=_fireModeUseCombo?0:1;
        UiLayout.Add(fire,UiLayout.Text(Lang.T("触发方式","Trigger method"))); UiLayout.Add(fire,mode);
        string FireName()=>_fireModeUseCombo?(_fireModeCombo==0?Lang.T("未录制组合","No combo recorded"):KeyMap.ComboName(_fireModeCombo)):KeyMap.BitName(_fireModeKey);
        var fireName=UiLayout.Text(Lang.T("当前触发：","Current trigger: ")+FireName()); UiLayout.Add(fire,fireName);
        mode.SelectedIndexChanged+=(_,_)=>{_fireModeUseCombo=mode.SelectedIndex==0;fireName.Text=Lang.T("当前触发：","Current trigger: ")+FireName();SendFire();};
        UiLayout.Action(fire,Lang.T("录制触发按键 →","Record trigger →"),()=>{
            var rec=new FireModeKeyForm(_fireModeUseCombo,_fireModeKey,_fireModeCombo,(combo,key,mask)=>{
                _fireModeUseCombo=combo;_fireModeKey=key;_fireModeCombo=mask;mode.SelectedIndex=combo?0:1;
                fireName.Text=Lang.T("当前触发：","Current trigger: ")+FireName();SendFire();}); OpenInline(rec, Lang.T("录制触发按键","Record trigger"));
        });
        UiLayout.Slider(fire,Lang.T("长按触发时间","Hold duration"),fireMs,100,1200,v=>v+" ms",v=>{fireMs=v;_fireModeHoldMs=v;SendFire();});

        }
        if(initialSection<0 || initialSection==2) {
        var movement=_pages[2].Stack;
        var wasd=UiLayout.Card(movement,Lang.T("WASD 移动","WASD movement"),Lang.T("左摇杆转换为八方向键盘移动。","Convert the left stick to eight-way keyboard movement."));
        UiLayout.Check(wasd,Lang.T("启用 WASD 移动","Enable WASD movement"),wasdOn,v=>{wasdOn=v;if(v)touchOn=false;SendMovement();});
        UiLayout.Slider(wasd,Lang.T("外圈冲刺阈值","Outer-ring sprint threshold"),wasdThreshold,50,99,v=>v+"%",v=>{wasdThreshold=v;SendMovement();},Lang.T("阈值按摇杆推幅的百分比计算。","Threshold is a percentage of stick magnitude."));
        UiLayout.Add(wasd,UiLayout.Text(Lang.T("使用 WASD 时，请在游戏中将奔跑模式设为“长按奔跑”。","When using WASD, set in-game sprint to Hold to sprint.")));
        if(movementStatus!=null)
        {
            var live=UiLayout.Text(movementStatus());live.ForeColor=StudioTheme.Accent;UiLayout.Add(wasd,live);
            UiLayout.Add(wasd,UiLayout.Text(Lang.T("推到外圈时检查实际推幅；若达不到当前阈值，请适当降低阈值。","Check actual travel at the outer ring; lower the threshold if your pad cannot reach it.")));
            var liveTimer=new System.Windows.Forms.Timer{Interval=150};liveTimer.Tick+=(_,_)=>{if(Visible)live.Text=movementStatus();};
            Shown+=(_,_)=>liveTimer.Start();FormClosed+=(_,_)=>liveTimer.Dispose();Disposed+=(_,_)=>liveTimer.Dispose();
        }
        var analog=UiLayout.Card(movement,Lang.T("原生摇杆移动","Analog stick movement"),Lang.T("保留全向移动，L3 可切换静步/奔跑。","Keep analog movement; L3 can toggle walk/run."));
        UiLayout.Check(analog,Lang.T("启用 L3 奔跑切换","Enable L3 walk/run toggle"),_enableRun,v=>_onRunChanged(v,_runThreshold,_autoRun,_gameDefaultRun,_returnWalk),
            Lang.T("使用该方案时，请关闭上方 WASD 移动；游戏内奔跑设为点击切换。","Disable WASD above for this scheme; set in-game sprint to tap-to-toggle."));

        }
        if(initialSection<0 || initialSection==3) {
        var assist=_pages[3].Stack;
        var cursor=UiLayout.Card(assist,Lang.T("光标辅助","Cursor helpers"));
        UiLayout.Check(cursor,Lang.T("左摇杆推开中央光标","Move cursor away with left stick"),_nudgeCursor,_onNudgeChanged,Lang.T("避免重生后的光标遮挡视野。","Keep the cursor out of view after respawning."));
        UiLayout.Check(cursor,Lang.T("单键切换光标模式","Single-button cursor toggle"),_cursorSwitchMode==1,_onCursorModeChanged,
            Lang.T("Xbox：有线 Home 键；DS：触摸板。关闭后使用自定义组合键。","Xbox: wired Home button; DS: touchpad. When off, use the configured combo."));
        var hold=UiLayout.Card(assist,Lang.T("长按与快捷交互","Hold actions"));
        bool proneOn=fastProne;
        var prone=UiLayout.Check(hold,Lang.T("长按 B 快速趴下","Hold B for fast prone"),proneOn,v=>{proneOn=v;_onProne(v,proneVal);});
        UiLayout.Slider(hold,Lang.T("趴下长按时间","Prone hold duration"),proneVal,100,1000,v=>v+" ms",v=>{proneVal=v;_onProne(proneOn,v);});
        UiLayout.Slider(hold,Lang.T("快速交互长按时间","Interact hold duration"),interactVal,100,1000,v=>v+" ms",v=>{interactVal=v;_onInteractHoldChanged(v);},Lang.T("开关及触发键在“键位 → 游戏动作”中设置。","Enable and choose its button under Keys → Game actions."));
        bool viewOn=_viewHoldTab; int viewMs=_viewHoldVal;
        UiLayout.Check(hold,Lang.T("长按视图键打开记分表","Hold View for scoreboard"),viewOn,v=>{viewOn=v;_onViewHoldChanged(v,viewMs);});
        UiLayout.Slider(hold,Lang.T("记分表长按时间","Scoreboard hold duration"),viewMs,100,1000,v=>v+" ms",v=>{viewMs=v;_onViewHoldChanged(viewOn,v);});
        UiLayout.Slider(hold,Lang.T("核弹组合长按时间","Nuke combo hold duration"),nukeVal,200,1200,v=>v+" ms",v=>{nukeVal=v;_onNukeHoldChanged(v);},Lang.T("组合开关在“键位 → 游戏动作”中设置。","Enable the combo under Keys → Game actions."));

        }
        if(initialSection<0 || initialSection==4) {
        var feedback=_pages[4].Stack;
        var feedbackCard=UiLayout.Card(feedback,Lang.T("震动与语音","Rumble & voice"),Lang.T("震动、语音和自定义播报全部免费开放。","Rumble, voice and custom prompts are all free."));
        if(_versionLevel>=2) {
            UiLayout.Action(feedbackCard,Lang.T("设置震动反馈 →","Configure rumble →"),onRumble,true);
            UiLayout.Action(feedbackCard,Lang.T("设置语音播报 →","Configure voice →"),onVoice);
        } else UiLayout.Add(feedbackCard,UiLayout.Text(Lang.T("手感和键位调节已开放；语音与震动需要 Pro。","Feel and key settings are open; voice and rumble require Pro.")));
        UiLayout.Action(feedbackCard,_versionLevel>=2?Lang.T("查看授权与支持作者 →","License & support →"):Lang.T("了解 Pro 与支持作者 →","About Pro & support →"),onUnlock);

        }
        if(initialSection<0 || initialSection==5) {
        var tools=_pages[5].Stack;
        var tests=UiLayout.Card(tools,Lang.T("测试与诊断","Tests & diagnostics"));
        UiLayout.Action(tests,Lang.T("按键与摇杆测试 →","Buttons & sticks test →"),onTestKeys);
        UiLayout.Action(tests,Lang.T("测试记录 →","Test log →"),onTestLog);
        UiLayout.Action(tests,Lang.T("驱动与连接诊断 →","Driver & connection diagnostics →"),onDiag);
        if(onReport!=null) UiLayout.Action(tests,Lang.T("完整诊断报告 →","Full diagnostic report →"),onReport);
        var config=UiLayout.Card(tools,Lang.T("配置与偏好","Configuration"));
        UiLayout.Action(config,Lang.T("导出配置 →","Export configuration →"),onExport);
        UiLayout.Action(config,Lang.T("导入配置 →","Import configuration →"),()=>onImport());
        UiLayout.Action(config,Lang.T("切换中文 / English →","Switch 中文 / English →"),()=>onLang());
        UiLayout.Action(config,Lang.T("使用引导 →","Setup guide →"),onGuide);
        UiLayout.Action(config,Lang.T("更新日志 →","Release notes →"),()=>OpenInline(new ChangelogForm(),Lang.T("更新日志","Release notes")));
        var advanced=UiLayout.Card(tools,Lang.T("维护与重置","Maintenance & reset"));
        UiLayout.Check(advanced,Lang.T("退出后打开清理工具","Open cleanup tool after exit"),_cleanDriversOnExit,_onCleanChanged,
            Lang.T("通常保持关闭。启用后退出会打开清理界面，仍需你选择范围并确认。","Usually leave off. After exit, the cleanup interface opens; you still choose and confirm the scope."));
        UiLayout.Action(advanced,Lang.T("恢复默认设置…","Restore defaults…"),()=>onReset());

        }
        if(initialSection<0 || initialSection==6) {
        var experiments=_pages[6].Stack;
        int gyroSensitivity=_gyroSens,gyroDzValue=_gyroDz,gyroSmValue=_gyroSm; bool gyroOn=_gyroEnabled,invX=_gyroInvX,invY=_gyroInvY;
        void SendGyro()=>_onGyroChanged(gyroOn,gyroSensitivity,gyroDzValue,gyroSmValue,invX,invY);
        var gyro=UiLayout.Card(experiments,Lang.T("体感瞄准 · 实验","Gyro aim · experimental"),Lang.T("需要带六轴传感器的 Sony 手柄。","Requires a Sony pad with a six-axis sensor."));
        UiLayout.Check(gyro,Lang.T("启用陀螺仪瞄准","Enable gyro aiming"),gyroOn,v=>{gyroOn=v;SendGyro();});
        UiLayout.Slider(gyro,Lang.T("体感灵敏度","Gyro sensitivity"),gyroSensitivity,20,300,v=>(v/100f).ToString("0.00")+"×",v=>{gyroSensitivity=v;SendGyro();});
        UiLayout.Slider(gyro,Lang.T("静止死区","Gyro deadzone"),gyroDzValue,0,3000,v=>v.ToString(),v=>{gyroDzValue=v;SendGyro();});
        UiLayout.Slider(gyro,Lang.T("平滑程度","Smoothing"),gyroSmValue,0,80,v=>v+"%",v=>{gyroSmValue=v;SendGyro();});
        UiLayout.Check(gyro,Lang.T("水平翻转","Invert horizontal"),invX,v=>{invX=v;SendGyro();});
        UiLayout.Check(gyro,Lang.T("垂直翻转","Invert vertical"),invY,v=>{invY=v;SendGyro();});
        var touch=UiLayout.Card(experiments,Lang.T("模拟触屏移动 · 实验","Touch movement · experimental"),Lang.T("用于模拟器的虚拟摇杆区域，需要管理员权限。","For emulator on-screen sticks; requires administrator privileges."));
        UiLayout.Check(touch,Lang.T("启用触屏滑动","Enable touch movement"),touchOn,v=>{touchOn=v;if(v)wasdOn=false;SendMovement();});
        UiLayout.Slider(touch,Lang.T("滑动半径","Slide radius"),touchRadius,40,300,v=>v+" px",v=>{touchRadius=v;SendMovement();});
        UiLayout.Slider(touch,Lang.T("触屏灵敏度","Touch sensitivity"),touchSensitivity,50,150,v=>v+"%",v=>{touchSensitivity=v;SendMovement();});
        var comboCard=UiLayout.Card(experiments,Lang.T("组合注入与原始报告","Combo injection & raw reports"));
        UiLayout.Check(comboCard,Lang.T("R3 同时发送 V + B","R3 sends V + B"),_expRsCombo,v=>{_expRsCombo=v;_onExpRsChanged(v);});
        UiLayout.Action(comboCard,Lang.T("查看原始 HID 报告 →","Raw HID report viewer →"),onHidProbe);

        }
        UiPreview.Attach(this);
        SelectSection(initialSection>=0?initialSection:0);
        Shown+=(_,_)=>{UiLayout.FitText(shell);};
    }

    private Action<Form,string>? _inlineHost;
    private void OpenInline(Form child,string title)
    {
        if(_inlineHost==null) throw new InvalidOperationException("设置页必须挂载到主窗口");
        _inlineHost(child,title);
    }
    internal void PrepareForStudio(int section,Action<Form,string> host)
    {
        _inlineHost=host;
        int index=Math.Clamp(section,0,_pages.Count-1);
        var page=_pages[index].Page;
        page.Parent=null;
        foreach(Control control in Controls.Cast<Control>().ToArray()) control.Dispose();
        Controls.Clear();page.Dock=DockStyle.Fill;page.Visible=true;
        page.Padding=new Padding(Dpi.Px(8),Dpi.Px(8),Dpi.Px(8),Dpi.Px(12));
        Controls.Add(page);MinimumSize=Size.Empty;MaximumSize=Size.Empty;
        AcceptButton=null;CancelButton=null;
        UiLayout.FitText(_pages[index].Stack);
    }

    private readonly List<(Panel Page,UiStack Stack)> _pages=new();
    private Label _sectionTitle=null!,_sectionHint=null!;
    private string[] SectionNames => new[] {
        Lang.T("手感与灵敏度","Feel & sensitivity"),Lang.T("键位设置","Key settings"),
        Lang.T("移动与奔跑","Move & sprint"),Lang.T("光标与交互","Cursor & holds"),
        Lang.T("震动与语音","Rumble & voice"),Lang.T("工具与配置","Tools & config"),
        Lang.T("实验性功能","Experimental")
    };
    public void SelectSection(int idx)
    {
        idx=Math.Clamp(idx,0,_pages.Count-1);
        for(int i=0;i<_pages.Count;i++) {
            _pages[i].Page.Visible=i==idx;
            Assets.StyleCapsule(_navBtns[i],i==idx,false);
        }
        _pages[idx].Page.BringToFront(); _pages[idx].Page.AutoScrollPosition=Point.Empty;
        _sectionTitle.Text=SectionNames[idx];
        _sectionHint.Text=Lang.T("设置 / ","Settings / ")+SectionNames[idx];
        UiLayout.FitText(_pages[idx].Stack);
    }
    private void RefreshSensHighlight(int idx)
    {for(int i=0;i<_sensBtns.Count;i++) Assets.StyleCapsule(_sensBtns[i],i==idx,false);}
    private void RefreshPackHighlight(string key)
    {foreach(var b in _packBtns) Assets.StyleCapsule(b,(string?)b.Tag==key,false);}
    private void PreviewInvalidate()=>_previewPanel.Invalidate();


    /// <summary>曲线预览：正方形画布内画 左（灰）/ 腰射（橙）/ 开镜（青）三条曲线，网格 25% 辅助线。</summary>
    private void DrawPreview(Graphics g, Size size)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        // 网格
        using var gridPen = new Pen(Color.FromArgb(42, 48, 60), 1f);
        for (int i = 1; i < 4; i++)
        {
            int x = size.Width * i / 4, y = size.Height * i / 4;
            g.DrawLine(gridPen, x, 0, x, size.Height);
            g.DrawLine(gridPen, 0, y, size.Width, y);
        }
        // 对角线参考（线性基准）
        using var diagPen = new Pen(Color.FromArgb(70, 78, 92), 1f);
        g.DrawLine(diagPen, 0, size.Height, size.Width, 0);

        var curves = _preview();
        if (curves is { Length: >= 3 })
        {
            DrawCurve(g, size, curves[0], Color.FromArgb(150, 160, 175), 1.5f); // 左摇杆 · 灰（细）
            DrawCurve(g, size, curves[1], Assets.PrimaryOrange, 2.5f);          // 腰射 · 橙（粗）
            DrawCurve(g, size, curves[2], Color.FromArgb(90, 200, 235), 2.5f);  // 开镜 · 青（粗）
        }
    }

    private static void DrawCurve(Graphics g, Size size, FeelCurve c, Color color, float width)
    {
        if (c == null || c.Points.Count < 2) return;
        using var pen = new Pen(color, width);
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        float lastX = 0f, lastY = 0f;
        for (int i = 0; i < c.Points.Count; i++)
        {
            float px = c.Points[i].X * size.Width;
            float py = size.Height - c.Points[i].Y * size.Height;
            if (i == 0) { lastX = px; lastY = py; continue; }
            path.AddLine(lastX, lastY, px, py);
            lastX = px; lastY = py;
        }
        g.DrawPath(pen, path);

        // 控制点小圆点
        using var dot = new SolidBrush(color);
        for (int i = 0; i < c.Points.Count; i++)
        {
            float px = c.Points[i].X * size.Width;
            float py = size.Height - c.Points[i].Y * size.Height;
            g.FillEllipse(dot, px - 2.5f, py - 2.5f, 5f, 5f);
        }
    }

    private static string VersionName(int v) => Lang.T("全部免费", "All free");
}
