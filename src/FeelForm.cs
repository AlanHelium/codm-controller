namespace CODMControllerProbe;

/// <summary>
/// 手感设置：左摇杆 / 右摇杆·腰射 / 右摇杆·开镜（按住 LT）三套独立的
/// 反死区 + 响应曲线 + 轴向灵敏度（高级版/Pro 功能形态，调试期全开放）。
/// - 画布：横轴=输入推幅 0~100%，纵轴=输出推幅 0~100%；曲线起点高度=反死区。
/// - 拖动中间控制点改变曲线形状；Catmull-Rom 平滑过点。
/// - 左摇杆：仅保留反死区（其余功能已按用户要求移除）。
/// - 右摇杆：腰射 / 开镜两条独立曲线（反死区 + 横纵灵敏度 + 加点删点）。
/// - 灵敏度档位不在本窗口：主页首页统一调节（免费版锁定中档，作用于右摇杆）。
/// - 快速趴下（长按 B 触发左 Ctrl）与摇杆选项卡同排，位于右开镜旁，随时可见。
/// - 长按 B 趴下触发时间：高级版可调（100~1000ms），免费版锁定默认值。
/// - 所有调整实时生效（直接修改传入的 FeelCurve 对象，转发立即采用新值），
///   每次改动通过 onChange 回调立即写入 AppData 配置文件（强杀/崩溃不丢）。
/// </summary>
public class FeelForm : Form
{
    private readonly FeelCurve[] _edit;   // [0]=左 [1]=右腰射 [2]=右开镜 —— 母版（出厂基准）
    private readonly FeelCurve[] _curves; // 显示/转发曲线（母版 × 当前档位系数，由 MainForm 派生）
    private readonly Action _onChange;    // 改动回调：MainForm 重派生转发 + 立即保存设置
    private readonly int _versionLevel;   // 0=免费版 1=高级版 2=Pro 版
    private readonly Action<int> _onHoldDelayChanged; // 长按 B 趴下触发时间改动回调
    private int _holdProneDelayMs;        // 当前长按阈值（100~1000ms）
    private readonly bool _enableFastProne;           // 快速趴下开关
    private readonly Action<bool> _onFastProneChanged; // 开关改动回调
    private readonly Action<float[]> _onFineTuneChanged; // 高级版：横纵微调写入 fine tune 记忆
    private float[] _fineTune = { 1f, 1f, 1f, 1f, 1f, 1f };
    private int _idx;                     // 当前编辑：0 左 / 1 右腰射 / 2 右开镜
    private int _dragPoint = -1;          // 正在拖动的控制点索引，-1=无

    private Button btnLeft = null!;
    private Button btnRight = null!;
    private Button btnRightAds = null!;
    private readonly List<Button> _sensBtns = new(); // 本窗口 10 档胶囊（高级/Pro）
    private Label _lblSensHint = null!;            // 档位区说明
    private int _sensLevel;                        // 当前档位（0-9 对应 1~10）
    private readonly Action<int> _onSensLevelChanged; // 档位切换回调（MainForm 应用+保存）
    private Label lblAntiVal = null!;
    private Label lblGainXVal = null!;
    private Label lblGainYVal = null!;
    private Label lblHoldVal = null!;
    private StudioSlider trkAnti = null!;
    private StudioSlider trkGainX = null!;
    private StudioSlider trkGainY = null!;
    private StudioSlider trkHold = null!;
    private CheckBox chkFastProne = null!;
    private CheckBox chkNudge = null!;
    private bool _nudgeCursor;
    private readonly int _outputHz;
    private readonly Action<int> _onOutputHzChanged = _ => { };
    private Action<bool> _onNudgeChanged = _ => { };
    private Panel canvas = null!;

    // Pro 区控件（字段化，供左/右摇杆切换显隐：左摇杆只留反死区）
    private Label lblAnti = null!, lblGainX = null!, lblGainY = null!;
    private Button btnAdd = null!, btnDel = null!;

