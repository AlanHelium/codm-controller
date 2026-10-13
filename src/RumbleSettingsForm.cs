using System;
using System.Drawing;
using System.Windows.Forms;

namespace CODMControllerProbe
{
    /// <summary>
    /// 震动设置窗口（Pro 版）：总开关 + 枪声触发震动 + 强度 + 力度下限 + 低频增强 + 灵敏度
    /// + 仅游戏前台触发（后台不检测，避免桌面声音误震）+ 识别当前游戏进程。
    /// </summary>
    public sealed class RumbleSettingsForm : Form
    {
        private readonly Action<bool, bool, int, int, bool, float> _onChanged; // rumbleOn, shotOn, strength, minLevel, lowBoost, sens
        private readonly Action<bool> _onFocusOnly;  // 仅游戏前台触发开关
        private readonly Func<string>? _onPickGame;  // 识别当前前台窗口为游戏，返回进程名（空=未锁定）
        private readonly Action? _onTest;

        private CheckBox chkRumble = null!;
        private CheckBox chkShot = null!;
        private StudioSlider trkStrength = null!;
        private Label lblStrengthVal = null!;
        private StudioSlider trkMinLevel = null!;
        private Label lblMinLevelVal = null!;
        private CheckBox chkLowBoost = null!;
        private StudioSlider trkSens = null!;
        private Label lblSensVal = null!;
        private CheckBox chkFocusOnly = null!;
        private Label lblGameName = null!;

