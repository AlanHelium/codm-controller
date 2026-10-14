using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CODMControllerProbe;

/// <summary>
/// 按键映射窗口：手柄按键（A/B/X/Y/LB/RB/LT/RT/L3/R3/方向键/视图/Option/Home）→ 键盘任意键。
/// 这是「手柄键位设置」下的专门映射区：绑定后该手柄键按下时向游戏注入对应键盘键
/// （PostMessage 直投，同 U/J 已验证通道），该键的原生手柄输出被替代（动作跟着键位走，不双触发）。
/// 支持 LB→Q（战术道具）、LS→B（武器模式切换）等任意组合；同一手柄键只绑一个键盘键。
/// </summary>
public sealed class KeyBindForm : Form
{
    private readonly Func<List<PadKeyBind>,Task<bool>>? _confirm;
    private readonly Action<bool>? _captureChanged;
    private readonly List<(int padBit, string padName, Label keyLbl, Button setBtn, Button clearBtn)> _rows = new();
    private readonly List<PadKeyBind> _bindings;
    private readonly Action<List<PadKeyBind>> _onChange;   // 变更后立即保存
    private int _capturePadBit = -1;                        // -1=未在录键
    private readonly Label _hint;
    private readonly System.Windows.Forms.Timer _flashTimer;

    /// <summary>可映射的手柄键全表（含 Home/Logo；顺序即展示顺序）。</summary>
    private static readonly (int bit, string name)[] PadBits =
    {
        (0x1000, "A"), (0x2000, "B"), (0x4000, "X"), (0x8000, "Y"),
        (0x0100, "LB"), (0x0200, "RB"), (KeyMap.LT, "LT"), (KeyMap.RT, "RT"),
        (0x0040, "L3"), (0x0080, "R3"),
        (0x0001, "↑"), (0x0002, "↓"), (0x0004, "←"), (0x0008, "→"),
        (0x0010, "Option"), (0x0020, "View"), (0x0400, "Home"),
    };

