using System;
using System.Drawing;
using System.Windows.Forms;

namespace CODMControllerProbe;

/// <summary>
/// 「切换武器射击模式」触发键录制窗口：
/// 单键长按（A/B/X/Y/LB/RB/LT/RT/L3/R3/方向键/视图/Option/Home）或双键组合（两键同按）。
/// 读取通道同键位修正：XInput slot → Sony HID → Xbox HID，录完立即回调保存。
/// 双键组合只支持按钮位（扳机不占 wButtons 位）；组合默认 0=未设置，避免与切光标 L3+R3 / 核弹 LB+RB 撞车。
/// </summary>
public sealed class FireModeKeyForm : Form
{
    private readonly bool _initialCombo;
    private readonly int _initialKey;
    private readonly ushort _initialMask;
    private readonly Action<bool, int, ushort> _onRecorded;
    private readonly Label _hint;
    private readonly Button _btnSingle, _btnCombo;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    private int _captureMode = -1;      // 0=单键 1=组合；-1=空闲
    private ushort _pressedBits;        // 组合录制：聚合按下位
    private bool _allReleased;          // 已等待全部松开（准备状态）

    public FireModeKeyForm(bool initialCombo, int initialKey, ushort initialMask, Action<bool, int, ushort> onRecorded)
    {
        _initialCombo = initialCombo;
        _initialKey = initialKey;
        _initialMask = initialMask;
        _onRecorded = onRecorded;
        Dpi.Scale = Math.Max(1f, DeviceDpi / 96f);
        Text = Lang.T("录制触发键 · 切换武器射击模式", "Record trigger · Swap fire mode");
        ClientSize = new Size(Dpi.Px(430), Dpi.Px(230));
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = Assets.Ui(10F);
        Assets.Skin(this);

        var title = new Label
        {
            Text = Lang.T("选择「切换武器射击模式」的触发方式", "Choose the trigger for swapping weapon fire mode"),
            Location = new Point(Dpi.Px(20), Dpi.Px(14)), Size = new Size(Dpi.Px(390), Dpi.Px(26)),
            Font = Assets.Ui(13F, FontStyle.Bold),
        };
        _hint = new Label
        {
            Text = Lang.T("单键：按住触发键超过设定时长注入一次 B。\n双键：两键同时按住超过设定时长注入一次 B。\n当前：" + Describe(),
                   "Single: hold one key past the duration to inject B once.\nCombo: hold two keys together past the duration to inject B once.\nCurrent: " + Describe()),
            Location = new Point(Dpi.Px(20), Dpi.Px(50)), Size = new Size(Dpi.Px(390), Dpi.Px(52)),
            ForeColor = Color.FromArgb(107, 114, 128), Font = Assets.Ui(9F),
        };
        _btnSingle = new StudioButton
        {
            Text = Lang.T("录制单键触发", "Record single key"), FlatStyle = FlatStyle.Flat,
            Location = new Point(Dpi.Px(20), Dpi.Px(112)), Size = new Size(Dpi.Px(185), Dpi.Px(40)),
        };
        Assets.StylePrimary(_btnSingle);
        _btnSingle.Click += (_, _) => StartCapture(0);
        _btnCombo = new StudioButton
        {
            Text = Lang.T("录制双键组合", "Record two-key combo"), FlatStyle = FlatStyle.Flat,
            Location = new Point(Dpi.Px(225), Dpi.Px(112)), Size = new Size(Dpi.Px(185), Dpi.Px(40)),
        };
        Assets.StylePrimary(_btnCombo);
        _btnCombo.Click += (_, _) => StartCapture(1);

        var btnDone = new StudioButton
        {
            Text = Lang.T("完成", "Done"), FlatStyle = FlatStyle.Flat,
            Location = new Point(Dpi.Px(310), Dpi.Px(172)), Size = new Size(Dpi.Px(100), Dpi.Px(34)),
            DialogResult = DialogResult.OK,
        };
        Assets.StyleOutline(btnDone, 6);

        Controls.Add(title);
        Controls.Add(_hint);
        Controls.Add(_btnSingle);
        Controls.Add(_btnCombo);
        Controls.Add(btnDone);

        _timer.Tick += (_, _) => PollCapture();
        FormClosed += (_, _) => _timer.Stop();
    }

    private string Describe() => _initialCombo
        ? (_initialMask == 0 ? Lang.T("双键组合（未设置）", "two-key combo (not set)") : ComboMaskName(_initialMask))
        : KeyMap.BitName(_initialKey);

