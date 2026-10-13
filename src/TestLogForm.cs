using System;
using System.Drawing;
using System.Windows.Forms;

namespace CODMControllerProbe;

/// <summary>
/// 测试记录窗口：自动累积手柄测试的结构化日志（输入源 / WASD 方向 / 数据角度 / 摇杆幅度 / Shift 状态 / 触摸板），
/// 一键复制即可粘贴给开发者诊断，无需手打。
/// </summary>
public class TestLogForm : Form
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 250 };
    private TextBox _box = null!;
    private Label _hint = null!;

    public TestLogForm()
    {
        Dpi.Scale = Math.Max(1f, DeviceDpi / 96f);
        Text = Lang.T("测试记录", "Test Log");
        ClientSize = new Size(Dpi.Px(640), Dpi.Px(440));
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = Assets.CreamBg;
        Font = Assets.Ui(9.5F);
        StartPosition = FormStartPosition.CenterParent;

        _hint = new Label
        {
            Text = Lang.T(
                "自动记录手柄测试日志：推摇杆（各方向推满 2 秒）、按触摸板、切手柄——完成后点「复制全部」粘贴即可。",
                "Auto-captures pad test logs: push each stick direction fully for 2s, press the touchpad, switch pads — then hit \"Copy All\"."),
            Location = new Point(Dpi.Px(20), Dpi.Px(14)), Size = new Size(Dpi.Px(600), Dpi.Px(30)),
            ForeColor = Assets.SubText, Font = Assets.Ui(9F),
        };
        Controls.Add(_hint);

        _box = new TextBox
        {
            Location = new Point(Dpi.Px(20), Dpi.Px(52)), Size = new Size(Dpi.Px(600), Dpi.Px(330)),
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            BackColor = Color.FromArgb(15, 17, 21), ForeColor = Color.FromArgb(228, 230, 235),
            Font = Assets.Ui(9F), WordWrap = false,
        };
        Controls.Add(_box);

        var btnCopy = new StudioButton
        {
            Text = Lang.T("复制全部", "Copy All"),
            Location = new Point(Dpi.Px(20), Dpi.Px(392)), Size = new Size(Dpi.Px(120), Dpi.Px(34)),
            FlatStyle = FlatStyle.Flat, Font = Assets.Ui(9.5F, FontStyle.Bold),
        };
        Assets.StyleCapsule(btnCopy, true, false);
        btnCopy.Click += (_, _) =>
        {
            try
            {
                System.Windows.Forms.Clipboard.SetText(_box.Text);
                _hint.Text = Lang.T("已复制到剪贴板，直接粘贴即可。", "Copied to clipboard — paste it directly.");
            }
            catch { /* 剪贴板被占用时忽略 */ }
        };
        Controls.Add(btnCopy);

        var btnClear = new StudioButton
        {
            Text = Lang.T("清空", "Clear"),
            Location = new Point(Dpi.Px(150), Dpi.Px(392)), Size = new Size(Dpi.Px(80), Dpi.Px(34)),
            FlatStyle = FlatStyle.Flat, Font = Assets.Ui(9.5F),
        };
        Assets.StyleCapsule(btnClear, false, true);
        btnClear.Click += (_, _) => { _box.Clear(); TestLog.ResetFile(); };
        Controls.Add(btnClear);

        TestLog.ResetFile(); // 每次打开从干净状态开始
        _timer.Tick += (_, _) =>
        {
            string more = TestLog.Drain();
            if (more.Length > 0)
            {
                _box.AppendText(more);
                _box.SelectionStart = _box.TextLength;
                _box.ScrollToCaret();
            }
        };
        _timer.Start();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _timer.Stop();
        base.OnFormClosing(e);
    }
}