    private const int Pad = 34; // 画布边距

    public FeelForm(FeelCurve editLeft, FeelCurve editRight, FeelCurve editAds,
                    FeelCurve dispLeft, FeelCurve dispRight, FeelCurve dispAds,
                    Action onChange,
                    int versionLevel, int holdProneDelayMs, Action<int> onHoldDelayChanged,
                    bool enableFastProne, Action<bool> onFastProneChanged,
                    Action<float[]> onFineTuneChanged,
                    int sensLevel, Action<int> onSensLevelChanged,
                    bool nudgeCursor, Action<bool> onNudgeChanged,
                    int outputHz, Action<int> onOutputHzChanged)
    {
        // 编辑对象 = 母版（出厂基准）；显示对象 = 转发曲线（母版 × 档位，MainForm 派生好）
        _edit = new[] { editLeft, editRight, editAds };
        _curves = new[] { dispLeft, dispRight, dispAds };
        _onChange = onChange;
        _versionLevel = versionLevel;
        _holdProneDelayMs = holdProneDelayMs;
        _onHoldDelayChanged = onHoldDelayChanged;
        _enableFastProne = enableFastProne;
        _onFastProneChanged = onFastProneChanged;
        _onFineTuneChanged = onFineTuneChanged;
        _sensLevel = sensLevel;
        _onSensLevelChanged = onSensLevelChanged;
        _nudgeCursor = nudgeCursor;
        _onNudgeChanged = onNudgeChanged;
        _outputHz = outputHz;
        _onOutputHzChanged = onOutputHzChanged;
        Dpi.Scale = Math.Max(1f, DeviceDpi / 96f);
        Text = Lang.T("手感设置 · 反死区 + 曲线精调（全版本开放）", "Feel · anti-deadzone + curve tuning (all tiers)");
        ClientSize = new Size(Math.Min(Dpi.Px(940),Screen.FromControl(this).WorkingArea.Width-Dpi.Px(40)),Math.Min(Dpi.Px(680),Screen.FromControl(this).WorkingArea.Height-Dpi.Px(80)));
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(Dpi.Px(740),Dpi.Px(500));
        StartPosition = FormStartPosition.CenterParent;
        Font = Assets.Ui(9.5F);
        Assets.Skin(this); // 奶油白背景 + 橘猫图标
        BuildLayout();
        Load += (_, _) => SwitchStick(0);
    }

    /// <summary>窗口移动到不同 DPI 显示器：重建全部布局（按新缩放因子等比缩放）。</summary>
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        Dpi.Scale = Math.Max(1f, DeviceDpi / 96f);
        SuspendLayout();
        Controls.Clear();
        BuildLayout();
        ResumeLayout();
        SwitchStick(_idx);
        Invalidate();
    }