    private void StartCapture(int mode)
    {
        _captureMode = mode;
        _pressedBits = 0;
        _allReleased = false;
        _hint.Text = mode == 0
            ? Lang.T("请按下要作为触发键的手柄键（含扳机；Esc 无法用于手柄，改按「完成」取消）…", "Press the pad key to use as trigger (triggers included)…")
            : Lang.T("请同时按住两个手柄键作为组合（两键同按超过 2 秒后自动保存；只支持按钮位，不含扳机）…", "Hold two pad buttons together as the combo (auto-saves after 2s; button bits only, no triggers)…");
        _hint.ForeColor = Color.FromArgb(249, 115, 22);
        _timer.Start();
    }

    private void PollCapture()
    {
        if (_captureMode < 0) return;
        ushort buttons = 0;
        byte lt = 0, rt = 0;
        bool got = false;

        // XInput slot（与键位修正一致：4 槽内找第一个在线）
        for (int slot = 0; slot < 4; slot++)
        {
            if (XInputNative.XInputGetCapabilities(slot, XInputNative.XINPUT_FLAG_GAMEPAD, out _) !=
                XInputNative.ERROR_SUCCESS) continue;
            if (XInputNative.XInputGetState(slot, out var st) == XInputNative.ERROR_SUCCESS)
            {
                buttons |= st.Gamepad.wButtons;
                lt = Math.Max(lt, st.Gamepad.bLeftTrigger);
                rt = Math.Max(rt, st.Gamepad.bRightTrigger);
                got = true;
            }
            break;   // 只读第一个在线 slot（与键位修正一致）
        }
        if (!got && !SonyHidPad.IsOpen && !XboxHidPad.IsOpen)
        {
            if (SonyHidPad.Open()) got = true;
            else if (XboxHidPad.Open()) got = true;
        }
        if (SonyHidPad.IsOpen && SonyHidPad.Read(out var sg)) { buttons |= sg.wButtons; lt = Math.Max(lt, sg.bLeftTrigger); rt = Math.Max(rt, sg.bRightTrigger); got = true; }
        if (XboxHidPad.IsOpen && XboxHidPad.Read(out var xg)) { buttons |= xg.wButtons; lt = Math.Max(lt, xg.bLeftTrigger); rt = Math.Max(rt, xg.bRightTrigger); got = true; }
        if (!got) return;

        bool anyDown = buttons != 0 || lt > 30 || rt > 30;
        if (!_allReleased)
        {
            if (!anyDown) _allReleased = true;   // 等全部松开后开始录
            return;
        }

        if (_captureMode == 0)
        {
            // 单键：按下位（含扳机虚拟位）→ 保存
            if (buttons != 0 || lt > 30 || rt > 30)
            {
                int key = 0;
                for (int bit = 0x0001; bit <= 0x8000; bit <<= 1)
                    if ((buttons & bit) != 0) { key = bit; break; }
                if (lt > 30) key = KeyMap.LT;
                else if (rt > 30) key = KeyMap.RT;
                if (key != 0)
                {
                    EndCapture(combo: false, key: key, mask: 0);
                    return;
                }
            }
            return;
        }

        // 组合：聚合按钮位；两键同按满 2 秒且 >=2 位 → 保存
        if (anyDown) _pressedBits |= buttons;
        if (_pressedBits != 0 && (_pressedBits & (_pressedBits - 1)) != 0 && !anyDown)
        {
            EndCapture(combo: true, key: 0, mask: _pressedBits);
        }
    }

    private void EndCapture(bool combo, int key, ushort mask)
    {
        _timer.Stop();
        _captureMode = -1;
        _onRecorded(combo, key, mask);
        _hint.Text = combo
            ? Lang.T("已录制组合：" + ComboMaskName(mask), "Recorded combo: " + ComboMaskName(mask))
            : Lang.T("已录制单键：" + KeyMap.BitName(key), "Recorded single key: " + KeyMap.BitName(key));
        _hint.ForeColor = Color.FromArgb(5, 150, 105);
    }

    /// <summary>组合掩码显示名（按钮位 → 名称，按位拼接）。</summary>
    private static string ComboMaskName(ushort mask) => KeyMap.ComboName(mask);
    protected override void Dispose(bool disposing)
    {
        if(disposing){_timer?.Stop();_timer?.Dispose();}
        base.Dispose(disposing);
    }
}
