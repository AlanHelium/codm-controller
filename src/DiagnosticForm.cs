using System;
using System.Drawing;
using System.Windows.Forms;

namespace CODMControllerProbe;

/// <summary>
/// 诊断报告窗口：一键汇总「输入源 / 虚拟输出 / 隔离隐藏 / 键位配置 / 实时原始状态 / 日志尾段 / 系统环境」，
/// 点「复制全部」即可粘贴给开发者定位问题（串键、检测不到、断连、不顺畅等），无需手动描述。
/// </summary>
public class DiagnosticForm : Form
{
    private readonly Func<string> _build;
    private TextBox _box = null!;
    private Label _hint = null!;

    public DiagnosticForm(Func<string> build)
    {
        _build = build;
        Dpi.Scale = Math.Max(1f, DeviceDpi / 96f);
        Text = Lang.T("诊断报告", "Diagnostic Report");
        ClientSize = new Size(Dpi.Px(760), Dpi.Px(560));
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = Assets.CreamBg;
        Font = Assets.Ui(9.5F);
        StartPosition = FormStartPosition.CenterParent;

        _hint = new Label
        {
            Text = Lang.T(
                "一键汇总软件运行状态与日志。出现任何问题（串键/检测不到/断连/不顺畅）时点「复制全部」粘贴给我即可。",
                "One-tap summary of app state and logs. When anything breaks (double-input / not detected / dropouts / lag), hit \"Copy All\" and paste it."),
            Location = new Point(Dpi.Px(20), Dpi.Px(14)), Size = new Size(Dpi.Px(720), Dpi.Px(30)),
            ForeColor = Assets.SubText, Font = Assets.Ui(9F),
        };
        Controls.Add(_hint);

        _box = new TextBox
        {
            Location = new Point(Dpi.Px(20), Dpi.Px(52)), Size = new Size(Dpi.Px(720), Dpi.Px(430)),
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
            BackColor = Color.FromArgb(15, 17, 21), ForeColor = Color.FromArgb(228, 230, 235),
            Font = Assets.Ui(9F), WordWrap = false,
        };
        Controls.Add(_box);
        RefreshContent();

        var btnCopy = new StudioButton
        {
            Text = Lang.T("复制全部", "Copy All"),
            Location = new Point(Dpi.Px(20), Dpi.Px(494)), Size = new Size(Dpi.Px(120), Dpi.Px(36)),
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

        var btnRefresh = new StudioButton
        {
            Text = Lang.T("刷新", "Refresh"),
            Location = new Point(Dpi.Px(150), Dpi.Px(494)), Size = new Size(Dpi.Px(90), Dpi.Px(36)),
            FlatStyle = FlatStyle.Flat, Font = Assets.Ui(9.5F),
        };
        Assets.StyleCapsule(btnRefresh, false, true);
        btnRefresh.Click += (_, _) => RefreshContent();
        Controls.Add(btnRefresh);

        var btnClose = new StudioButton
        {
            Text = Lang.T("关闭", "Close"),
            Location = new Point(Dpi.Px(250), Dpi.Px(494)), Size = new Size(Dpi.Px(90), Dpi.Px(36)),
            FlatStyle = FlatStyle.Flat, Font = Assets.Ui(9.5F),
        };
        Assets.StyleCapsule(btnClose, false, true);
        btnClose.Click += (_, _) => Close();
        Controls.Add(btnClose);
    }

    private void RefreshContent()
    {
        try
        {
            _box.Text = _build();
        }
        catch (Exception ex)
        {
            _box.Text = "诊断报告生成失败：" + ex;
        }
        _box.SelectionStart = 0;
        _box.ScrollToCaret();
    }
}
