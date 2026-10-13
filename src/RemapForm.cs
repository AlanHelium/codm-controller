namespace CODMControllerProbe;

/// <summary>
/// 键位修正弹窗：部分第三方手柄会把 A/B/X/Y 在 XInput 层报错位。
/// 这里把游戏动作（跳跃/下蹲/换弹/切枪）重新绑定到你实际按下的键（支持 A/B/X/Y/LB/RB/LT/RT）；
/// 另可自定义"切换光标"组合键（按下任意组合录制，默认 L3+R3）、快速交互触发键（长按注入 F）、
/// 以及一键交换肩键/扳机（LT↔LB、RT↔RB）。
/// </summary>
public class RemapForm : Form
{
    public KeyMap Map;

    private readonly List<(string action, Label keyLbl, Button btn)> _rows = new();
    private int _captureIdx = -1;
    private bool _awaitRelease;
    private bool _hadPress;
    private ushort _pressedMask;
    private byte _pressedLt, _pressedRt;              // 组合录制期间扳机状态（扳机不占 wButtons 位）
    private readonly System.Windows.Forms.Timer _timer = null!;
    private readonly Label _hint;
    private readonly Action _onChange;          // 键位重设后立即保存
    private readonly Action<ushort> _onComboChange; // 组合键录制后保存
    private readonly bool _nukeEnabled;          // 核弹连杀（长按 LB+RB → J）开关
    private readonly Action<bool> _onNukeChange; // 核弹开关变更后保存
    private readonly bool _interactEnabled;      // 快速交互（长按触发键 → F）开关
    private readonly Action<bool> _onInteractEnableChange;
    private readonly Action<int> _onInteractKeyChange;
    private readonly bool _swapEnabled;          // 一键交换肩键/扳机（LT↔LB、RT↔RB）
    private readonly Action<bool> _onSwapChange;
    private int _meleeKey;                        // 近战/标点绑定键（默认 R3=0x0080，DS 原生近战+标点；可重绑其它键）
    private readonly Action<int> _onMeleeKeyChange;
    private int _interactKey;
    private ushort _combo;
    private Label _comboKeyLbl = null!;
    private Label _meleeKeyLbl = null!;
    private CheckBox _nukeChk = null!;
    private CheckBox _interactChk = null!;
    private CheckBox _swapChk = null!;