    public KeyBindForm(List<PadKeyBind> bindings, Action<List<PadKeyBind>> onChange,Func<List<PadKeyBind>,Task<bool>>? confirm=null,Action<bool>? captureChanged=null)
    {
        _confirm=confirm;_captureChanged=captureChanged;
        _bindings = bindings;
        _onChange = onChange;
        Dpi.Scale = Math.Max(1f, DeviceDpi / 96f);
        Text = Lang.T("按键映射 · 手柄键 → 键盘键", "Key Map · Pad key → Keyboard key");
        int contentH = Dpi.Px(17 * 42 + 190);
        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        int winH = Math.Min(contentH, Math.Max(Dpi.Px(420), wa.Height - Dpi.Px(24)));
        ClientSize = new Size(Dpi.Px(470), winH);
        MinimumSize = new Size(Dpi.Px(470), winH);
        MaximumSize = new Size(Dpi.Px(470), winH);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = Assets.Ui(10F);
        Assets.Skin(this);
        KeyPreview = true;   // 录键：窗体先收到键盘事件
        KeyDown += OnCaptureKey;

        var title = new Label
        {
            Text = Lang.T("按键映射（手柄键 → 键盘任意键）", "Key Map (pad → any keyboard key)"),
            Location = new Point(Dpi.Px(20), Dpi.Px(12)), Size = new Size(Dpi.Px(430), Dpi.Px(28)),
            Font = Assets.Ui(14F, FontStyle.Bold),
        };
        _hint = new Label
        {
            Text = Lang.T("把键盘上的键绑定到手柄按键：点「重设」后按下键盘上的目标键。绑定后该手柄键向游戏注入键盘键，支持字母 / 数字 / F1-F12 / Tab / CapsLock / Shift / Ctrl / Alt / Win / 方向键等（Fn 由键盘固件处理，不支持）。",
                           "Bind a keyboard key to a pad button: tap Set, then press the keyboard key you want. The pad button then sends that keyboard key to the game. Letters / digits / F1-F12 / Tab / CapsLock / Shift / Ctrl / Alt / Win / arrows supported (Fn is handled by the keyboard firmware and can't be mapped)."),
            Location = new Point(Dpi.Px(20), Dpi.Px(46)), Size = new Size(Dpi.Px(430), Dpi.Px(66)),
            ForeColor = Color.FromArgb(107, 114, 128), Font = Assets.Ui(8.5F),
        };

        var tbl = new TableLayoutPanel
        {
            Location = new Point(Dpi.Px(20), Dpi.Px(120)), Size = new Size(Dpi.Px(430), Dpi.Px(17 * 42)),
            ColumnCount = 4, RowCount = PadBits.Length + 1,
        };
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Dpi.Px(90)));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Dpi.Px(70)));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Dpi.Px(70)));

        for (int i = 0; i < PadBits.Length; i++)
        {
            var (bit, name) = PadBits[i];
            tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, Dpi.Px(42)));
            var padName = new Label { Text = name, TextAlign = ContentAlignment.MiddleLeft, Font = Assets.Ui(10.5F, FontStyle.Bold) };
            var keyLbl = new Label { Text = Lang.T("绑定：", "Bound: ") + BoundName(bit), TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(55, 65, 81), Font = Assets.Ui(9.5F) };
            var setBtn = new StudioButton { Text = Lang.T("重设", "Set"), Size = new Size(Dpi.Px(62), Dpi.Px(28)), FlatStyle = FlatStyle.Flat, Tag = bit };
            Assets.StyleOutline(setBtn, 6);
            var clearBtn = new StudioButton { Text = Lang.T("清除", "Clear"), Size = new Size(Dpi.Px(62), Dpi.Px(28)), FlatStyle = FlatStyle.Flat, Tag = bit };
            Assets.StyleOutline(clearBtn, 6);
            tbl.Controls.Add(padName, 0, i);
            tbl.Controls.Add(keyLbl, 1, i);
            tbl.Controls.Add(setBtn, 2, i);
            tbl.Controls.Add(clearBtn, 3, i);
            _rows.Add((bit, name, keyLbl, setBtn, clearBtn));
            setBtn.Click += (_, _) => BeginCapture(bit);
            clearBtn.Click += (_, _) => ClearBind(bit);
        }

        var btnDone = new StudioButton
        {
            Text = Lang.T("完成", "Done"),
            Location = new Point(Dpi.Px(340), Dpi.Px(120 + 17 * 42 + 12)), Size = new Size(Dpi.Px(110), Dpi.Px(34)),
            FlatStyle = FlatStyle.Flat,
        };
        Assets.StylePrimary(btnDone);
        btnDone.Click += (_, _) => { _flashTimer?.Stop(); DialogResult = DialogResult.OK; Close(); };

        var scroll = new Panel { AutoScroll = true, Location = Point.Empty, Size = ClientSize, BackColor = Color.Transparent };
        scroll.Controls.Add(title);
        scroll.Controls.Add(_hint);
        scroll.Controls.Add(tbl);
        scroll.Controls.Add(btnDone);
        Controls.Add(scroll);
        UiLayout.MappingDialog(this,title,_hint,tbl,btnDone);

        _flashTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _flashTimer.Tick += (_, _) =>
        {
            // 录键提示闪烁：等待键盘输入时给出醒目的视觉状态
            _hint.ForeColor = _hint.ForeColor == Color.FromArgb(249, 115, 22)
                ? Color.FromArgb(107, 114, 128)
                : Color.FromArgb(249, 115, 22);
        };
    }

    /// <summary>当前绑定键盘键的显示名（未绑定显示 "—"）。</summary>
    private string BoundName(int padBit)
    {
        foreach (var b in _bindings)
            if (b.PadBit == padBit) return b.KeyName;
        return Lang.T("未绑定", "none");
    }

    private void BeginCapture(int padBit)
    {
        _captureChanged?.Invoke(true);
        _capturePadBit = padBit;
        foreach (var (bit, _, keyLbl, _, _) in _rows)
            if (bit == padBit) keyLbl.Text = Lang.T("等待键盘按键…（Esc 取消）", "Waiting for keyboard key… (Esc cancels)");
        _hint.Text = Lang.T("请按下键盘上要绑定到「" + PadNameOf(padBit) + "」的键。", "Press the keyboard key to bind to \"" + PadNameOf(padBit) + "\".");
        _hint.ForeColor = Color.FromArgb(249, 115, 22);
        _flashTimer.Start();
    }

    private void CancelCapture()
    {
        _captureChanged?.Invoke(false);
        _capturePadBit = -1;
        _flashTimer.Stop();
        RefreshRows();
        _hint.Text = Lang.T("已取消。", "Cancelled.");
        _hint.ForeColor = Color.FromArgb(107, 114, 128);
    }

    private async void OnCaptureKey(object? sender, KeyEventArgs e)
    {
        if (_capturePadBit < 0) return;
        if (e.KeyCode == Keys.Escape) { CancelCapture(); return; }

        var k = KeyboardKeys.ByVk((ushort)e.KeyCode);
        if (k == null)
        {
            _hint.Text = Lang.T("该键暂不支持映射（Fn 等由键盘固件处理）。请换一个键。", "This key isn't mappable (Fn etc. is handled by the keyboard firmware). Try another.");
            return;
        }
        int padBit = _capturePadBit;
        _capturePadBit = -1;
        _flashTimer.Stop();
        // 同一手柄键只保留一条；更新或新增
        var candidate=_bindings.Where(b=>b.PadBit!=padBit).ToList();candidate.Add(PadKeyBind.Of(padBit,k));
        bool accepted=_confirm==null||await _confirm(candidate);if(IsDisposed)return;if(!accepted){CancelCapture();return;}
        _bindings.Clear();_bindings.AddRange(candidate);_captureChanged?.Invoke(false);
        RefreshRows();
        _hint.Text = Lang.T("已绑定：" + PadNameOf(padBit) + " → " + k.Name + "。", "Bound: " + PadNameOf(padBit) + " → " + k.Name + ".");
        _hint.ForeColor = Color.FromArgb(5, 150, 105);
        _onChange(_bindings);
    }

    private void ClearBind(int padBit)
    {
        if (_capturePadBit == padBit) { _capturePadBit = -1; _flashTimer.Stop(); }
        _bindings.RemoveAll(b => b.PadBit == padBit);
        RefreshRows();
        _hint.Text = Lang.T("已清除：" + PadNameOf(padBit) + " 恢复原生手柄输出。", "Cleared: " + PadNameOf(padBit) + " restores native pad output.");
        _hint.ForeColor = Color.FromArgb(107, 114, 128);
        _onChange(_bindings);
    }

    private void RefreshRows()
    {
        foreach (var (bit, _, keyLbl, _, _) in _rows)
            keyLbl.Text = Lang.T("绑定：", "Bound: ") + BoundName(bit);
    }

    private static string PadNameOf(int padBit)
    {
        foreach (var (bit, name) in PadBits)
            if (bit == padBit) return name;
        return KeyMap.BitName(padBit);
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing){_captureChanged?.Invoke(false);_flashTimer?.Stop();_flashTimer?.Dispose();}
        base.Dispose(disposing);
    }
    protected override bool ProcessCmdKey(ref Message msg,Keys keyData)
    {
        if(_capturePadBit>=0){var key=KeyboardKeys.FromMessage((ushort)(keyData&Keys.KeyCode),msg.LParam);var e=new KeyEventArgs(key==null?keyData:(Keys)key.Vk);OnCaptureKey(this,e);return true;}
        return base.ProcessCmdKey(ref msg,keyData);
    }
}
