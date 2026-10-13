using System.Reflection;

namespace CODMControllerProbe;

/// <summary>
/// 美术资源与配色规范（assets/DESIGN.md）：橘猫外壳 + geek 数据内核。
/// 资源以 EmbeddedResource 嵌入程序集，单文件发布后运行时也能加载。
/// </summary>
internal static class Assets
{
    // ---- 配色（暖色浅底 + 活力橙，AI 生图风格）----
    public static readonly Color CreamBg = Color.FromArgb(23, 25, 28);   // #FDF6E9 奶油底
    public static readonly Color CardWhite = Color.FromArgb(32, 35, 39); // #FFFFFF 卡片白
    public static readonly Color MainText = Color.FromArgb(242, 240, 235);  // #1F2937 主文字
    public static readonly Color SubText = Color.FromArgb(160, 164, 169);  // #6B7280 次要文字
    public static readonly Color PrimaryOrange = Color.FromArgb(239, 133, 76); // #FB923C 主操作橙
    public static readonly Color BorderGray = Color.FromArgb(53, 57, 62); // #E5E7EB 描边按钮边框
    public static readonly Color BorderText = Color.FromArgb(242, 240, 235); // #374151 描边按钮文字
    public static readonly Color OkGreen = Color.FromArgb(0x10, 0xB9, 0x81);  // #10B981 成功/运行绿
    public static readonly Color WarnOrange = Color.FromArgb(0xF5, 0x9E, 0x0B); // #F59E0B 数值/警告橙
    public static readonly Color ErrRed = Color.FromArgb(0xEF, 0x44, 0x44);   // #EF4444 错误红
    public static readonly Color CanvasDark = Color.FromArgb(26, 28, 31); // #111827 曲线画布深色
    public static readonly Color CurveBlue = Color.FromArgb(0x60, 0xA5, 0xFA); // #60A5FA 链路青蓝
    public static readonly Color AccentPurple = Color.FromArgb(0x8B, 0x7F, 0xD4); // #8B7FD4 蓝紫点缀

