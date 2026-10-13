using System.Drawing;
using System.Windows.Forms;

namespace CODMControllerProbe;

/// <summary>
/// 一键诊断：把最容易出问题的 4 个环节做成状态灯（驱动/隐藏/手柄/音频），
/// 哪一步有问题直接告诉用户怎么解决，减少售后沟通。
/// </summary>
public sealed class DiagForm : Form
{
    private readonly Label[] _rows;
    private readonly Button _btnGuide;
    private readonly bool _vigemOk, _hidHideOk, _padOk, _audioOk;
    private readonly int _versionLevel;

    public DiagForm(bool vigemOk, bool hidHideOk, bool padOk, bool audioOk, int versionLevel,
                    string hidHideText, string padText, Action openGuide)
    {
        _vigemOk = vigemOk;
        _hidHideOk = hidHideOk;
        _padOk = padOk;
        _audioOk = audioOk;
        _versionLevel = versionLevel;

        Text = "一键诊断 · CODM 手柄转换";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(252, 249, 244);
        ClientSize = new Size(520, 400);
        Font = Assets.Ui(9F);

        var title = new Label
        {
            Text = Lang.T("一键诊断", "Diagnostics"),
            Font = Assets.Ui(15F, FontStyle.Bold),
            ForeColor = Assets.MainText,
            Location = new Point(28, 20), Size = new Size(240, 30),
        };
        Controls.Add(title);

        var sub = new Label
        {
            Text = Lang.T("哪里有问题一目了然，按提示操作即可", "See at a glance what needs fixing, follow the hint"),
            Font = Assets.Ui(8.5F),
            ForeColor = Assets.SubText,
            Location = new Point(28, 52), Size = new Size(320, 20),
        };
        Controls.Add(sub);

        _rows = new[]
        {
            MakeRow(24, 92, "ViGEm 虚拟手柄驱动", vigemOk,
                Lang.T("未检测到 ViGEm 驱动。请安装 ViGEmBus 驱动后重启工具。",
                       "ViGEm driver not found. Install ViGEmBus driver and restart the app.")),
            MakeRow(24, 140, "HidHide 实体手柄隐藏", hidHideOk,
                Lang.T("HidHide 未生效（未安装驱动或未启用）。安装 HidHide 后重开工具；" + hidHideText,
                       "HidHide not active (driver missing or disabled). Install HidHide and restart; " + hidHideText)),
            MakeRow(24, 188, "手柄识别（输入侧）", padOk,
                Lang.T("未检测到实体手柄，请插上手柄（或拔插一次重试）。" + padText,
                       "No controller detected. Plug one in (or replug once). " + padText)),
            MakeRow(24, 236, "系统音频采集（枪声震动）", audioOk,
                Lang.T("音频采集不可用：枪声震动需要系统输出音频。请检查声卡/默认输出设备。",
                       "Audio capture unavailable: shot rumble needs system audio. Check sound output device.")),
        };

        var note = new Label
        {
            Text = Lang.T("提示：枪声震动才依赖音频采集，其他功能不受影响。",
                          "Note: only shot-rumble depends on audio capture; other features unaffected."),
            Font = Assets.Ui(8F),
            ForeColor = Assets.SubText,
            Location = new Point(28, 292), Size = new Size(460, 20),
        };
        Controls.Add(note);

        var ver = new Label
        {
            Text = Lang.T("版本 v3.4.2 · 全部功能免费 · Made with ♥ by Burrito 什么饼（B站）",
                          "v3.4.2 · All features free · Made with ♥ by Burrito (Bilibili)"),
            Font = Assets.Ui(8F),
            ForeColor = Color.FromArgb(160, 165, 170),
            Location = new Point(28, 318), Size = new Size(460, 20),
        };
        Controls.Add(ver);

        _btnGuide = new StudioButton
        {
            Text = Lang.T("查看使用引导", "View Setup Guide"),
            Font = Assets.Ui(9F),
            FlatStyle = FlatStyle.Flat,
            Location = new Point(28, 350), Size = new Size(150, 32),
        };
        Assets.StyleOutline(_btnGuide, 8);
        _btnGuide.Click += (_, _) => openGuide();
        Controls.Add(_btnGuide);

        var btnClose = new StudioButton
        {
            Text = Lang.T("关闭", "Close"),
            Font = Assets.Ui(9F),
            FlatStyle = FlatStyle.Flat,
            Location = new Point(196, 350), Size = new Size(90, 32),
        };
        Assets.StyleOutline(btnClose, 8);
        btnClose.Click += (_, _) => Close();
        Controls.Add(btnClose);

        _ = _rows;
        _ = _btnGuide;
        _ = _versionLevel;
    }

    private Label MakeRow(int x, int y, string name, bool ok, string fixHint)
    {
        var lamp = new Label
        {
            Text = ok ? "✓" : "✕",
            Font = Assets.Ui(12F, FontStyle.Bold),
            ForeColor = ok ? Color.FromArgb(5, 150, 105) : Color.FromArgb(239, 68, 68),
            Location = new Point(x, y), Size = new Size(26, 24),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        Controls.Add(lamp);

        var nameLbl = new Label
        {
            Text = name,
            Font = Assets.Ui(9.5F, FontStyle.Bold),
            ForeColor = Assets.MainText,
            Location = new Point(x + 30, y), Size = new Size(200, 22),
        };
        Controls.Add(nameLbl);

        var fixLbl = new Label
        {
            Text = ok ? Lang.T("一切正常", "All good") : fixHint,
            Font = Assets.Ui(8.5F),
            ForeColor = ok ? Color.FromArgb(5, 150, 105) : Color.FromArgb(200, 90, 60),
            Location = new Point(x + 30, y + 22), Size = new Size(452, 30),
        };
        Controls.Add(fixLbl);
        return fixLbl;
    }
}