    public RemapForm(KeyMap map, Action onChange, ushort comboMask = 0x00C0, Action<ushort>? onComboChange = null,
                     bool nukeEnabled = true, Action<bool>? onNukeChange = null,
                     bool interactEnabled = false, Action<bool>? onInteractEnableChange = null,
                     int interactKey = 0x4000, Action<int>? onInteractKeyChange = null,
                     bool swapEnabled = false, Action<bool>? onSwapChange = null,
                     int meleeKey = 0x0080, Action<int>? onMeleeKeyChange = null,
                     bool autoCaptureMelee = false)
    {
        Map = map;
        _onChange = onChange;
        _onComboChange = onComboChange ?? (_ => { });
        _nukeEnabled = nukeEnabled;
        _onNukeChange = onNukeChange ?? (_ => { });
        _interactEnabled = interactEnabled;
        _onInteractEnableChange = onInteractEnableChange ?? (_ => { });
        _interactKey = interactKey;
        _onInteractKeyChange = onInteractKeyChange ?? (_ => { });
        _swapEnabled = swapEnabled;
        _onSwapChange = onSwapChange ?? (_ => { });
        _meleeKey = meleeKey == 0 ? 0x0080 : meleeKey;
        _onMeleeKeyChange = onMeleeKeyChange ?? (_ => { });
        _combo = comboMask == 0 ? (ushort)0x00C0 : comboMask;
        Dpi.Scale = Math.Max(1f, DeviceDpi / 96f);
        Text = Lang.T("键位修正", "Key Remap");
        // 高度自适应：内容总高（标题+提示+表格+按钮）在 DPI 放大后可能超过屏幕/父窗口可视区，
        // 此时压缩窗口高度并让内容区可滚动，保证「近战/标点」与底部按钮始终能滚到、不被裁掉。
        int contentH = Dpi.Px(692);
        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        int winH = Math.Min(contentH, Math.Max(Dpi.Px(420), wa.Height - Dpi.Px(24)));
        ClientSize = new Size(Dpi.Px(440), winH);
        MinimumSize = new Size(Dpi.Px(440), winH);
        MaximumSize = new Size(Dpi.Px(440), winH);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = Assets.Ui(10F);
        Assets.Skin(this); // 奶油白背景 + 橘猫图标

        var title = new Label
        {
            Text = Lang.T("键位修正", "Key Remap"), Location = new Point(Dpi.Px(20), Dpi.Px(12)), Size = new Size(Dpi.Px(400), Dpi.Px(30)),
            Font = Assets.Ui(15F, FontStyle.Bold),
        };

        _hint = new Label
        {
            Text = Lang.T("选择动作后点「重设」并按手柄键；已被其他动作占用的键会自动交换。键盘映射在上一页单独设置。", "Some pads report buttons swapped. Tap Set then press the pad button you want (A/B/X/Y/LB/RB/LT/RT/L3/R3/D-Pad/View/Option supported)."),
            Location = new Point(Dpi.Px(20), Dpi.Px(46)), Size = new Size(Dpi.Px(400), Dpi.Px(40)),
            ForeColor = Color.FromArgb(107, 114, 128), Font = Assets.Ui(9F),
        };

        var tbl = new TableLayoutPanel
        {
            Location = new Point(Dpi.Px(20), Dpi.Px(92)), Size = new Size(Dpi.Px(400), Dpi.Px(480)),
            ColumnCount = 3, RowCount = 12,
        };
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Dpi.Px(90)));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Dpi.Px(80)));

        var actions = new[] {
            (Lang.T("跳跃", "Jump"), map.Jump), (Lang.T("下蹲", "Crouch"), map.Crouch),
            (Lang.T("换弹", "Reload"), map.Reload), (Lang.T("切枪", "Switch"), map.Switch),
            (Lang.T("瞄准", "Aim"), map.Aim), (Lang.T("射击", "Shoot"), map.Shoot),
            (Lang.T("战术道具", "Tactical"), map.Tactical), (Lang.T("杀伤道具", "Lethal"), map.Lethal),
        };
        for (int i = 0; i < 8; i++)
        {
            tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, Dpi.Px(40)));
            var name = new Label { Text = actions[i].Item1, TextAlign = ContentAlignment.MiddleLeft, Font = Assets.Ui(10.5F, FontStyle.Bold) };
            var key = new Label { Text = Lang.T("绑定：", "Bound: ") + KeyMap.BitName(actions[i].Item2), TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(55, 65, 81) };
            var btn = new StudioButton { Text = Lang.T("重设", "Set"), Size = new Size(Dpi.Px(70), Dpi.Px(28)), Tag = i, FlatStyle = FlatStyle.Flat };
            Assets.StyleOutline(btn, 6);
            tbl.Controls.Add(name, 0, i);
            tbl.Controls.Add(key, 1, i);
            tbl.Controls.Add(btn, 2, i);
            _rows.Add((actions[i].Item1, key, btn));
            int idx = i;
            btn.Click += (_, _) => StartCapture(idx);
        }

        // 第 9 行：切换光标组合键（自定义录制）
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, Dpi.Px(40)));
        var comboName = new Label { Text = Lang.T("切换光标", "Cursor Toggle"), TextAlign = ContentAlignment.MiddleLeft, Font = Assets.Ui(10.5F, FontStyle.Bold) };
        _comboKeyLbl = new Label { Text = Lang.T("组合：", "Combo: ") + ComboName(_combo), TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(55, 65, 81) };
        var comboBtn = new StudioButton { Text = Lang.T("重设", "Set"), Size = new Size(Dpi.Px(70), Dpi.Px(28)), Tag = 8, FlatStyle = FlatStyle.Flat };
        Assets.StyleOutline(comboBtn, 6);
        tbl.Controls.Add(comboName, 0, 8);
        tbl.Controls.Add(_comboKeyLbl, 1, 8);
        tbl.Controls.Add(comboBtn, 2, 8);
        comboBtn.Click += (_, _) => StartCapture(8);

        // 第 10 行：核弹连杀组合键（长按 LB+RB → J，注入方式同 U 键直投；开启肩键/扳机交换后由物理 LT+RT 长按触发）。开关可勾选，组合固定。
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, Dpi.Px(40)));
        var nukeName = new Label { Text = Lang.T("核弹连杀", "Nuke Streak"), TextAlign = ContentAlignment.MiddleLeft, Font = Assets.Ui(10.5F, FontStyle.Bold) };
        _nukeChk = new StudioCheckBox
        {
            Text = Lang.T("长按 LB+RB 触发（键盘 J）", "Hold LB+RB (keyboard J)"),
            Checked = _nukeEnabled,
            AutoSize = true,
            Location = new Point(Dpi.Px(4), Dpi.Px(8)),
            ForeColor = Color.FromArgb(55, 65, 81),
            Font = Assets.Ui(9.5F),
        };
        _nukeChk.CheckedChanged += (_, _) => _onNukeChange(_nukeChk.Checked);
        tbl.Controls.Add(nukeName, 0, 9);
        tbl.Controls.Add(_nukeChk, 1, 9);

        // 第 11 行：快速交互（长按触发键 → F，同快速趴下规格）。开关 + 触发键录制（触发键名动态显示在勾选文本上）。
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, Dpi.Px(40)));
        var interactName = new Label { Text = Lang.T("快速交互", "Fast Interact"), TextAlign = ContentAlignment.MiddleLeft, Font = Assets.Ui(10.5F, FontStyle.Bold) };
        _interactChk = new StudioCheckBox
        {
            Text = Lang.T("长按 " + KeyMap.BitName(_interactKey) + " 触发 F（交互键）", "Hold " + KeyMap.BitName(_interactKey) + " to press F (interact)"),
            Checked = _interactEnabled,
            AutoSize = true,
            Location = new Point(Dpi.Px(4), Dpi.Px(8)),
            ForeColor = Color.FromArgb(55, 65, 81),
            Font = Assets.Ui(9.5F),
        };
        _interactChk.CheckedChanged += (_, _) => _onInteractEnableChange(_interactChk.Checked);
        var interactBtn = new StudioButton { Text = Lang.T("重设", "Set"), Size = new Size(Dpi.Px(70), Dpi.Px(28)), Tag = 9, FlatStyle = FlatStyle.Flat };
        Assets.StyleOutline(interactBtn, 6);
        tbl.Controls.Add(interactName, 0, 10);
        tbl.Controls.Add(_interactChk, 1, 10);
        tbl.Controls.Add(interactBtn, 2, 10);
        interactBtn.Click += (_, _) => StartCapture(9);

        // 第 12 行：一键交换肩键/扳机（LT↔LB、RT↔RB，正作同款设置）
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, Dpi.Px(40)));
        var swapName = new Label { Text = Lang.T("肩键/扳机", "Shoulders/Triggers"), TextAlign = ContentAlignment.MiddleLeft, Font = Assets.Ui(10.5F, FontStyle.Bold) };
        _swapChk = new StudioCheckBox
        {
            Text = Lang.T("一键交换 LT↔LB / RT↔RB", "Swap LT↔LB / RT↔RB"),
            Checked = _swapEnabled,
            AutoSize = true,
            Location = new Point(Dpi.Px(4), Dpi.Px(8)),
            ForeColor = Color.FromArgb(55, 65, 81),
            Font = Assets.Ui(9.5F),
        };
        _swapChk.CheckedChanged += (_, _) => _onSwapChange(_swapChk.Checked);
        tbl.Controls.Add(swapName, 0, 11);
        tbl.Controls.Add(_swapChk, 1, 11);

        // 第 13 行：近战/标点（DS 系 R3 原生即近战+标点同一键）。与其它动作行同规格：
        // 默认绑定 R3，可点「重设」换到其它键——换键后由新键输出 R3 位（游戏原生近战/标点），
        // 原 R3 位被释放可改作他用（如下蹲），不再双触发。
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, Dpi.Px(40)));
        var meleeName = new Label { Text = Lang.T("近战/标点", "Melee/Ping"), TextAlign = ContentAlignment.MiddleLeft, Font = Assets.Ui(10.5F, FontStyle.Bold) };
        _meleeKeyLbl = new Label { Text = Lang.T("绑定：", "Bound: ") + KeyMap.BitName(_meleeKey), TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(55, 65, 81) };
        var meleeBtn = new StudioButton { Text = Lang.T("重设", "Set"), Size = new Size(Dpi.Px(70), Dpi.Px(28)), Tag = 12, FlatStyle = FlatStyle.Flat };
        Assets.StyleOutline(meleeBtn, 6);
        tbl.Controls.Add(meleeName, 0, 12);
        tbl.Controls.Add(_meleeKeyLbl, 1, 12);
        tbl.Controls.Add(meleeBtn, 2, 12);
        meleeBtn.Click += (_, _) => StartCapture(12);

        var btnDefault = new StudioButton { Text = Lang.T("恢复默认", "Default"), Location = new Point(Dpi.Px(20), Dpi.Px(628)), Size = new Size(Dpi.Px(110), Dpi.Px(34)), FlatStyle = FlatStyle.Flat };
        Assets.StyleOutline(btnDefault);
        var btnDone = new StudioButton { Text = Lang.T("完成", "Done"), Location = new Point(Dpi.Px(310), Dpi.Px(628)), Size = new Size(Dpi.Px(110), Dpi.Px(34)), FlatStyle = FlatStyle.Flat };
        Assets.StylePrimary(btnDone);

        // 底部小猫装饰（设计稿元素；居中底部，避免遮挡按钮）
        var cat = new PictureBox
        {
            Image = Assets.CatHead(),
            SizeMode = PictureBoxSizeMode.Zoom,
            Location = new Point(Dpi.Px(190), Dpi.Px(636)), Size = new Size(Dpi.Px(60), Dpi.Px(40)),
            BackColor = Color.Transparent,
        };
        btnDefault.Click += (_, _) =>
        {
            Map.Jump = 0x1000; Map.Crouch = 0x2000; Map.Reload = 0x4000; Map.Switch = 0x8000;
            Map.Aim = KeyMap.LT; Map.Shoot = KeyMap.RT;
            Map.Tactical = 0x0100; Map.Lethal = 0x0200;
            _combo = 0x00C0;
            _interactKey = 0x4000;
            if (!_nukeChk.Checked) { _nukeChk.Checked = true; _onNukeChange(true); }
            if (_interactChk.Checked) { _interactChk.Checked = false; _onInteractEnableChange(false); }
            _meleeKey = 0x0080;
            _onMeleeKeyChange(_meleeKey);
            RefreshRows();
            _interactChk.Text = Lang.T("长按 " + KeyMap.BitName(_interactKey) + " 触发 F（交互键）", "Hold " + KeyMap.BitName(_interactKey) + " to press F (interact)");
            _hint.Text = Lang.T("已恢复默认：A=跳跃 B=下蹲 X=换弹 Y=切枪；瞄准=LT 射击=RT；战术=LB 杀伤=RB；切换光标 = L3+R3；快速交互触发键 = X。", "Restored defaults: A=Jump B=Crouch X=Reload Y=Switch; Aim=LT Shoot=RT; Tactical=LB Lethal=RB; Cursor toggle = L3+R3; Fast interact trigger = X.");
            _onChange(); // 恢复默认也立即保存
            _onComboChange(_combo);
            _onInteractKeyChange(_interactKey);
        };
        btnDone.Click += (_, _) =>
        {
            _timer.Stop();
            DialogResult = DialogResult.OK;
            Close();
        };

        // 内容全部放进可滚动面板：窗口高度不足时出现垂直滚动条，滚动即可看到近战/标点与底部按钮
        var scroll = new Panel { AutoScroll = true, Location = Point.Empty, Size = ClientSize, BackColor = Color.Transparent };
        scroll.Controls.Add(title);
        scroll.Controls.Add(_hint);
        scroll.Controls.Add(tbl);
        scroll.Controls.Add(btnDefault);
        scroll.Controls.Add(btnDone);
        scroll.Controls.Add(cat);
        Controls.Add(scroll);
        UiLayout.MappingDialog(this,title,_hint,tbl,btnDefault,btnDone);

        _timer = new System.Windows.Forms.Timer { Interval = 16 };
        _timer.Tick += (_, _) => PollCapture();
        // 设置页「近战/标点」行点「重设」打开本窗口时：打开后直接进入近战/标点录键，用户按下新键即完成绑定
        if (autoCaptureMelee)
            BeginInvoke(() => StartCapture(12));
    }

    private void StartCapture(int idx)
    {
        _captureIdx = idx;
        _awaitRelease = false;
        _hadPress = false;
        _pressedMask = 0;
        _pressedLt = 0; _pressedRt = 0;
        if (idx == 8)
        {
            _comboKeyLbl.Text = Lang.T("等待按键…", "Waiting…");
            _hint.Text = Lang.T("请同时按下你要用作「切换光标」的组合键（按住不放，再按下其它键），然后一起松开。至少两个键。", "Press and hold the combo you want for cursor toggle (at least two buttons), then release all together.");
        }
        else if (idx == 9)
        {
            _interactChk.Text = Lang.T("等待按键…", "Waiting…");
            _hint.Text = Lang.T("请按下要作为「快速交互」触发键的手柄按键（A/B/X/Y/LB/RB/LT/RT/L3/R3/方向键/视图/Option）。", "Press the pad button (A/B/X/Y/LB/RB/LT/RT/L3/R3/D-Pad/View/Option) to use as the fast-interact trigger.");
        }
        else if (idx == 12)
        {
            _meleeKeyLbl.Text = Lang.T("等待按键…", "Waiting…");
            _hint.Text = Lang.T("请按下要作为「近战/标点」的手柄按键（默认 R3——DS 系 R3 原生就是近战+标点；换键后由新键触发，R3 可改作他用）。", "Press the pad button (default R3 — DS R3 natively is melee+ping; after rebinding the new key triggers it and R3 is free for other uses).");
        }
        else
        {
            _rows[idx].keyLbl.Text = Lang.T("等待按键…", "Waiting…");
            _hint.Text = Lang.T($"请按下要绑定到「{_rows[idx].action}」的手柄按键（A/B/X/Y/LB/RB/LT/RT/L3/R3/方向键/视图/Option）。", $"Press the pad button (A/B/X/Y/LB/RB/LT/RT/L3/R3/D-Pad/View/Option) to bind to {_rows[idx].action}.");
        }
        _timer.Start();
    }

    private void PollCapture()
    {
        if (_captureIdx < 0) return;

        // XInput 优先；无 XInput 设备时走 Sony HID 通道（DS4/DualSense/DSE）或 Xbox HID 直读（One/Series）
        int slot = -1;
        for (int i = 0; i < 4; i++)
            if (XInputNative.XInputGetCapabilities(i, XInputNative.XINPUT_FLAG_GAMEPAD, out _) ==
                XInputNative.ERROR_SUCCESS) { slot = i; break; }
        if (slot >= 0)
        {
            if (XInputNative.XInputGetState(slot, out var st) != XInputNative.ERROR_SUCCESS) return;
            HandleCapture(st.Gamepad.wButtons, st.Gamepad.bLeftTrigger, st.Gamepad.bRightTrigger);
            return;
        }

        if (!SonyHidPad.IsOpen && !XboxHidPad.IsOpen)
        {
            if (!SonyHidPad.Open()) XboxHidPad.Open();
        }
        if (SonyHidPad.IsOpen)
        {
            if (SonyHidPad.Read(out var sg)) HandleCapture(sg.wButtons, sg.bLeftTrigger, sg.bRightTrigger);
            return;
        }
        if (XboxHidPad.IsOpen)
        {
            if (XboxHidPad.Read(out var xg)) HandleCapture(xg.wButtons, xg.bLeftTrigger, xg.bRightTrigger);
        }
    }

    private void HandleCapture(ushort buttons, byte lt, byte rt)
    {
        if (_captureIdx == 8)
        {
            // 组合键录制：等全部松开 → 聚合按下位（按住 L3 再按 R3 会聚合成两者）→ 全松后保存
            if (!_awaitRelease)
            {
                if (buttons == 0 && lt == 0 && rt == 0) _awaitRelease = true;
                return;
            }
            if (buttons != 0 || lt > 10 || rt > 10)
            {
                _pressedMask |= buttons;
                if (lt > 10) _pressedLt = lt;
                if (rt > 10) _pressedRt = rt;
                _hadPress = true;
                return;
            }
            if (_hadPress)
            {
                int count = PopCount(_pressedMask) + (_pressedLt > 10 ? 1 : 0) + (_pressedRt > 10 ? 1 : 0);
                if (count >= 2)
                {
                    _combo = _pressedMask;
                    _captureIdx = -1;
                    _timer.Stop();
                    RefreshRows();
                    _hint.Text = Lang.T("组合键已更新：", "Combo updated: ") + ComboName(_combo);
                    _onComboChange(_combo);
                }
                else
                {
                    _awaitRelease = false;
                    _hadPress = false;
                    _pressedMask = 0;
                    _pressedLt = 0; _pressedRt = 0;
                    _hint.Text = Lang.T("需要至少两个键的组合。请再试一次：按住几个键后一起松开。", "A combo needs at least two buttons. Try again: hold a few and release together.");
                }
            }
            return;
        }

        // 单键录制（A/B/X/Y/LB/RB/LT/RT/L3/R3/方向键/视图/Option，DPad 四向同样可绑）
        if (!_awaitRelease)
        {
            if (buttons == 0 && lt == 0 && rt == 0) _awaitRelease = true; // 等旧键松开
            return;
        }
        // 扳机优先：LT/RT 不占按钮位，单独判定；随后取按钮位（含 DPad 四向，单键动作同样适用）
        if (lt > 10) { ApplyCapture(KeyMap.LT); return; }
        if (rt > 10) { ApplyCapture(KeyMap.RT); return; }
        ushort pressed = buttons; // 保留全部按钮位：A/B/X/Y/LB/RB/L3/R3/方向键/View/Option 均可作为触发键
        if (pressed == 0) return;
        ApplyCapture(pressed);
    }

    private void ApplyCapture(int pressed)
    {
        switch (_captureIdx)
        {
            // 防撞（逻辑冲突修复）：新绑定值若已被其它动作占用，两动作自动交换绑定——
            // 例：把跳跃重绑到物理 □（0x4000）时，换弹默认也是 □，换弹自动改到跳跃原键（×），
            // 避免同一物理键同时触发两个动作（"× 键同时换弹+跳跃"）。
            case 0: Map.Jump = ResolveConflict(0, pressed); break;
            case 1: Map.Crouch = ResolveConflict(1, pressed); break;
            case 2: Map.Reload = ResolveConflict(2, pressed); break;
            case 3: Map.Switch = ResolveConflict(3, pressed); break;
            case 4: Map.Aim = ResolveConflict(4, pressed); break;
            case 5: Map.Shoot = ResolveConflict(5, pressed); break;
            case 6: Map.Tactical = ResolveConflict(6, pressed); break;
            case 7: Map.Lethal = ResolveConflict(7, pressed); break;
            case 9:
                _interactKey = pressed;
                _onInteractKeyChange(_interactKey);   // 触发键变更立即保存
                break;
            case 12:
                Map.AssignAction(8,pressed);
                _meleeKey = Map.MeleePing;
                _onMeleeKeyChange(_meleeKey);         // 近战/标点绑定键变更立即保存
                break;
        }

        _captureIdx = -1;
        _timer.Stop();
        RefreshRows();
        _hint.Text = Lang.T("已更新。再点「重设」可以继续调整其它动作。", "Updated. Tap Set again to adjust another action.");
        _onChange(); // 立即保存键位修正到配置文件
    }

    /// <summary>8 个动作当前绑定值（跳跃/下蹲/换弹/切枪/瞄准/射击/战术/杀伤）。</summary>
    private int ActionValue(int idx) => idx switch
    {
        0 => Map.Jump,
        1 => Map.Crouch,
        2 => Map.Reload,
        3 => Map.Switch,
        4 => Map.Aim,
        5 => Map.Shoot,
        6 => Map.Tactical,
        7 => Map.Lethal,
        _ => 0,
    };

    private void SetAction(int idx, int bind)
    {
        switch (idx)
        {
            case 0: Map.Jump = bind; break;
            case 1: Map.Crouch = bind; break;
            case 2: Map.Reload = bind; break;
            case 3: Map.Switch = bind; break;
            case 4: Map.Aim = bind; break;
            case 5: Map.Shoot = bind; break;
            case 6: Map.Tactical = bind; break;
            case 7: Map.Lethal = bind; break;
        }
    }

    /// <summary>防撞：新绑定值已被其它动作占用 → 该动作交换到本动作原键（保证一键一动作）。</summary>
    private int ResolveConflict(int actionIdx, int newBind)
    {
        Map.AssignAction(actionIdx,newBind);
        _meleeKey=Map.MeleePing;
        return newBind;
    }

    private void RefreshRows()
    {
        _meleeKey=Map.MeleePing;
        var bits = new[] { Map.Jump, Map.Crouch, Map.Reload, Map.Switch, Map.Aim, Map.Shoot, Map.Tactical, Map.Lethal };
        for (int i = 0; i < 8; i++)
            _rows[i].keyLbl.Text = Lang.T("绑定：", "Bound: ") + KeyMap.BitName(bits[i]);
        _comboKeyLbl.Text = Lang.T("组合：", "Combo: ") + ComboName(_combo);
        _interactChk.Text = Lang.T("长按 " + KeyMap.BitName(_interactKey) + " 触发 F（交互键）", "Hold " + KeyMap.BitName(_interactKey) + " to press F (interact)");
        _meleeKeyLbl.Text = Lang.T("绑定：", "Bound: ") + KeyMap.BitName(_meleeKey);
    }

    private static int PopCount(ushort v)
    {
        int c = 0;
        while (v != 0) { v &= (ushort)(v - 1); c++; }
        return c;
    }

    /// <summary>组合键位掩码 → 可读名字（如 "L3 + R3"）。</summary>
    public static string ComboName(ushort mask)
    {
        var parts = new List<string>();
        (ushort bit, string name)[] table =
        {
            (0x0001, "↑"), (0x0002, "↓"), (0x0004, "←"), (0x0008, "→"),
            (0x0010, "Option"), (0x0020, "View"),
            (0x0040, "L3"), (0x0080, "R3"),
            (0x0100, "LB"), (0x0200, "RB"),
            (0x0400, "Logo"),
            (0x1000, "A"), (0x2000, "B"), (0x4000, "X"), (0x8000, "Y"),
        };
        foreach (var (bit, name) in table)
            if ((mask & bit) != 0) parts.Add(name);
        return parts.Count == 0 ? "—" : string.Join(" + ", parts);
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing){_timer?.Stop();_timer?.Dispose();}
        base.Dispose(disposing);
    }
}
