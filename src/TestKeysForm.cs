using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CODMControllerProbe;

/// <summary>
/// 按键测试窗口：实时读取实体手柄（XInput 原始输入）——16 个按键按下即点亮，
/// 摇杆/扳机显示实时数值，帮助用户确认手柄每个键是否正常（本机版 Gamepad Tester）。
/// 纯读取，不注入任何输入，与转换主流程互不干扰。
/// </summary>
public class TestKeysForm : Form
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    private readonly Dictionary<string, Button> _keys = new();
    private Label _state = null!;
    private Label _diag = null!;
    private TextBox _logBox = null!;
    private readonly Label[] _vals = new Label[6];
    private int _slot = -1;
    private bool _ownSony;  // 测试窗口自己打开了 Sony 通道（关闭窗口时由本窗口负责关闭）
    private bool _ownXbox;  // 测试窗口自己打开了 Xbox HID 通道

    public TestKeysForm()
    {
        Dpi.Scale = Math.Max(1f, DeviceDpi / 96f);
        Text = Lang.T("按键测试", "Test Keys");
        ClientSize = new Size(Dpi.Px(560), Dpi.Px(520));
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = Assets.CreamBg;
        Font = Assets.Ui(9.5F);
        StartPosition = FormStartPosition.CenterParent;

        var cap = new Label
        {
            Text = Lang.T("实时读取实体手柄（XInput）原始输入：按下任意键看是否点亮，摇杆 / 扳机看数值。", "Live raw input from the physical pad (XInput): press keys to see them light up, watch stick / trigger values."),
            Location = new Point(Dpi.Px(20), Dpi.Px(14)), Size = new Size(Dpi.Px(520), Dpi.Px(22)),
            ForeColor = Assets.SubText, Font = Assets.Ui(9F),
        };
        Controls.Add(cap);

        string[,] grid =
        {
            { "A", "B", "X", "Y" },
            { "LB", "RB", "L3", "R3" },
            { "LT", "RT", "Start", "Back" },
            { Lang.T("上", "Up"), Lang.T("下", "Down"), Lang.T("左", "Left"), Lang.T("右", "Right") }, // DPad
        };
        for (int r = 0; r < 4; r++)
        for (int c = 0; c < 4; c++)
        {
            var b = new StudioButton
            {
                Text = grid[r, c],
                Location = new Point(Dpi.Px(20 + c * 128), Dpi.Px(44 + r * 56)), Size = new Size(Dpi.Px(120), Dpi.Px(44)),
                FlatStyle = FlatStyle.Flat, Font = Assets.Ui(9.5F, FontStyle.Bold),
            };
            Assets.StyleCapsule(b, false, true); // 灰色描边起步
            _keys[grid[r, c]] = b;
            Controls.Add(b);
        }

        _state = new Label
        {
            Text = Lang.T("未连接手柄…（请插上你的 Xbox 手柄）", "No pad connected… (plug in your Xbox pad)"), Location = new Point(Dpi.Px(20), Dpi.Px(284)), Size = new Size(Dpi.Px(520), Dpi.Px(22)),
            ForeColor = Assets.SubText, Font = Assets.Ui(9F),
        };
        Controls.Add(_state);

        _diag = new Label
        {
            Text = "", Location = new Point(Dpi.Px(20), Dpi.Px(366)), Size = new Size(Dpi.Px(520), Dpi.Px(18)),
            ForeColor = Assets.WarnOrange, Font = Assets.Ui(7.5F),
        };
        Controls.Add(_diag);

        var logCap = new Label
        {
            Text = Lang.T("按键日志（按完键后 Ctrl+A 全选复制，发给我即可）", "Button log (press keys, then Ctrl+A to copy all, send to me)"),
            Location = new Point(Dpi.Px(20), Dpi.Px(386)), Size = new Size(Dpi.Px(520), Dpi.Px(18)),
            ForeColor = Assets.SubText, Font = Assets.Ui(8F, FontStyle.Bold),
        };
        Controls.Add(logCap);

        _logBox = new TextBox
        {
            ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical,
            Location = new Point(Dpi.Px(20), Dpi.Px(404)), Size = new Size(Dpi.Px(520), Dpi.Px(100)),
            BackColor = Color.FromArgb(24, 24, 28), ForeColor = Color.FromArgb(220, 220, 225),
            Font = new Font("Consolas", 7.5F), BorderStyle = BorderStyle.FixedSingle,
        };
        Controls.Add(_logBox);

        string[] names = { Lang.T("左摇杆 X", "L-Stick X"), Lang.T("左摇杆 Y", "L-Stick Y"), Lang.T("右摇杆 X", "R-Stick X"), Lang.T("右摇杆 Y", "R-Stick Y"), "LT", "RT" };
        for (int i = 0; i < 6; i++)
        {
            var lab = new Label
            {
                Text = names[i], Location = new Point(Dpi.Px(20 + (i % 4) * 140), Dpi.Px(i < 4 ? 318 : 348)), Size = new Size(Dpi.Px(72), Dpi.Px(20)),
                ForeColor = Assets.SubText, Font = Assets.Ui(8.5F),
            };
            Controls.Add(lab);
            _vals[i] = new Label
            {
                Text = "—", Location = new Point(Dpi.Px(92 + (i % 4) * 140), Dpi.Px(i < 4 ? 318 : 348)), Size = new Size(Dpi.Px(64), Dpi.Px(20)),
                ForeColor = Assets.WarnOrange, Font = Assets.Ui(9F, FontStyle.Bold), TextAlign = ContentAlignment.MiddleRight,
            };
            Controls.Add(_vals[i]);
        }

        _timer.Tick += (_, _) => Poll();
        _timer.Start();
        Shown += (_, _) => Poll();
        FormClosed += (_, _) =>
        {
            _timer.Stop();
            // 只关闭"本窗口自己打开"的通道；主流程已连接的 Sony/Xbox 通道继续归主流程管理
            if (_slot == -2 && _ownSony) SonyHidPad.Close();
            if (_slot == -3 && _ownXbox) XboxHidPad.Close();
        };
    }

    private void Poll()
    {
        if(UiPreview.Enabled)return;
        // 刷新诊断日志面板（Sony 通道数据；Sony 通道未激活时无内容）
        try
        {
            var diag = SonyHidPad.DrainDiagLines();
            if (diag.Length > 0)
            {
                _logBox.AppendText(diag);
                // 限制行数，防止日志无限增长
                if (_logBox.TextLength > 24000)
                    _logBox.Text = _logBox.Text.Substring(_logBox.TextLength - 24000);
                _logBox.SelectionStart = _logBox.TextLength;
                _logBox.ScrollToCaret();
            }
        }
        catch { /* 日志刷新失败不影响主流程 */ }

        if (_slot < 0)
        {
            // 优先接管主流程已经打开的通道（主界面绿色 = Sony/Xbox HID 已在转发）
            if (SonyHidPad.IsOpen) { _slot = -2; }
            else if (XboxHidPad.IsOpen) { _slot = -3; }
            else
            {
                for (int i = 0; i < 4; i++)
                    if (XInputNative.XInputGetCapabilities(i, XInputNative.XINPUT_FLAG_GAMEPAD, out _) ==
                        XInputNative.ERROR_SUCCESS) { _slot = i; break; }
                if (_slot < 0 && SonyHidPad.Open()) { _slot = -2; _ownSony = true; }
                if (_slot < 0 && XboxHidPad.Open()) { _slot = -3; _ownXbox = true; }
                if (_slot < 0)
                {
                    _state.Text = Lang.T("未连接手柄…（请插上你的手柄）", "No pad connected… (plug in your pad)");
                    _state.ForeColor = Assets.SubText;
                    return;
                }
            }
        }

        if (_slot == -2)
        {
            if (!SonyHidPad.IsOpen)
            {
                ClearInput();
                _slot = -1;
                _state.Text = Lang.T("手柄已断开，等待重连…", "Pad disconnected, waiting to reconnect…");
                _state.ForeColor = Assets.ErrRed;
                return;
            }
            if (!SonyHidPad.Read(out var sg))
            { ClearInput(); _state.Text = "等待有效输入报告…"; _state.ForeColor = Assets.SubText; return; } // 尚无数据（蓝牙唤醒中），空转
            _state.Text = Lang.T($"手柄已连接（{SonyHidPad.KindName()} · Sony HID）· 实时读取中", $"Pad connected ({SonyHidPad.KindName()} · Sony HID) · live reading");
            _state.ForeColor = Assets.OkGreen;
            _diag.Text = "RAW " + (SonyHidPad.LastReportHex ?? "") + "  |  " + (SonyHidPad.DevicePath ?? "");
            ShowState(sg);
            return;
        }

        if (_slot == -3)
        {
            if (!XboxHidPad.IsOpen)
            {
                ClearInput();
                _slot = -1;
                _state.Text = Lang.T("手柄已断开，等待重连…", "Pad disconnected, waiting to reconnect…");
                _state.ForeColor = Assets.ErrRed;
                return;
            }
            if (!XboxHidPad.Read(out var xg))
            { ClearInput(); return; }
            _state.Text = Lang.T($"手柄已连接（{XboxHidPad.KindName()} · Xbox HID）· 实时读取中", $"Pad connected ({XboxHidPad.KindName()} · Xbox HID) · live reading");
            _state.ForeColor = Assets.OkGreen;
            ShowState(xg);
            return;
        }

        if (XInputNative.XInputGetState(_slot, out var st) != XInputNative.ERROR_SUCCESS)
        {
            ClearInput();
            _slot = -1; // 拔插后重扫
            _state.Text = Lang.T("手柄已断开，等待重连…", "Pad disconnected, waiting to reconnect…");
            _state.ForeColor = Assets.ErrRed;
            return;
        }
        var g = st.Gamepad;
        _state.Text = Lang.T($"手柄已连接（XInput 槽位 {_slot}）· 实时读取中", $"Pad connected (XInput slot {_slot}) · live reading");
        _state.ForeColor = Assets.OkGreen;
        ShowState(g);
    }

    /// <summary>用统一手柄状态刷新按键/轴显示（XInput 与 Sony HID 通道共用）。</summary>
    private void ShowState(XInputNative.XInputGamepad g)
    {
        var b = (XInputNative.GamepadButtons)g.wButtons;

        Set(_keys["A"], b.HasFlag(XInputNative.GamepadButtons.A));
        Set(_keys["B"], b.HasFlag(XInputNative.GamepadButtons.B));
        Set(_keys["X"], b.HasFlag(XInputNative.GamepadButtons.X));
        Set(_keys["Y"], b.HasFlag(XInputNative.GamepadButtons.Y));
        Set(_keys["LB"], b.HasFlag(XInputNative.GamepadButtons.LeftShoulder));
        Set(_keys["RB"], b.HasFlag(XInputNative.GamepadButtons.RightShoulder));
        Set(_keys["L3"], b.HasFlag(XInputNative.GamepadButtons.LeftThumb));
        Set(_keys["R3"], b.HasFlag(XInputNative.GamepadButtons.RightThumb));
        Set(_keys["Start"], b.HasFlag(XInputNative.GamepadButtons.Start));
        Set(_keys["Back"], b.HasFlag(XInputNative.GamepadButtons.Back));
        Set(_keys["上"], b.HasFlag(XInputNative.GamepadButtons.DPadUp));
        Set(_keys["下"], b.HasFlag(XInputNative.GamepadButtons.DPadDown));
        Set(_keys["左"], b.HasFlag(XInputNative.GamepadButtons.DPadLeft));
        Set(_keys["右"], b.HasFlag(XInputNative.GamepadButtons.DPadRight));
        Set(_keys["LT"], g.bLeftTrigger > 30);
        Set(_keys["RT"], g.bRightTrigger > 30);

        _vals[0].Text = Pct(g.sThumbLX) + "%";
        _vals[1].Text = Pct(g.sThumbLY) + "%";
        _vals[2].Text = Pct(g.sThumbRX) + "%";
        _vals[3].Text = Pct(g.sThumbRY) + "%";
        _vals[4].Text = Math.Round(g.bLeftTrigger / 255f * 100f) + "%";
        _vals[5].Text = Math.Round(g.bRightTrigger / 255f * 100f) + "%";
    }

    private void ClearInput()
    {
        foreach (var button in _keys.Values) Set(button, false);
        foreach (var value in _vals) value.Text = "—";
        _diag.Text = "";
    }

    private static void Set(Button btn, bool down)
        => Assets.StyleCapsule(btn, down, !down);

    /// <summary>摇杆 -32768~32767 → 0~100%（中心 50%）。</summary>
    private static string Pct(short v)
    {
        float p = (v + 32768f) / 65535f * 100f;
        return Math.Round(Math.Clamp(p, 0f, 100f)).ToString("0");
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing){_timer?.Stop();_timer?.Dispose();}
        base.Dispose(disposing);
    }
}