    /// <summary>从程序集内嵌资源读取图片。</summary>
    public static Image LoadImage(string logicalName)
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException($"内嵌资源缺失: {logicalName}");
        return Image.FromStream(stream);
    }

    /// <summary>
    /// 界面字体：中文优先微软雅黑 UI，英文 Segoe UI 回退。
    /// 启动时探测一次真实可用字体名，避免 GDI+ 静默回退导致界面字体不确定。
    /// 数据区保持等宽 Consolas 不变。
    /// </summary>
    private static readonly string _uiFace = ProbeFont();

    private static string ProbeFont()
    {
        string[] candidates = { "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI", "幼圆", "YouYuan" };
        foreach (var name in candidates)
        {
            try
            {
                using var f = new Font(name, 9f);
                if (f.Name == name) return name; // GDI+ 回退时 Name 会变成实际字体名
            }
            catch { /* 尝试下一个 */ }
        }
        return "Microsoft YaHei UI";
    }

    /// <summary>统一字体入口：全局字号 +1.25pt（窗口与间距已同步放大，保证可读性与高级感）。</summary>
    public static Font Ui(float size, FontStyle style = FontStyle.Regular) => new(_uiFace, size, style);

    /// <summary>从程序集内嵌资源读取图标（橘猫 app.ico）。</summary>
    public static Icon LoadIcon(string logicalName = "assets.app.ico")
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException($"内嵌资源缺失: {logicalName}");
        return new Icon(stream);
    }

    /// <summary>橘猫探头装饰图（右下角）。</summary>
    public static Image CatHead() => LoadImage("assets.app-128.png");

    /// <summary>卡通玩家头像（拿手柄玩游戏的小卡通人物，顶部品牌位）。</summary>
    public static Image CharacterHead() => LoadImage("assets.character_head.png");

    /// <summary>卡通玩家小图（同一角色，右下角装饰位）。</summary>
    public static Image CharacterCorner() => LoadImage("assets.character_corner.png");

    /// <summary>应用图标（右下角品牌位，替换卡通角色）。</summary>
    public static Image AppIcon() => LoadImage("assets.app-128.png");

    /// <summary>主界面射击游戏主题背景图（暖米白+橙、中央留白衬内容）。</summary>
    public static Image Background() => LoadImage("assets.bg-shooter.jpg");

    /// <summary>解锁页微信赞赏码（固定码内嵌，随软件分发）。</summary>
    public static Image RewardQr() => LoadImage("assets.qr-reward.png");

    /// <summary>给控件设置圆角 Region（WinForms 圆角按钮/面板）。</summary>
    public static void Round(Control c, int radius)
    {
        if(c is StudioButton)return; // Custom buttons paint their own corners at their current size.
        using var path = RoundPath(new Rectangle(0, 0, c.Width, c.Height), radius);
        var old=c.Region; c.Region = new Region(path); old?.Dispose();
    }

    /// <summary>生成圆角矩形路径（供 Region / 自绘描边复用）。</summary>
    public static System.Drawing.Drawing2D.GraphicsPath RoundPath(Rectangle r, int radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        int d = radius * 2;
        if (d > r.Width) d = r.Width;
        if (d > r.Height) d = r.Height;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d - 1, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d - 1, r.Bottom - d - 1, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d - 1, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>把按钮做成"主按钮"：橙色底白字圆角，悬停亮橙提亮、按下更深（明显的光标反馈）。</summary>
    public static void StylePrimary(Button b, int radius = 10)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.BackColor = PrimaryOrange;
        b.ForeColor = StudioTheme.Background;
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(255, 153, 96);    // 悬停亮橙（明显变亮）
        b.FlatAppearance.MouseDownBackColor = Color.FromArgb(234, 120, 24);    // 按下深橙
        Round(b, radius);
    }

    /// <summary>把按钮做成"描边按钮"：白底灰边灰字圆角，悬停淡橘。</summary>
    public static void StyleOutline(Button b, int radius = 8)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderColor = BorderGray;
        b.FlatAppearance.BorderSize = 1;
        b.BackColor = CardWhite;
        b.ForeColor = BorderText;
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(44, 47, 51);  // 悬停淡奶油橘
        Round(b, radius);
    }

    /// <summary>主界面深色背景专用按钮：半透明深蓝实底 + 亮青蓝边框（按钮感明确、一眼可辨，叠在深蓝背景上不突兀）；
    /// 悬停时底色明显提亮成淡橙，离开自动恢复（明确的光标移入/移出反馈）。</summary>
    public static void StyleOutlineDark(Button b, int radius = 8) => StyleOutline(b,radius);

    /// <summary>胶囊档位按钮三态：选中=橙实心白字；普通=白底描边；锁定=灰底灰字。</summary>
    public static void StyleCapsule(Button b, bool active, bool locked)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 1;
        if (active)
        {
            b.BackColor = PrimaryOrange;
            b.ForeColor = StudioTheme.Background;
            b.FlatAppearance.BorderColor = PrimaryOrange;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(255, 153, 96);
        }
        else if (locked)
        {
            b.BackColor = Color.FromArgb(240, 240, 240);   // 灰底
            b.ForeColor = Color.FromArgb(150, 150, 150);  // 灰字（不可用）
            b.FlatAppearance.BorderColor = Color.FromArgb(220, 220, 220);
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(240, 240, 240);
        }
        else
        {
            b.BackColor = CardWhite;
            b.ForeColor = BorderText;
            b.FlatAppearance.BorderColor = BorderGray;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(44, 47, 51);
        }
        Round(b, 13);
    }

    /// <summary>浅色圆角卡片面板（分组/状态区用）。</summary>
    public static void Card(Panel p, int radius = 12)
    {
        p.BackColor = CardWhite;
        Round(p, radius);
    }

    /// <summary>给窗口套用基础皮肤：奶油白背景 + 橘猫图标。</summary>
    public static void Skin(Form form)
    {
        form.BackColor = CreamBg;
        try { form.Icon = LoadIcon(); } catch { /* 图标加载失败不影响使用 */ }
    }
}
