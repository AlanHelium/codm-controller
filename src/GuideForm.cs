using System.Drawing;
using System.Windows.Forms;

namespace CODMControllerProbe;

/// <summary>
/// 首次启动引导 / 使用指南：3 步上手 + 重点说明（切光标组合键、免费/付费差异）。
/// 免费版第一次打开自动弹出；此后可在诊断窗口随时再开。
/// </summary>
public sealed class GuideForm : Form
{
    public GuideForm()
    {
        Text = "使用引导 · CODM 手柄转换";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(252, 249, 244);
        ClientSize = new Size(560, 460);
        Font = Assets.Ui(9F);

        var title = new Label
        {
            Text = Lang.T("三步开始用手柄玩 CODM", "Three steps to play CODM with a pad"),
            Font = Assets.Ui(16F, FontStyle.Bold),
            ForeColor = Assets.MainText,
            Location = new Point(32, 24), Size = new Size(480, 34),
        };
        Controls.Add(title);

        (string head, string body)[] steps =
        {
            (Lang.T("① 插入手柄", "① Plug in your pad"),
             Lang.T("支持 Xbox360 协议第三方手柄、Xbox One/Series S/X、DS4、DualSense / DualSense Edge。插上即可，工具会自动识别。",
                    "Supports Xbox360 third-party pads, Xbox One/Series S/X, DS4, DualSense / DualSense Edge. Auto-detected.")),
            (Lang.T("② 打开工具并启动转换", "② Open the app and start"),
             Lang.T("点「启动转换」。工具会自动隐藏实体手柄，只留一个 DS4 协议的虚拟手柄给游戏识别。",
                    "Hit Start. The app hides the physical pad and exposes one virtual DS4 controller to the game.")),
            (Lang.T("③ 进游戏按 L3+R3 切模式", "③ In game, press L3+R3 to switch mode"),
             Lang.T("游戏默认键鼠输入。同时按下左摇杆 + 右摇杆（L3+R3），把光标切出来再收回，即切换到「手柄原生」大辅瞄模式。可听到语音提示。",
                    "The game defaults to KBM input. Press L3+R3 together to toggle the cursor — switches to native pad mode with big aim-assist. Voice confirms.")),
        };

        int y = 76;
        foreach (var (head, body) in steps)
        {
            var h = new Label
            {
                Text = head,
                Font = Assets.Ui(10.5F, FontStyle.Bold),
                ForeColor = Assets.PrimaryOrange,
                Location = new Point(32, y), Size = new Size(500, 24),
            };
            Controls.Add(h);
            y += 26;
            var b = new Label
            {
                Text = body,
                Font = Assets.Ui(9F),
                ForeColor = Assets.MainText,
                Location = new Point(32, y), Size = new Size(500, 52),
            };
            Controls.Add(b);
            y += 58;
        }

        var note = new Label
        {
            Text = Lang.T("切模式组合键可在「键位修正」里自定义（按一次记录一次）。切换失败就再按一次，光标会立刻移开不挡视野。",
                          "The switch combo can be re-bound in Key Mapping (press once to record). Tap again if it fails — cursor moves away instantly."),
            Font = Assets.Ui(8.5F),
            ForeColor = Assets.SubText,
            Location = new Point(32, y), Size = new Size(500, 34),
        };
        Controls.Add(note);
        y += 44;

        var btn = new StudioButton
        {
            Text = Lang.T("开始使用", "Let's go"),
            Font = Assets.Ui(10F, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            BackColor = Assets.PrimaryOrange,
            ForeColor = Color.White,
            Location = new Point(32, y), Size = new Size(150, 38),
        };
        btn.FlatAppearance.BorderSize = 0;
        btn.Click += (_, _) => Close();
        Controls.Add(btn);
    }
}