private UiStack? _gainXBlock, _gainYBlock;
    private bool _loadingParameters;
    private void BuildLayout()
    {
        _sensBtns.Clear();
        var shell=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=3,Padding=new Padding(Dpi.Px(20))};
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,55)); shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,45));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize)); shell.RowStyles.Add(new RowStyle(SizeType.Percent,100)); shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(shell);
        var tabs=new FlowLayoutPanel {Dock=DockStyle.Top,WrapContents=true,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink};
        btnLeft=UiLayout.Choice(tabs,Lang.T("左摇杆","Left stick"),()=>SwitchStick(0));
        btnRight=UiLayout.Choice(tabs,Lang.T("右摇杆 · 腰射","Right · hip"),()=>SwitchStick(1));
        btnRightAds=UiLayout.Choice(tabs,Lang.T("右摇杆 · 开镜","Right · ADS"),()=>SwitchStick(2));
        shell.Controls.Add(tabs,0,0); shell.SetColumnSpan(tabs,2);
        canvas=new Panel {Dock=DockStyle.Fill,BackColor=Assets.CanvasDark,Margin=new Padding(0,0,Dpi.Px(18),0)};
        canvas.Paint+=CanvasPaint; canvas.MouseDown+=CanvasMouseDown; canvas.MouseMove+=CanvasMouseMove;
        canvas.MouseUp+=(_,_)=>{_dragPoint=-1;_onChange();}; canvas.Resize+=(_,_)=>canvas.Invalidate(); shell.Controls.Add(canvas,0,1);
        var viewport=new Panel {Dock=DockStyle.Fill,AutoScroll=true,Margin=Padding.Empty};
        var parameters=UiLayout.Stack(); viewport.Controls.Add(parameters); shell.Controls.Add(viewport,1,1);
        var response=UiLayout.Card(parameters,Lang.T("当前摇杆参数","Stick response"),Lang.T("曲线图横轴为输入，纵轴为输出。拖动控制点调整响应。","Input is horizontal; output is vertical. Drag control points to adjust the response."));
        UiStack Parameter(string name,int min,int max,int initial, out Label title,out Label number,out StudioSlider slider)
        {
            var block=UiLayout.Stack(); block.Margin=new Padding(0,Dpi.Px(6),0,Dpi.Px(10));
            title=UiLayout.Text(name); title.ForeColor=Assets.MainText; number=UiLayout.Text(""); number.ForeColor=Assets.PrimaryOrange;
            var row=new TableLayoutPanel {AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=2,RowCount=1,AutoSize=true,Dock=DockStyle.Top};
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.Controls.Add(title,0,0); row.Controls.Add(number,1,0); UiLayout.Add(block,row);
            slider=new StudioSlider {Minimum=min,Maximum=max,Value=Math.Clamp(initial,min,max),AutoSize=false,Height=Dpi.Px(34),TickStyle=TickStyle.None,AccessibleName=name};
            UiLayout.Add(block,slider); UiLayout.Add(response,block); return block;
        }
        Parameter(Lang.T("反死区","Anti-deadzone"),0,50,(int)(_edit[_idx].Anti*100),out lblAnti,out lblAntiVal,out trkAnti);
        trkAnti.ValueChanged+=(_,_)=>{if(_loadingParameters)return;_edit[_idx].Anti=trkAnti.Value/100f;lblAntiVal.Text=trkAnti.Value+"%";_onChange();canvas.Invalidate();};
        _gainXBlock=Parameter(Lang.T("横向灵敏度","Horizontal sensitivity"),0,150,(int)((_curves[_idx].GainX-.5f)*100),out lblGainX,out lblGainXVal,out trkGainX);
        trkGainX.ValueChanged+=(_,_)=>{if(_loadingParameters)return;_curves[_idx].GainX=.5f+trkGainX.Value/100f;lblGainXVal.Text=_curves[_idx].GainX.ToString("0.00")+"×";PushFineTune();_onChange();};
        _gainYBlock=Parameter(Lang.T("纵向灵敏度","Vertical sensitivity"),0,150,(int)((_curves[_idx].GainY-.5f)*100),out lblGainY,out lblGainYVal,out trkGainY);
        trkGainY.ValueChanged+=(_,_)=>{if(_loadingParameters)return;_curves[_idx].GainY=.5f+trkGainY.Value/100f;lblGainYVal.Text=_curves[_idx].GainY.ToString("0.00")+"×";PushFineTune();_onChange();};
        var edits=UiLayout.Buttons(response);
        btnAdd=UiLayout.Choice(edits,Lang.T("+ 加点","+ Add point"),()=>{_edit[_idx].AddPoint();_onChange();canvas.Invalidate();});
        btnDel=UiLayout.Choice(edits,Lang.T("删除点","Remove point"),()=>{_edit[_idx].RemovePoint();_onChange();canvas.Invalidate();});
        var rate=UiLayout.Card(parameters,Lang.T("输出刷新率","Output rate"));
        UiLayout.Slider(rate,Lang.T("虚拟手柄输出频率","Virtual pad output rate"),_outputHz,60,1000,v=>v+" Hz",_onOutputHzChanged,
            Lang.T("控制软件的输出节奏；实际输入速度仍取决于手柄和连接方式。","Controls output cadence; input speed depends on the pad and connection."),40);
        var footer=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Padding=new Padding(0,Dpi.Px(10),0,0)};
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.Controls.Add(UiLayout.Text(Lang.T("调整即时生效；灵敏度档位在上一级设置。","Changes apply immediately; tiers are in the parent settings.")),0,0);
        var done=new StudioButton {Text=Lang.T("完成","Done"),MinimumSize=new Size(Dpi.Px(100),Dpi.Px(40)),AutoSize=true,DialogResult=DialogResult.OK};
        Assets.StylePrimary(done); UiLayout.Responsive(done); footer.Controls.Add(done,1,0);
        shell.Controls.Add(footer,0,2); shell.SetColumnSpan(footer,2); AcceptButton=done; CancelButton=done;
        viewport.SizeChanged+=(_,_)=>UiLayout.FitText(parameters);
        Shown+=(_,_)=>UiLayout.FitText(shell);
    }


    private Button MakeBtn(string text, Point loc, Size size)
    {
        var b = new StudioButton
        {
            Text = text, Location = loc, Size = size,
            Font = Assets.Ui(9F),
            FlatStyle = FlatStyle.Flat,
        };
        Assets.StyleOutline(b);
        return b;
    }

    private void SwitchStick(int idx)
    {
        _loadingParameters = true;
        _idx = idx;

        void SetActive(Button b, bool active)
        {
            if (active) { b.BackColor = Assets.PrimaryOrange; b.ForeColor = Color.White; }
            else { b.BackColor = Assets.CardWhite; b.ForeColor = Assets.BorderText; }
        }
        SetActive(btnLeft, _idx == 0);
        SetActive(btnRight, _idx == 1);
        SetActive(btnRightAds, _idx == 2);
        var c = _curves[_idx];

        // 档位胶囊刷新
        for (int i = 0; i < _sensBtns.Count; i++)
            Assets.StyleCapsule(_sensBtns[i], _sensLevel == i, false);
        if (_lblSensHint != null) _lblSensHint.Text = $"{_sensLevel + 1} / 20";

        // 左摇杆：仅保留反死区（横纵灵敏度 / 加点删点隐藏）；右摇杆两条曲线：全部开放
        bool isLeft = _idx == 0;
        if (_gainXBlock != null) _gainXBlock.Visible = !isLeft;
        if (_gainYBlock != null) _gainYBlock.Visible = !isLeft;
        if (lblGainX != null) lblGainX.Visible = !isLeft;
        if (trkGainX != null) trkGainX.Visible = !isLeft;
        if (lblGainXVal != null) lblGainXVal.Visible = !isLeft;
        if (lblGainY != null) lblGainY.Visible = !isLeft;
        if (trkGainY != null) trkGainY.Visible = !isLeft;
        if (lblGainYVal != null) lblGainYVal.Visible = !isLeft;
        if (btnAdd != null) btnAdd.Visible = !isLeft;
        if (btnDel != null) btnDel.Visible = !isLeft;

        // 免费版：未创建任何编辑滑杆，只重绘画布（画布显示出厂基准×中档的缩放曲线）
        // 高级版：只有横纵灵敏度滑杆（无反死区）；Pro：全部滑杆
        // 反死区滑杆 = 母版起点（各档起点锚定一致）；横纵 = 显示曲线（含 fine tune 叠加）
        var edit = _edit[_idx];
        if (trkAnti != null)
        {
            trkAnti.Value = Math.Min(50, (int)MathF.Round(edit.Anti * 100f));
            lblAntiVal.Text = $"{trkAnti.Value}%";
        }
        if (trkGainX != null)
        {
            trkGainX.Value = Math.Clamp((int)MathF.Round((c.GainX - 0.50f) * 100f), 0, 150);
            lblGainXVal!.Text = $"{c.GainX:0.00}x";
        }
        if (trkGainY != null)
        {
            trkGainY.Value = Math.Clamp((int)MathF.Round((c.GainY - 0.50f) * 100f), 0, 150);
            lblGainYVal!.Text = $"{c.GainY:0.00}x";
        }
        canvas.Invalidate();
    }

    /// <summary>档位切换：回调 MainForm 应用档位（重建曲线+保存+主页同步），画布实时反映新档曲线。</summary>
    private void SetSensLevel(int level)
    {
        if (_sensLevel == level) return;
        _sensLevel = level;
        _onSensLevelChanged(level);
        for (int i = 0; i < _sensBtns.Count; i++)
            Assets.StyleCapsule(_sensBtns[i], _sensLevel == i, false);
        if (_lblSensHint != null) _lblSensHint.Text = $"{_sensLevel + 1} / 20";
        canvas.Invalidate(); // 曲线对象被 MainForm 就地重建，画布实时反映
    }

    /// <summary>高级版：把三条曲线的横纵灵敏度写入 fine tune 记忆（[0]=左X [1]=左Y [2]=右腰X [3]=右腰Y [4]=右开X [5]=右开Y）。</summary>
    private void PushFineTune()
    {
        _fineTune = new[]
        {
            _curves[0].GainX, _curves[0].GainY,
            _curves[1].GainX, _curves[1].GainY,
            _curves[2].GainX, _curves[2].GainY,
        };
        _onFineTuneChanged(_fineTune);
    }

    // ---- 画布 ----
    // 正方形绘图区（用户要求纵坐标看得更细）：边长取可用宽高较小者，居中放置
    private Rectangle PlotRect()
    {
        int side = Math.Min(canvas.Width - Dpi.Px(Pad) * 2, canvas.Height - Dpi.Px(52));
        side = Math.Max(side, Dpi.Px(200));
        int ox = (canvas.Width - side) / 2;
        int oy = Dpi.Px(12) + Math.Max(0, (canvas.Height - Dpi.Px(40) - side) / 2);
        return new Rectangle(ox, oy, side, side);
    }

    private void CanvasPaint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var r = PlotRect();

        // 网格（每 10%）
        using var gridPen = new Pen(Color.FromArgb(60, 71, 94), 1f);
        for (int i = 0; i <= 10; i++)
        {
            float x = r.Left + r.Width * i / 10f;
            float y = r.Top + r.Height * (1f - i / 10f);
            g.DrawLine(gridPen, x, r.Top, x, r.Bottom);
            g.DrawLine(gridPen, r.Left, y, r.Right, y);
        }

        // 参考对角线（线性无反死区）
        using var diagPen = new Pen(Color.FromArgb(90, 103, 128), 1f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
        g.DrawLine(diagPen, r.Left, r.Bottom, r.Right, r.Top);

        // 曲线（单摇杆 / 总曲线工作台中的当前子曲线）
        var curve = _curves[_idx];
        var pts = curve.Sample(101).Select(p => new PointF(r.Left + p.X * r.Width, r.Bottom - p.Y * r.Height)).ToArray();
        using var curvePen = new Pen(Color.FromArgb(96, 165, 250), 2.5f);
        g.DrawLines(curvePen, pts);

        // 控制点（带实时坐标标签）
        for (int i = 0; i < curve.Points.Count; i++)
        {
            var p = curve.Points[i];
            var c = new PointF(r.Left + p.X * r.Width, r.Bottom - p.Y * r.Height);
            var brush = i == _dragPoint ? Brushes.Orange : (i == 0 ? Brushes.Lime : Brushes.SteelBlue);
            g.FillEllipse(brush, c.X - 5, c.Y - 5, 10, 10);
            g.DrawEllipse(Pens.White, c.X - 5, c.Y - 5, 10, 10);

            // 坐标标签：(输入%, 输出%)，拖动时实时跟随
            // 相邻点标签按奇偶上下交替，避免同排互相压字；越界自动翻转到另一侧
            string label = $"({p.X * 100f:0}%, {p.Y * 100f:0}%)";
            using var labelFont = new Font("Consolas", 8F);
            var lsize = g.MeasureString(label, labelFont);
            float lx = c.X + 8;
            float ly = (i % 2 == 0) ? c.Y - lsize.Height - 6 : c.Y + 10;
            if (lx + lsize.Width > r.Right) lx = c.X - lsize.Width - 8; // 右侧越界 → 画到点左侧
            if (ly < r.Top) ly = c.Y + 10;                              // 上方越界 → 画到点下方
            if (ly + lsize.Height > r.Bottom) ly = c.Y - lsize.Height - 6; // 下方越界 → 画到点上方
            using var back = new SolidBrush(Color.FromArgb(205, 17, 24, 39));
            g.FillRectangle(back, lx - 2, ly - 1, lsize.Width + 4, lsize.Height + 2);
            g.DrawString(label, labelFont, i == _dragPoint ? Brushes.Orange : Brushes.LightGray, lx, ly);
        }

        // 坐标轴文字：y 轴 100% 放左上角（避开右上角数据点标签），x 轴右侧 100% 保留
        g.DrawString("0", SystemFonts.DefaultFont, Brushes.Gray, r.Left - 18, r.Bottom - 12);
        g.DrawString("100%", SystemFonts.DefaultFont, Brushes.Gray, r.Left - 30, r.Top - 6);
        g.DrawString("100%", SystemFonts.DefaultFont, Brushes.Gray, r.Right - 2, r.Bottom - 12);
        using var axisPen = new Pen(Color.FromArgb(148, 163, 184), 1.5f);
        g.DrawRectangle(axisPen, r);
    }

    private void CanvasMouseDown(object? sender, MouseEventArgs e)
    {
        var r = PlotRect();
        var curve = _curves[_idx];
        _dragPoint = -1;
        for (int i = 0; i < curve.Points.Count; i++)
        {
            var p = curve.Points[i];
            var c = new PointF(r.Left + p.X * r.Width, r.Bottom - p.Y * r.Height);
            if (Math.Abs(e.X - c.X) < 12 && Math.Abs(e.Y - c.Y) < 12) { _dragPoint = i; break; }
        }
        if (_dragPoint >= 0) { CanvasMouseMove(sender, e); canvas.Invalidate(); }
    }

    private void CanvasMouseMove(object? sender, MouseEventArgs e)
    {
        if (_dragPoint < 0) return;
        var r = PlotRect();
        var disp = _curves[_idx];   // 显示曲线（母版 × 当前档位）
        var edit = _edit[_idx];     // 母版（编辑对象）
        if (_dragPoint == 0) return; // 起点 X 固定 0，高度只由反死区滑杆控制
        float x = (e.X - r.Left) / (float)r.Width;
        float yDisp = (r.Bottom - e.Y) / (float)r.Height;   // 画布坐标（档位缩放后的显示值）
        // 反解回母版：显示 = 母版起点锚定压缩，反向推出母版高度（低档可用空间小 → 母版变化被放大）
        float s = _idx switch
        {
            0 => 1f,
            1 => FeelCurve.FreeSensScale(_sensLevel),
            _ => FeelCurve.AdsSensScale(_sensLevel),
        };
        float anti = edit.Points.Count > 0 ? edit.Points[0].Y : 0f;
        float span = Math.Max(s - anti, 0.1f);
        float yEdit = anti + (yDisp - anti) * (1f - anti) / span;
        edit.SetPoint(_dragPoint, new PointF(x, Math.Clamp(yEdit, 0f, 1f)));
        // 显示曲线同步该点 → 画布拖动实时跟手（松手后 _onChange 再按母版重派生校准）
        if (_dragPoint < disp.Points.Count)
            disp.SetPoint(_dragPoint, new PointF(x, Math.Clamp(yDisp, 0f, 1f)));
        canvas.Invalidate();
    }
}