        public RumbleSettingsForm(bool rumbleOn, bool shotOn, int strength, int minLevel, bool lowBoost, float sens,
                                  bool focusOnly, string gameName,
                                  Action<bool, bool, int, int, bool, float> onChanged,
                                  Action<bool> onFocusOnly, Func<string>? onPickGame = null, Action? onTest = null)
        {
            _onChanged = onChanged;
            _onFocusOnly = onFocusOnly;
            _onPickGame = onPickGame;
            _onTest = onTest;
            Dpi.Scale = Math.Max(1f, DeviceDpi / 96f);
            Text = Lang.T("震动设置", "Rumble Settings");
            ClientSize = new Size(Dpi.Px(400), Dpi.Px(540));
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = Assets.Ui(9.5F);
            Assets.Skin(this);
            BuildLayout(rumbleOn, shotOn, strength, minLevel, lowBoost, sens, focusOnly, gameName);
        }

private void BuildLayout(bool rumbleOn, bool shotOn, int strength, int minLevel, bool lowBoost, float sens,
                                 bool focusOnly, string gameName)
        {
            ClientSize=new Size(Math.Min(Dpi.Px(700),Screen.FromControl(this).WorkingArea.Width-Dpi.Px(40)),Math.Min(Dpi.Px(720),Screen.FromControl(this).WorkingArea.Height-Dpi.Px(80)));
            MinimumSize=new Size(Dpi.Px(580),Dpi.Px(450)); FormBorderStyle=FormBorderStyle.Sizable;
            var shell=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Padding=new Padding(Dpi.Px(20))};
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));shell.RowStyles.Add(new RowStyle(SizeType.Percent,100));shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));Controls.Add(shell);
            var viewport=new Panel {Dock=DockStyle.Fill,AutoScroll=true,Margin=Padding.Empty};
            var body=UiLayout.Stack();viewport.Controls.Add(body);shell.Controls.Add(viewport,0,0);
            var general=UiLayout.Card(body,Lang.T("震动反馈","Rumble feedback"));
            chkRumble=UiLayout.Check(general,Lang.T("启用手柄震动","Enable controller rumble"),rumbleOn,_=>Push());
            trkStrength=new StudioSlider {Minimum=0,Maximum=100,Value=Math.Clamp(strength,0,100),SmallChange=5};
            lblStrengthVal=UiLayout.Text(trkStrength.Value+"%");UiLayout.BoundSlider(general,Lang.T("整体震动强度","Overall strength"),lblStrengthVal,trkStrength);
            trkStrength.ValueChanged+=(_,_)=>{lblStrengthVal.Text=trkStrength.Value+"%";Push();};

            var audio=UiLayout.Card(body,Lang.T("枪声触发","Shot-triggered rumble"),Lang.T("分析系统音频中的枪声，为手柄提供震动反馈。","Analyze gunshot bursts in system audio to trigger rumble."));
            chkShot=UiLayout.Check(audio,Lang.T("启用枪声触发","Enable shot trigger"),shotOn,_=>Push());
            trkMinLevel=new StudioSlider {Minimum=50,Maximum=100,Value=Math.Clamp(minLevel,50,100),SmallChange=5};
            lblMinLevelVal=UiLayout.Text(trkMinLevel.Value+"%");UiLayout.BoundSlider(audio,Lang.T("开枪力度下限","Minimum shot strength"),lblMinLevelVal,trkMinLevel);
            trkMinLevel.ValueChanged+=(_,_)=>{lblMinLevelVal.Text=trkMinLevel.Value+"%";Push();};
            chkLowBoost=UiLayout.Check(audio,Lang.T("低频增强","Low-frequency boost"),lowBoost,_=>Push(),Lang.T("让重型枪、爆炸等低频声音更容易触发。","Make heavy-gun and explosion sounds easier to detect."));
            trkSens=new StudioSlider {Minimum=15,Maximum=80,Value=(int)Math.Round(Math.Clamp(sens,1.5f,8f)*10),SmallChange=5};
            lblSensVal=UiLayout.Text((trkSens.Value/10f).ToString("0.0")+"×");UiLayout.BoundSlider(audio,Lang.T("枪声识别灵敏度","Shot detection sensitivity"),lblSensVal,trkSens);
            trkSens.ValueChanged+=(_,_)=>{lblSensVal.Text=(trkSens.Value/10f).ToString("0.0")+"×";Push();};

            var focus=UiLayout.Card(body,Lang.T("触发范围","When to trigger"));
            chkFocusOnly=UiLayout.Check(focus,Lang.T("仅游戏在前台时触发","Only while the game is focused"),focusOnly,v=>{_onFocusOnly(v);Push();});
            lblGameName=UiLayout.Text(Lang.T("当前游戏：","Current game: ")+(string.IsNullOrEmpty(gameName)?Lang.T("自动识别前台","Automatic foreground detection"):gameName));UiLayout.Add(focus,lblGameName);
            if(_onPickGame!=null) UiLayout.Action(focus,Lang.T("识别当前游戏 →","Identify current game →"),()=>{var name=_onPickGame();if(!string.IsNullOrEmpty(name))lblGameName.Text=Lang.T("当前游戏：","Current game: ")+name;});

            var footer=new FlowLayoutPanel {AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Dock=DockStyle.Top,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(0,Dpi.Px(12),0,0)};
            var done=new StudioButton {Text=Lang.T("完成","Done"),AutoSize=true,MinimumSize=new Size(Dpi.Px(110),Dpi.Px(40)),DialogResult=DialogResult.OK};
            Assets.StylePrimary(done);UiLayout.Responsive(done);footer.Controls.Add(done);AcceptButton=done;CancelButton=done;
            if(_onTest!=null) {var test=new StudioButton {Text=Lang.T("测试震动","Test rumble"),AutoSize=true,MinimumSize=new Size(Dpi.Px(110),Dpi.Px(40))};Assets.StyleOutline(test);UiLayout.Responsive(test);test.Click+=(_,_)=>_onTest();footer.Controls.Add(test);}
            shell.Controls.Add(footer,0,1);Shown+=(_,_)=>UiLayout.FitText(shell);viewport.SizeChanged+=(_,_)=>UiLayout.FitText(body);UiPreview.Attach(this);
        }


        private void Push()
        {
            _onChanged(chkRumble.Checked, chkShot.Checked, trkStrength.Value, trkMinLevel.Value, chkLowBoost.Checked, trkSens.Value / 10f);
        }
    }
}
