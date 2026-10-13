namespace CODMControllerProbe;

/// <summary>
/// 语音设置总控台。
/// - 免费版：仅提供固定的 TTS "Let's Go"，可选 开启/关闭，不可自定义（Pro 专属功能置灰说明）。
/// - Pro 版：完整语音库 —— 真人语音（导入/勾选随机/删除/试听）+ 音量；
///   TTS 自定义句子 + 音量。
/// 所有改动立即生效并保存到配置文件。
/// </summary>
public class VoiceSettingsForm : Form
{
    private readonly Action<int> _onModeChanged;
    private readonly bool _isPro;
    private bool _initializing = true;

    private RadioButton rbOff = null!;
    private RadioButton rbTts = null!;
    private RadioButton rbHuman = null!;
    private StudioTabs tabs = null!;
    private StudioSlider trkHumanVol = null!;
    private Label lblHumanVol = null!;
    private CheckedListBox clbClips = null!;
    private StudioSlider trkTtsVol = null!;
    private Label lblTtsVol = null!;
    private ListBox lstLines = null!;
    private TextBox txtLine = null!;

    public VoiceSettingsForm(int currentMode, bool isPro, Action<int> onModeChanged)
    {
        isPro = true; _isPro = true;
        _onModeChanged = onModeChanged;
        Dpi.Scale = Math.Max(1f, DeviceDpi / 96f);

        Text = "语音设置";
        ClientSize = new Size(Dpi.Px(640), Dpi.Px(460));
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = Assets.Ui(9.5F);
        Assets.Skin(this);

        var lblMode = new Label
        {
            Text = isPro ? "切换提示音" : "切换提示音（免费版）",
            Location = new Point(Dpi.Px(24), Dpi.Px(14)), Size = new Size(Dpi.Px(300), Dpi.Px(28)),
            Font = Assets.Ui(15F, FontStyle.Bold),
        };

        // ---- 模式选择 ----
        rbOff = MakeModeRadio("关闭", 24, 50);
        rbTts = MakeModeRadio("TTS 语音", 130, 50);
        rbHuman = MakeModeRadio("真人语音", 240, 50);

        if (isPro)
        {
            rbHuman.Enabled = true;
        }
        else
        {
            // 免费版：真人语音不可选（Pro 专属）
            rbHuman.Enabled = false;
            rbHuman.Text += "（Pro）";
        }

        int start = currentMode switch { VoicePlayer.Off => VoicePlayer.Off, VoicePlayer.Human when isPro => VoicePlayer.Human, _ => VoicePlayer.Tts };
        (start switch { VoicePlayer.Off => rbOff, VoicePlayer.Human => rbHuman, _ => rbTts }).Checked = true;

        if (isPro)
        {
            // ---- Pro：完整语音库选项卡 ----
            tabs = new StudioTabs { Location = new Point(Dpi.Px(24), Dpi.Px(86)), Size = new Size(Dpi.Px(590), Dpi.Px(330)) };
            tabs.TabPages.Add(BuildHumanPage());
            tabs.TabPages.Add(BuildTtsPage());
            tabs.SelectedIndex = start == VoicePlayer.Tts ? 1 : 0;

            var btnPreview = new StudioButton
            {
                Text = "试听当前模式", Location = new Point(Dpi.Px(24), Dpi.Px(424)), Size = new Size(Dpi.Px(130), Dpi.Px(28)),
                Font = Assets.Ui(9.5F, FontStyle.Bold), FlatStyle = FlatStyle.Flat,
            };
            Assets.StylePrimary(btnPreview);
            btnPreview.Click += (_, _) => VoicePlayer.Preview(CurrentMode());

            var btnOk = new StudioButton
            {
                Text = "完成", Location = new Point(Dpi.Px(510), Dpi.Px(424)), Size = new Size(Dpi.Px(100), Dpi.Px(28)),
                Font = Assets.Ui(9.5F), FlatStyle = FlatStyle.Flat,
            };
            Assets.StyleOutline(btnOk);
            btnOk.Click += (_, _) => Close();

            Controls.Add(lblMode);
            Controls.Add(rbOff); Controls.Add(rbTts); Controls.Add(rbHuman);
            Controls.Add(tabs);
            Controls.Add(btnPreview);
            Controls.Add(btnOk);
        }
        else
        {
            // ---- 免费版：简单说明 + 开/关 ----
            var hint = new Label
            {
                Text = Lang.T("免费版：切换时播报固定的 TTS 语音 “Let's Go”，不可自定义句子、无真人语音。\nPro 版解锁完整语音库：自定义 TTS 句子、真人语音导入、随机播放勾选、音量调节。",
                              "Free: fixed TTS prompt “Let's Go” on switch; no custom lines, no human clips.\nPro unlocks the full voice library: custom TTS lines, human clips, shuffle, volume."),
                Location = new Point(Dpi.Px(24), Dpi.Px(92)), Size = new Size(Dpi.Px(580), Dpi.Px(56)),
                ForeColor = Color.FromArgb(107, 114, 128), Font = Assets.Ui(9.5F),
            };

            var btnOk = new StudioButton
            {
                Text = "完成", Location = new Point(Dpi.Px(510), Dpi.Px(420)), Size = new Size(Dpi.Px(100), Dpi.Px(28)),
                Font = Assets.Ui(9.5F), FlatStyle = FlatStyle.Flat,
            };
            Assets.StyleOutline(btnOk);
            btnOk.Click += (_, _) => Close();

            Controls.Add(lblMode);
            Controls.Add(rbOff); Controls.Add(rbTts); Controls.Add(rbHuman);
            Controls.Add(hint);
            Controls.Add(btnOk);
        }
        _initializing = false;
        ArrangeLayout(lblMode);
    }

private void ArrangeLayout(Label title)
    {
        var buttons=Controls.OfType<Button>().ToArray();
        var hints=Controls.OfType<Label>().Where(l=>l!=title).ToArray();
        Controls.Clear();MaximumSize=Size.Empty;FormBorderStyle=FormBorderStyle.Sizable;
        ClientSize=new Size(Math.Min(Dpi.Px(780),Screen.FromControl(this).WorkingArea.Width-Dpi.Px(40)),Math.Min(Dpi.Px(660),Screen.FromControl(this).WorkingArea.Height-Dpi.Px(80)));
        MinimumSize=new Size(Dpi.Px(640),Dpi.Px(450));
        var shell=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(Dpi.Px(20))};
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));shell.RowStyles.Add(new RowStyle(SizeType.Percent,100));shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));Controls.Add(shell);
        var heading=UiLayout.Stack();title.AutoSize=true;UiLayout.Add(heading,title);
        var modes=UiLayout.Buttons(heading);
        foreach(var radio in new[]{rbOff,rbTts,rbHuman}) {radio.AutoSize=true;radio.MinimumSize=new Size(0,Dpi.Px(32));radio.Margin=new Padding(0,0,Dpi.Px(20),Dpi.Px(8));modes.Controls.Add(radio);}
        shell.Controls.Add(heading,0,0);
        if(_isPro) {tabs.Dock=DockStyle.Fill;tabs.Margin=Padding.Empty;shell.Controls.Add(tabs,0,1);ArrangeVoicePage(tabs.TabPages[0],trkHumanVol,lblHumanVol,clbClips,null);ArrangeVoicePage(tabs.TabPages[1],trkTtsVol,lblTtsVol,lstLines,txtLine);}
        else {var info=UiLayout.Stack();foreach(var hint in hints){hint.AutoSize=true;UiLayout.Add(info,hint);}shell.Controls.Add(info,0,1);}
        var footer=new FlowLayoutPanel {AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Dock=DockStyle.Top,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(0,Dpi.Px(12),0,0)};
        foreach(var button in buttons.Reverse()) {button.AutoSize=true;button.MinimumSize=new Size(Dpi.Px(120),Dpi.Px(40));UiLayout.Responsive(button);footer.Controls.Add(button);}
        shell.Controls.Add(footer,0,2);Shown+=(_,_)=>UiLayout.FitText(shell);SizeChanged+=(_,_)=>UiLayout.FitText(shell);UiPreview.Attach(this);
    }
    private void ArrangeVoicePage(StudioTabPage page,StudioSlider volume,Label value,Control list,TextBox? entry)
    {
        var buttons=page.Controls.OfType<Button>().ToArray();
        var hint=page.Controls.OfType<Label>().FirstOrDefault(l=>l!=value&&l.Top>Dpi.Px(40));
        page.Controls.Clear(); page.AutoScroll=true;
        var shell=new TableLayoutPanel {Dock=DockStyle.Top,ColumnCount=1,RowCount=5,MinimumSize=new Size(0,Dpi.Px(380))};
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));shell.RowStyles.Add(new RowStyle(SizeType.Percent,100));shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));page.Controls.Add(shell);
        var level=UiLayout.Stack();UiLayout.BoundSlider(level,Lang.T("音量","Volume"),value,volume);shell.Controls.Add(level,0,0);
        if(hint!=null){hint.AutoSize=true;hint.Dock=DockStyle.Top;hint.Margin=new Padding(0,0,0,Dpi.Px(8));shell.Controls.Add(hint,0,1);}
        if(list is ListBox listBox) listBox.HorizontalScrollbar=true;
        list.Dock=DockStyle.Fill;list.Margin=Padding.Empty;shell.Controls.Add(list,0,2);
        if(entry!=null){entry.Dock=DockStyle.Top;entry.Margin=new Padding(0,Dpi.Px(8),0,0);shell.Controls.Add(entry,0,3);}
        var actions=new FlowLayoutPanel {AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Dock=DockStyle.Top,WrapContents=true,Margin=new Padding(0,Dpi.Px(8),0,0)};
        foreach(var button in buttons){button.AutoSize=true;button.MinimumSize=new Size(Dpi.Px(110),Dpi.Px(36));UiLayout.Responsive(button);actions.Controls.Add(button);}
        shell.Controls.Add(actions,0,4);
        void FitPage(){shell.Height=Math.Max(Dpi.Px(380),page.ClientSize.Height-page.Padding.Vertical);UiLayout.FitText(shell);}
        page.SizeChanged+=(_,_)=>FitPage(); page.VisibleChanged+=(_,_)=>{if(page.Visible)FitPage();};
        FitPage();
    }


    private RadioButton MakeModeRadio(string text, int x, int y)
    {
        var rb = new RadioButton
        {
            Text = text, Location = new Point(Dpi.Px(x), Dpi.Px(y)), Size = new Size(Dpi.Px(100), Dpi.Px(24)),
            ForeColor = Color.FromArgb(55, 65, 81), Font = Assets.Ui(9.5F),
        };
        rb.CheckedChanged += (_, _) =>
        {
            if (rb.Checked && !_initializing) _onModeChanged(CurrentMode());
        };
        return rb;
    }

    private int CurrentMode()
    {
        if (rbOff.Checked) return VoicePlayer.Off;
        if (rbTts.Checked) return VoicePlayer.Tts;
        return VoicePlayer.Human;
    }

    // ---- 真人语音页 ----

    private StudioTabPage BuildHumanPage()
    {
        var page = new StudioTabPage("真人语音") { Padding = new Padding(12) };

        var lblVol = new Label { Text = "音量", Location = new Point(Dpi.Px(14), Dpi.Px(16)), Size = new Size(Dpi.Px(50), Dpi.Px(20)) };
        lblHumanVol = new Label
        {
            Text = $"{MathF.Round(VoiceStore.Lib.HumanVolume * 100f):0}%",
            Location = new Point(Dpi.Px(66), Dpi.Px(16)), Size = new Size(Dpi.Px(44), Dpi.Px(20)), TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Assets.WarnOrange, Font = Assets.Ui(10F, FontStyle.Bold),
        };
        trkHumanVol = new StudioSlider { Location = new Point(Dpi.Px(116), Dpi.Px(12)), Size = new Size(Dpi.Px(260), Dpi.Px(28)), Minimum = 0, Maximum = 100, TickFrequency = 10 };
        trkHumanVol.Value = Math.Clamp((int)MathF.Round(VoiceStore.Lib.HumanVolume * 100f), 0, 100);
        trkHumanVol.ValueChanged += (_, _) =>
        {
            VoiceStore.Lib.HumanVolume = trkHumanVol.Value / 100f;
            lblHumanVol.Text = $"{trkHumanVol.Value}%";
            VoiceStore.Save();
        };

        var lblList = new Label
        {
            Text = "勾选 = 加入随机播放",
            Location = new Point(Dpi.Px(14), Dpi.Px(50)), Size = new Size(Dpi.Px(220), Dpi.Px(18)),
            ForeColor = Color.FromArgb(107, 114, 128), Font = Assets.Ui(8.5F),
        };

        clbClips = new CheckedListBox
        {
            Location = new Point(Dpi.Px(14), Dpi.Px(70)), Size = new Size(Dpi.Px(360), Dpi.Px(200)),
            CheckOnClick = true,
            DisplayMember = nameof(VoiceClip.Name),
            HorizontalScrollbar = true,
            Font = Assets.Ui(9F),
        };
        RefreshClips();
        clbClips.ItemCheck += (_, e) =>
        {
            if (clbClips.Items[e.Index] is VoiceClip clip)
                VoiceStore.SetClipEnabled(clip, e.NewValue == CheckState.Checked);
        };

        var btnAdd = new StudioButton { Text = "添加语音", Location = new Point(Dpi.Px(386), Dpi.Px(70)), Size = new Size(Dpi.Px(96), Dpi.Px(28)), FlatStyle = FlatStyle.Flat, Font = Assets.Ui(9F) };
        Assets.StyleOutline(btnAdd);
        btnAdd.Click += (_, _) => AddClipDialog();

        var btnPlay = new StudioButton { Text = "试听选中", Location = new Point(Dpi.Px(386), Dpi.Px(106)), Size = new Size(Dpi.Px(96), Dpi.Px(28)), FlatStyle = FlatStyle.Flat, Font = Assets.Ui(9F) };
        Assets.StyleOutline(btnPlay);
        btnPlay.Click += (_, _) =>
        {
            if (clbClips.SelectedItem is VoiceClip c) VoicePlayer.PlayClip(c);
        };

        var btnDel = new StudioButton { Text = "删除选中", Location = new Point(Dpi.Px(386), Dpi.Px(142)), Size = new Size(Dpi.Px(96), Dpi.Px(28)), FlatStyle = FlatStyle.Flat, Font = Assets.Ui(9F) };
        Assets.StyleOutline(btnDel);
        btnDel.Click += (_, _) =>
        {
            if (clbClips.SelectedItem is VoiceClip c) { VoiceStore.RemoveClip(c); RefreshClips(); }
        };

        page.Controls.Add(lblVol); page.Controls.Add(lblHumanVol); page.Controls.Add(trkHumanVol);
        page.Controls.Add(lblList); page.Controls.Add(clbClips);
        page.Controls.Add(btnAdd); page.Controls.Add(btnPlay); page.Controls.Add(btnDel);
        return page;
    }

    private void RefreshClips()
    {
        clbClips.Items.Clear();
        foreach (var c in VoiceStore.Lib.Clips)
            clbClips.Items.Add(c, c.Enabled);
    }

    private void AddClipDialog()
    {
        using var ofd = new OpenFileDialog
        {
            Title = "选择提示音文件（MP3 / WAV）",
            Filter = "音频文件|*.mp3;*.wav;*.ogg;*.m4a|所有文件|*.*",
            Multiselect = true,
        };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;
        foreach (var f in ofd.FileNames)
        {
            try { VoiceStore.AddClip(f); } catch { /* 单个失败跳过 */ }
        }
        RefreshClips();
    }

    // ---- TTS 页 ----

    private StudioTabPage BuildTtsPage()
    {
        var page = new StudioTabPage("TTS 语音") { Padding = new Padding(12) };

        var lblVol = new Label { Text = "音量", Location = new Point(Dpi.Px(14), Dpi.Px(16)), Size = new Size(Dpi.Px(50), Dpi.Px(20)) };
        lblTtsVol = new Label
        {
            Text = $"{MathF.Round(VoiceStore.Lib.TtsVolume * 100f):0}%",
            Location = new Point(Dpi.Px(66), Dpi.Px(16)), Size = new Size(Dpi.Px(44), Dpi.Px(20)), TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Assets.WarnOrange, Font = Assets.Ui(10F, FontStyle.Bold),
        };
        trkTtsVol = new StudioSlider { Location = new Point(Dpi.Px(116), Dpi.Px(12)), Size = new Size(Dpi.Px(260), Dpi.Px(28)), Minimum = 0, Maximum = 100, TickFrequency = 10 };
        trkTtsVol.Value = Math.Clamp((int)MathF.Round(VoiceStore.Lib.TtsVolume * 100f), 0, 100);
        trkTtsVol.ValueChanged += (_, _) =>
        {
            VoiceStore.Lib.TtsVolume = trkTtsVol.Value / 100f;
            lblTtsVol.Text = $"{trkTtsVol.Value}%";
            VoiceStore.Save();
        };

        var lblHint = new Label
        {
            Text = "自定义播报句子，切换时随机播放一句",
            Location = new Point(Dpi.Px(14), Dpi.Px(50)), Size = new Size(Dpi.Px(260), Dpi.Px(18)),
            ForeColor = Color.FromArgb(107, 114, 128), Font = Assets.Ui(8.5F),
        };

        lstLines = new ListBox { Location = new Point(Dpi.Px(14), Dpi.Px(70)), Size = new Size(Dpi.Px(360), Dpi.Px(200)), Font = Assets.Ui(9F) };
        RefreshLines();

        txtLine = new TextBox { Location = new Point(Dpi.Px(14), Dpi.Px(276)), Size = new Size(Dpi.Px(360), Dpi.Px(22)), Font = Assets.Ui(9F) };

        var btnAdd = new StudioButton { Text = "添加句子", Location = new Point(Dpi.Px(386), Dpi.Px(276)), Size = new Size(Dpi.Px(96), Dpi.Px(24)), FlatStyle = FlatStyle.Flat, Font = Assets.Ui(9F) };
        Assets.StyleOutline(btnAdd);
        btnAdd.Click += (_, _) =>
        {
            string line = txtLine.Text.Trim();
            if (line.Length == 0) return;
            VoiceStore.Lib.TtsLines.Add(line);
            VoiceStore.Save();
            txtLine.Clear();
            RefreshLines();
        };

        var btnDel = new StudioButton { Text = "删除选中", Location = new Point(Dpi.Px(386), Dpi.Px(106)), Size = new Size(Dpi.Px(96), Dpi.Px(28)), FlatStyle = FlatStyle.Flat, Font = Assets.Ui(9F) };
        Assets.StyleOutline(btnDel);
        btnDel.Click += (_, _) =>
        {
            if (lstLines.SelectedIndex >= 0)
            {
                VoiceStore.Lib.TtsLines.RemoveAt(lstLines.SelectedIndex);
                VoiceStore.Save();
                RefreshLines();
            }
        };

        var btnPlay = new StudioButton { Text = "试听选中", Location = new Point(Dpi.Px(386), Dpi.Px(142)), Size = new Size(Dpi.Px(96), Dpi.Px(28)), FlatStyle = FlatStyle.Flat, Font = Assets.Ui(9F) };
        Assets.StyleOutline(btnPlay);
        btnPlay.Click += (_, _) =>
        {
            if (lstLines.SelectedIndex >= 0)
                VoicePlayer.SpeakLine(VoiceStore.Lib.TtsLines[lstLines.SelectedIndex]);
        };

        page.Controls.Add(lblVol); page.Controls.Add(lblTtsVol); page.Controls.Add(trkTtsVol);
        page.Controls.Add(lblHint); page.Controls.Add(lstLines); page.Controls.Add(txtLine);
        page.Controls.Add(btnAdd); page.Controls.Add(btnDel); page.Controls.Add(btnPlay);
        return page;
    }

    private void RefreshLines()
    {
        lstLines.Items.Clear();
        foreach (var line in VoiceStore.Lib.TtsLines)
            lstLines.Items.Add(line);
    }
}
