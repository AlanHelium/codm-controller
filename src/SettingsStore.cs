using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace CODMControllerProbe;

/// <summary>
/// 用户设置持久化：反死区 + 曲线 + 轴向灵敏度 + 键位修正，存到 %AppData%\CODMControllerProbe\settings.json。
/// 与 exe 位置无关 → 版本更新、换目录、装到其他路径都不丢设置。
/// 序列化的是可读数据（曲线控制点 + 灵敏度），加载时重建 FeelCurve（切线由 Eval 自动重算）。
/// </summary>
public class AppSettings
{
    public CurveData Left { get; set; } = new();
    public CurveData Right { get; set; } = new();
    public CurveData RightAds { get; set; } = new(); // 开镜（按住 LT）专用右摇杆

    public int MapJump { get; set; } = 0x1000;
    public int MapCrouch { get; set; } = 0x2000;
    public int MapReload { get; set; } = 0x4000;
    public int MapSwitch { get; set; } = 0x8000;
    public int MapAim { get; set; } = KeyMap.LT;
    public int MapShoot { get; set; } = KeyMap.RT;
    public int MapTactical { get; set; } = 0x0100;
    public int MapLethal { get; set; } = 0x0200;

    /// <summary>切换光标/输入模式的组合键位掩码（默认 L3+R3 = 0x00C0，可在键位修正中自定义）。</summary>
    public ushort ComboMask { get; set; } = 0x00C0;

    /// <summary>切换提示语音模式：0=关 1=TTS 2=真人（VoicePlayer 常量）。</summary>
    public int VoiceMode { get; set; } = 1;

    /// <summary>Pro 版解锁标记（正式版由激活码设置，调试期由版本按钮切换）。</summary>
    public bool ProUnlocked { get; set; }

    /// <summary>版本档位：0=免费版 1=高级版 2=Pro 版（正式版由激活码设置，调试期由版本按钮切换）。</summary>
    public int VersionLevel { get; set; }

    /// <summary>
    /// 高级版在档位基础上的横纵微调记忆（fine tune）：
    /// [0]=左摇杆X [1]=左摇杆Y [2]=右腰射X [3]=右腰射Y [4]=右开镜X [5]=右开镜Y。
    /// 叠加在当前档位的出厂缩放曲线上，切换档位后仍生效；
    /// 但不会改写档位本身数值（档位默认值始终是出厂调校）。
    /// 免费版不开放（固定中档、无微调）；Pro 版直接用自定义曲线，无需此值。
    /// </summary>
    public float[] FineTuneGains { get; set; } = { 1f, 1f, 1f, 1f, 1f, 1f };

    /// <summary>长按 B 触发趴下（左 Ctrl）的阈值毫秒；免费版锁定默认值，高级版/Pro 可调。</summary>
    public int HoldProneDelayMs { get; set; } = 150;

    /// <summary>快速趴下开关：true=长按 B 注入左 Ctrl（快速趴下）；false=禁用，B 全程走原生。</summary>
    public bool EnableFastProne { get; set; } = true;

    /// <summary>核弹连杀组合键开关：true=长按 LB+RB 向游戏注入一次 J 键（触发核弹连杀奖励，注入方式同 U 键直投）；
    /// false=禁用，LB/RB 全程走原生转发。</summary>
    public bool EnableNukeCombo { get; set; } = true;

    /// <summary>核弹连杀长按判定阈值毫秒（两肩键同时按住超此时长触发 J，防误触）；独立可调，不与趴下/交互共用。</summary>
    public int NukeHoldMs { get; set; } = 400;

    /// <summary>快速交互长按判定阈值毫秒（触发键按住超此时长注入 F）；独立可调，不与趴下/核弹共用。</summary>
    public int InteractHoldMs { get; set; } = 150;

    /// <summary>视图键长按 → Tab（打开记分表）。true=长按 View 超阈值向游戏注入 Tab 按下（松开抬起），期间暂停 Share 原生转发。</summary>
    public bool EnableViewHoldTab { get; set; } = true;
    public int ViewHoldMs { get; set; } = 200;

    /// <summary>推左摇杆（&gt;90%）把屏幕光标移到左上角：重生后光标回到屏幕中央会挡视野，推杆时自动避让。
    /// true=启用避让；false=左摇杆不碰光标（部分玩家习惯完全由自己控制切光标时机）。</summary>
    public bool NudgeCursor { get; set; } = true;

    /// <summary>一键交换肩键与扳机：LT↔LB、RT↔RB（对应正作"肩键/扳机互换"设置）。
    /// true=按 LT 触发 L1、按 LB 触发 L2、按 RT 触发 R1、按 RB 触发 R2；false=原生对应。</summary>
    public bool SwapShoulderTriggers { get; set; } = false;

    /// <summary>切光标触发方式：0=组合键（L3+R3，默认）；1=单键一键触发（Xbox 系=Home 西瓜键 / DS 系=触摸板）。
    /// 单键模式下 DS 触摸板不再注入 Tab（记分表请用视图键长按）。</summary>
    public int CursorSwitchMode { get; set; } = 0;

    /// <summary>快速交互开关：true=长按触发键注入 F（游戏交互键，同快速趴下规格：短按保持原生、长按超阈值直投 F）。
    /// false=禁用，触发键全程走原生转发。</summary>
    public bool EnableFastInteract { get; set; } = false;

    /// <summary>快速交互触发键（默认 LB，可在键位修正中录制）：按钮位或 LT/RT 虚拟位。</summary>
    public int InteractKey { get; set; } = 0x0100;

    /// <summary>近战/标点绑定键（DS 系 R3 原生即近战+标点同一键）：
    /// 默认 0x0080（R3）——虚拟 DS4 的 R3 位输出给游戏，游戏手柄模式原生触发近战/标点；
    /// 用户可在键位修正中重绑为其它键（如 L3），绑定键按下时输出 R3 位、该键原生位被屏蔽。
    /// 若用户把 R3 改作他用（如下蹲），R3 不再输出原生近战/标点位，避免双触发。</summary>
    public int MeleeKey { get; set; } = 0x0080;

    /// <summary>左摇杆奔跑：左摇杆轴保持全向移动；按下 L3（左摇杆按键）注入一次 H（静步/奔跑切换键）。
    /// true=启用；false=禁用（左摇杆只转发摇杆数据）。</summary>
    public bool EnableLeftStickRun { get; set; } = true;

    /// <summary>自动奔跑：开启后推杆总幅度跨越 RunThreshold 的边沿自动注入一次 H（轻推走路/重推奔跑），无需按 L3；
    /// 关闭后仅按 L3 手动切换。物理 L3 两种模式下都始终可用。</summary>
    public bool AutoRun { get; set; } = true;

    /// <summary>游戏默认开启奔跑模式（H 按一次=关闭奔跑，再按=开启）：CODM 多数版本手柄默认直接奔跑，
    /// 此时自动奔跑的"推满"边沿无需按 H（角色已在跑），只在"回落"边沿按 H 切回走路。
    /// false=游戏默认走路模式（按 H 才开启奔跑）。方向调反时切换此项即可。</summary>
    public bool RunGameDefaultRun { get; set; } = true;

    /// <summary>回中自动切回走路：开启（默认）时，推满切到奔跑后，回落到阈值以下自动按一次 H 切回走路（轻推走路）；
    /// 关闭时回中不触发 H，奔跑模式保持，下次推杆直接冲刺，走路靠手动按 L3 切换。</summary>
    public bool RunReturnWalk { get; set; } = true;

    /// <summary>左摇杆奔跑触发阈值百分比 50~100（默认 90）：推杆总幅度 ≥ 阈值视为"重推"（自动触发 H）。</summary>
    public int RunThreshold { get; set; } = 90;

    /// <summary>枪声震动仅限游戏前台触发：后台/切到其它窗口时不做音频检测，避免桌面声音误触发震动。
    /// 前台窗口判定：已"识别当前游戏"则精确匹配该进程；未识别时自动排除系统/本程序窗口。</summary>
    public bool RumbleFocusOnly { get; set; } = true;

    /// <summary>退出时清理驱动（ViGEmBus + HidHide）：默认关闭 —— 关闭软件只是关闭，绝不卸载驱动/请求重启。
    /// 需要"彻底清除后玩 COD 正作"时：手动开启此选项，或直接用配套的卸载程序。开启后卸载全程静默
    /// （/norestart），若驱动要求重启只记录提示、绝不自动重启电脑。配置保存在 %AppData%，不受影响。</summary>
    public bool CleanDriversOnExit { get; set; } = false;

    // ================= 实验性功能（默认全关 · 不写宣传 · 可能不稳定） =================
    /// <summary>Gyro 体感瞄准总开关（实验性）：Sony 六轴手柄（DS4/DualSense）陀螺仪角速度叠加到右摇杆。
    /// 默认关闭；开启后仅在有 Sony HID 手柄且此开关为 true 时生效，Xbox/第三方手柄无陀螺仪不受影响。</summary>
    public bool GyroEnabled { get; set; } = false;

    /// <summary>实验：左摇杆 → WASD（外圈 Shift 冲刺），屏蔽左摇杆轴输出（默认关）。</summary>
    public bool ExpWasdMove { get; set; } = false;

    /// <summary>实验 WASD 外圈阈值 50~99（默认 85%）：幅度达到该值按下 Shift 冲刺。</summary>
    public int ExpWasdThreshold { get; set; } = 85;

    /// <summary>实验：模拟触屏滑动（模拟器场景），屏蔽左摇杆轴输出（默认关）。</summary>
    public bool ExpTouchMove { get; set; } = false;

    /// <summary>实验触屏滑动半径 px（默认 120）：摇杆满推时触点移动距离。</summary>
    public int ExpTouchRadius { get; set; } = 120;

    /// <summary>实验触屏灵敏度 50~150（默认 100）：滑动距离缩放。</summary>
    public int ExpTouchSens { get; set; } = 100;

    /// <summary>输出刷新率 60~1000Hz（默认 1000）：控制软件向虚拟 DS4 的注入频率。真实生效，非显示项。</summary>
    public int OutputHz { get; set; } = 1000;

    /// <summary>Gyro 灵敏度倍率 0.2~3.0（默认 1.0）：转动幅度 → 视角速度的缩放。</summary>
    public float GyroSens { get; set; } = 1f;

    /// <summary>Gyro 死区（raw 角速度单位，默认 600）：静止时低于该值的漂移忽略，值越大越稳。</summary>
    public int GyroDeadzone { get; set; } = 600;

    /// <summary>Gyro 平滑 0~0.8（默认 0.3）：一阶低通系数，越大视角越稳但略迟钝。</summary>
    public float GyroSmooth { get; set; } = 0.3f;

    /// <summary>Gyro 水平方向翻转（默认关）：方向反了勾此项。</summary>
    public bool GyroInvertX { get; set; } = false;

    /// <summary>Gyro 垂直方向翻转（默认关）：方向反了勾此项。</summary>
    public bool GyroInvertY { get; set; } = false;

    // ================= 震动转发 =================
    /// <summary>震动总开关：游戏 → 虚拟 DS4 的震动指令是否转发到实体手柄。</summary>
    public bool RumbleOn { get; set; } = true;
    /// <summary>震动强度百分比 0~100：按比例缩放转发到实体手柄的马达。</summary>
    public int RumbleStrength { get; set; } = 100;

    /// <summary>枪声触发震动：采集系统音频，检测到爆音（能量突增）时震动实体手柄。</summary>
    public bool RumbleByShot { get; set; } = true;

    /// <summary>枪声触发的力度下限（50~100%，默认 75%）：开枪即有这个力度起步，越响越强。</summary>
    public int RumbleMinLevel { get; set; } = 75;

    /// <summary>低频增强：对 40~300Hz 频段能量加权参与枪声判定与强度（重型枪/爆炸/部分消音枪更易触发）。</summary>
    public bool RumbleLowBoost { get; set; } = true;

    /// <summary>枪声检测灵敏度：瞬时能量超过"基线 × 倍数"视为枪声，2~8，越大越灵敏。</summary>
    public float ShotSensitivity { get; set; } = 3f;

    /// <summary>
    /// 灵敏度档位（高级版可用）：0~19 = 档位 1~20，腰射 32%~100% 非线性（低档区分度大、高档细腻）；
    /// 开镜 65%~100% 非线性（低档缓、高档密）。免费版锁定为"中档"（9，第 10 档），用户不可选择；
    /// 高级版可选 20 档并在档位基础上横纵微调（fine tune 另存，不覆盖档位默认值）；
    /// Pro 版用自定义曲线，不读此值。默认 9（第 10 档中档，与出厂调校一致；避免"恢复默认即拉满 20"）。
    /// </summary>
    public int FreeSensLevel { get; set; } = 9;

    /// <summary>
    /// ADS（开镜）灵敏度倍率：0.50~1.50，默认 0.90。作用在开镜派生曲线上（= 开镜档位系数 × 此倍率），
    /// 对应正作 "ADS Sensitivity Multiplier"：&lt;1 开镜更稳（多数玩家 0.8~0.9）、=1 与腰射同速、&gt;1 开镜更快。
    /// 高级版/Pro 可调；免费版锁定默认 0.90。
    /// </summary>
    public float AdsMultiplier { get; set; } = 0.9f;

    /// <summary>界面语言：0=中文（默认） 1=English。</summary>
    public int Language { get; set; }

    /// <summary>
    /// 按键映射区：手柄按键（含 Home/Logo 等全部按钮位 + LT/RT 扳机）→ 键盘任意键。
    /// 绑定后该手柄键按下时向游戏注入对应键盘键（PostMessage 直投，同 U/J 通道），
    /// 该键的原生手柄输出被替代（动作跟着键位走，不双触发）。支持 LB→Q、LS→B 等任意组合。
    /// 同一手柄键只允许一个键盘键（组合多键走实验性功能）；同一键盘键可被多个手柄键复用。
    /// </summary>
    public List<PadKeyBind> PadKeyMap { get; set; } = new();

    /// <summary>实验：右摇杆（R3）同时注入 V + B 两个键（组合注入示例，V/B 功能不冲突）。默认关。</summary>
    public bool ExpRsCombo { get; set; } = false;

    /// <summary>游戏事件「切换武器射击模式」开关：触发后向游戏注入一次 B 键（键盘默认键）。默认关。</summary>
    public bool FireModeEnabled { get; set; } = false;

    /// <summary>切换武器射击模式长按判定阈值毫秒（单键=触发键按住超此时长；双键=组合键同按超此时长）。</summary>
    public int FireModeHoldMs { get; set; } = 250;

    /// <summary>切换武器射击模式触发方式：true=双键组合（两键同按超时触发一次 B）；false=单键（触发键长按超时触发一次 B）。</summary>
    public bool FireModeUseCombo { get; set; } = true;

    /// <summary>切换武器射击模式单键触发键（默认 Option——不占用动作/奔跑/近战键位，可在设置里录制更换）。</summary>
    public int FireModeKey { get; set; } = 0x0010;

    /// <summary>切换武器射击模式双键组合掩码（默认 0=未设置，需在设置里录制；避免与切光标 L3+R3 / 核弹 LB+RB 撞车）。</summary>
    public ushort FireModeCombo { get; set; } = 0;

    /// <summary>是否首次启动（true → 打开后展示使用引导；展示后立即置 false 保存）。</summary>
    public bool FirstRun { get; set; } = true;

    /// <summary>曲线数据是否为母版形态（归一化形状，总曲线）。true=新版保存（母版）；
    /// false=旧版保存（当时转发曲线 = 母版 × 档位），加载时按当时档位系数反解回母版。</summary>
    public bool CurvesAreMaster { get; set; } = false;

    /// <summary>手感封装：以 COD 作品 × 风格区分（当前精简为 COD20 × 动态/线性/标准 = 3 套）。
    /// 每套独立保存三根母版曲线（左摇杆 / 右·腰射 / 右·开镜），档位（1~20）在母版上按系数派生。</summary>
    public class CurvePack
    {
        public string Key { get; set; } = "";   // 稳定标识，如 "cod20-dyn"
        public string Name { get; set; } = "";  // 显示名，如 "COD20 · 动态"
        public CurveData Left { get; set; } = new();
        public CurveData Right { get; set; } = new();
        public CurveData RightAds { get; set; } = new();
    }


    /// <summary>3 套手感封装（母版形态）。空 → 首次启动/升级时由 EnsurePacks 填充直线 5 点默认。</summary>
    public List<CurvePack> Packs { get; set; } = new();

    /// <summary>当前激活封装 Key（运行/编辑目标）。出厂默认 = COD20 · 动态（免费版内置手感）。</summary>
    public string ActivePack { get; set; } = "cod20-dyn";

    /// <summary>曲线数据（控制点 [x,y] + 灵敏度）。</summary>
    public class CurveData
    {
        public List<float[]> Points { get; set; } = new();
        public float GainX { get; set; } = 1f;
        public float GainY { get; set; } = 1f;

        /// <summary>从控制点构造。</summary>
        public static CurveData Of(float gainX, float gainY, params float[][] pts)
        {
            var d = new CurveData { GainX = gainX, GainY = gainY };
            foreach (var p in pts) d.Points.Add(p);
            return d;
        }
        /// <summary>从 FeelCurve 复制当前状态。</summary>
        public static CurveData From(FeelCurve c)
        {
            var d = new CurveData { GainX = c.GainX, GainY = c.GainY };
            foreach (var p in c.Points) d.Points.Add(new[] { p.X, p.Y });
            return d;
        }

        /// <summary>重建 FeelCurve（切线会在首次 Eval 时自动重算）。</summary>
        public FeelCurve ToCurve()
        {
            var c = new FeelCurve { GainX = GainX, GainY = GainY };
            c.Points.Clear();
            foreach (var p in Points)
            {
                float x = Math.Clamp(p.Length > 0 ? p[0] : 0f, 0f, 1f);
                float y = Math.Clamp(p.Length > 1 ? p[1] : 0f, 0f, 1f);
                c.Points.Add(new System.Drawing.PointF(x, y));
            }
            // 保证结构合法：首点 X=0、末点 X=1、至少两点
            if (c.Points.Count == 0) c.Points.Add(new System.Drawing.PointF(0f, 0f));
            if (c.Points.Count == 1) c.Points.Add(new System.Drawing.PointF(1f, 1f));
            c.Points[0] = new System.Drawing.PointF(0f, c.Points[0].Y);
            c.Points[^1] = new System.Drawing.PointF(1f, c.Points[^1].Y);
            return c;
        }
    }

    /// <summary>
    /// 免费版出厂默认手感（COD 正作转译，2026-10-05 更新）：
    /// 默认激活封装 = COD20 · 动态（p=1.45 加速曲线，轻推慢、推满快，正作 Dynamic 手感）。
    /// 左摇杆：反死区 15%、恒直线、灵敏度 1.0/1.0（跑动/移动，线性最直觉）。
    /// 右·腰射：反死区 22%、动态曲线（25%→32.5%、50%→50.5%、75%→73.4%）、灵敏度 1.0/0.9。
    /// 右·开镜：反死区 24%、动态曲线（25%→34.2%、50%→51.8%、75%→74.1%）、灵敏度 1.0/0.9。
    /// 3 套封装（COD20 × 动态/线性/标准）详见 BuildDefaultPacks。
    /// 键位：A=跳跃 B=下蹲 X=换弹 Y=切枪（XInput 默认）。
    /// 长按 B 快速趴下默认 150ms（Shift 奔跑功能已按用户要求移除，奔跑在游戏内设置）。
    /// 新用户（无配置文件）第一次启动即用此套；Pro 版可自定义，免费版锁定。
    /// </summary>
    public static AppSettings CreateDefault()
    {
        return new AppSettings
        {
            Left = CurveData.Of(1f, 1f,
                new[] { 0f, 0.15f },
                new[] { 1f, 1f }),
            Right = CurveData.Of(1f, 0.9f,
                new[] { 0f, 0.22f },
                new[] { 0.35176992f, 0.39615384f },
                new[] { 0.6017699f, 0.6f },
                new[] { 1f, 1f }),
            RightAds = CurveData.Of(1f, 0.9f,
                new[] { 0f, 0.25f },
                new[] { 0.2987805f, 0.4f },
                new[] { 0.80487806f, 0.8f },
                new[] { 1f, 1f }),
            MapJump = 0x1000,
            MapCrouch = 0x2000,
            MapReload = 0x4000,
            MapSwitch = 0x8000,
            MapAim = KeyMap.LT,
            MapShoot = KeyMap.RT,
            MapTactical = 0x0100,
            MapLethal = 0x0200,
            ComboMask = 0x00C0, // 切换光标组合键默认 L3+R3（LeftThumb|RightThumb）
            HoldProneDelayMs = 150,
            EnableFastProne = true,
            EnableNukeCombo = true,
            NukeHoldMs = 400,
            InteractHoldMs = 150,
            EnableViewHoldTab = true,
            ViewHoldMs = 200,
            SwapShoulderTriggers = false,
            EnableFastInteract = false,
            InteractKey = 0x4000, // 快速交互默认触发键 = X（键位修正可录制更改）
            MeleeKey = 0x0080, // 近战/标点默认绑定 R3（DS 原生近战+标点键）
            EnableLeftStickRun = true,
            AutoRun = true,
            RunGameDefaultRun = true,
            RunReturnWalk = true,
            RunThreshold = 90,
            FreeSensLevel = 9, // 出厂默认档位 = 第 10 档（中档，与免费版锁定档一致）；避免"恢复默认即 20 拉满"
            AdsMultiplier = 0.9f,
            VersionLevel = 0,
            FineTuneGains = new[] { 1f, 1f, 1f, 1f, 1f, 1f },
            Packs = BuildDefaultPacks(),
            ActivePack = "cod20-dyn", // 出厂默认 = COD20 · 动态（免费版内置手感）
        };
    }

    /// <summary>12→3 套封装精简：只保留 COD20 × 动态/线性/标准（作品维度暂移除，未来多参数化再加回）。
    /// 每套三根母版曲线 = 各自反死区起点的幂函数曲线（y=anti+x^p×(1-anti)，5 个控制点）。
    /// 起点沿用调教定稿：左 15% / 腰射 22% / 开镜 24%；灵敏度 左 1.0/1.0、腰射/开镜 1.0/0.9（纵向略缓）。</summary>
    private static List<CurvePack> BuildDefaultPacks()
    {
        var packs = new List<CurvePack>();
        // 动态 = S 形转火曲线（反切感：微调更稳、中段陡起、甩枪更冲）；线性/标准 = 幂函数（跟手/均衡）
        packs.Add(new CurvePack
        {
            Key = "dyn",
            Name = "COD20 · 动态",
            Left = CurveData.Of(1f, 1f, StylePoints(0.15f, 1.00f)),
            Right = CurveData.Of(1f, 0.9f, StylePointsS(0.22f)),
            RightAds = CurveData.Of(1f, 0.9f, StylePointsS(0.25f)),
        });
        (string key, string name, float exp)[] styles =
        {
            ("lin", "COD20 · 线性", 1.00f),
            ("std", "COD20 · 标准", 1.15f),
        };
        foreach (var (key, name, exp) in styles)
            packs.Add(new CurvePack
            {
                Key = key,
                Name = name,
                Left = CurveData.Of(1f, 1f, StylePoints(0.15f, 1.00f)),
                Right = CurveData.Of(1f, 0.9f, StylePoints(0.22f, exp)),
                RightAds = CurveData.Of(1f, 0.9f, StylePoints(0.25f, exp)),
            });
        return packs;
    }

    /// <summary>COD 正作手感转译：y = anti + x^p × (1-anti)，x∈[0,1]，5 个控制点（0/25/50/75/100%）。</summary>
    private static float[][] StylePoints(float anti, float p)
    {
        float y(float x) => anti + MathF.Pow(x, p) * (1f - anti);
        return new[]
        {
            new[] { 0f, y(0f) },
            new[] { 0.25f, y(0.25f) },
            new[] { 0.5f, y(0.5f) },
            new[] { 0.75f, y(0.75f) },
            new[] { 1f, y(1f) },
        };
    }

    /// <summary>动态 S 形（转火向，反切感）：相对起点反死区的 5 点 = 0 / 10% / 46% / 81% / 100%。
    /// 低段明显更缓（精细瞄准稳）→ 中段陡起（跟手加速）→ 高段冲（甩枪/转火快于标准），全程单调递增。</summary>
    private static float[][] StylePointsS(float anti)
    {
        return new[]
        {
            new[] { 0f, anti },
            new[] { 0.25f, anti + 0.10f * (1f - anti) },
            new[] { 0.5f, anti + 0.46f * (1f - anti) },
            new[] { 0.75f, anti + 0.81f * (1f - anti) },
            new[] { 1f, 1f },
        };
    }

    /// <summary>反死区起点的直线 5 点：x = 0/0.25/0.5/0.75/1，y = anti + x×(1-anti)。</summary>
    private static float[][] LinePoints(float anti)
    {
        float y(float x) => anti + x * (1f - anti);
        return new[]
        {
            new[] { 0f, y(0f) },
            new[] { 0.25f, y(0.25f) },
            new[] { 0.5f, y(0.5f) },
            new[] { 0.75f, y(0.75f) },
            new[] { 1f, y(1f) },
        };
    }

    /// <summary>确保 Packs 非空且含 3 套（旧配置/首次启动迁移）；ActivePack 非法时回退 cod20-dyn。</summary>
    public static void EnsurePacks(AppSettings s)
    {
        if (s.Packs is not { Count: 3 })
            s.Packs = BuildDefaultPacks();
        if (string.IsNullOrEmpty(s.ActivePack) || !s.Packs.Exists(p => p.Key == s.ActivePack))
            s.ActivePack = "cod20-dyn"; // 出厂默认 = COD20 · 动态
        foreach (var p in s.Packs)
        {
            if (p.Left.Points.Count < 2) p.Left = CurveData.Of(1f, 1f, LinePoints(0.15f));
            if (p.Right.Points.Count < 2) p.Right = CurveData.Of(1f, 0.9f, LinePoints(0.22f));
            if (p.RightAds.Points.Count < 2) p.RightAds = CurveData.Of(1f, 0.9f, LinePoints(0.25f));
        }
        // 动态封装形状迁移：旧版为幂函数（中段≈0.51），新版为 S 形转火曲线（中段≈0.58）。
        // 检测中段点 <0.55 即旧形状 → 重建为 S 形，让老配置自动拿到新动态手感。
        var dyn = s.Packs.Find(p => p.Key == "dyn");
        if (dyn != null && dyn.Right.Points.Count == 5 && dyn.Right.Points[2][1] < 0.55f)
        {
            dyn.Right = CurveData.Of(1f, 0.9f, StylePointsS(0.22f));
            dyn.RightAds = CurveData.Of(1f, 0.9f, StylePointsS(0.25f));
        }
    }
}

/// <summary>
/// 一条按键映射：手柄键位（XInput 按钮位或 LT/RT 虚拟位）→ 键盘键（VK + Set-1 扫描码 + 显示名）。
/// 序列化存短名（KeyName）以保持 JSON 可读；加载时经 KeyboardKeys.ByName 还原 VK/Scan。
/// </summary>
public sealed class PadKeyBind
{
    /// <summary>手柄键位：按钮位（A/B/X/Y/LB/RB/L3/R3/方向键/View/Option/Logo）或 KeyMap.LT / KeyMap.RT。</summary>
    public int PadBit { get; set; }

    /// <summary>键盘键显示名（KeyboardKeys 表内短名，如 "Q"、"左Ctrl"）。</summary>
    public string KeyName { get; set; } = "";

    /// <summary>由 KeyName 还原的注入参数（加载时填充；序列化不保存）。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public ushort Vk { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public ushort Scan { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool Extended { get; set; }

    /// <summary>从键盘键构造一条映射（设置窗口录制后立即生成）。</summary>
    public static PadKeyBind Of(int padBit, KeyboardKey k) => new()
    {
        PadBit = padBit,
        KeyName = k.Name,
        Vk = k.Vk,
        Scan = k.Scan,
        Extended = k.Extended,
    };

    /// <summary>加载时还原注入参数；表外名称视为无效（返回 null）。</summary>
    public static PadKeyBind? Resolve(PadKeyBind raw)
    {
        if (!SettingsStore.IsValidBind(raw.PadBit)) return null;
        var k = KeyboardKeys.ByName(raw.KeyName);
        if (k == null) return null;
        raw.Vk = k.Vk;
        raw.Scan = k.Scan;
        raw.Extended = k.Extended;
        return raw;
    }
}

/// <summary>AppData 下的 JSON 读写（线程安全：本工具单实例，无并发写）。</summary>
public static class SettingsStore
{
    /// <summary>键位绑定合法集合：按钮位（A/B/X/Y/LB/RB/L3/R3/方向键/Start/Option/View/Logo）+ 扳机虚拟位。</summary>
    internal static bool IsValidBind(int v) =>
        v is 0x0001 or 0x0002 or 0x0004 or 0x0008   // 方向键 ↑↓←→
        or 0x1000 or 0x2000 or 0x4000 or 0x8000     // A/B/X/Y
        or 0x0100 or 0x0200 or 0x0040 or 0x0080     // LB/RB/L3/R3
        or 0x0010 or 0x0020 or 0x0400               // Option/View/Logo
        or KeyMap.LT or KeyMap.RT;

    public static string Path_ =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CODMControllerProbe", "settings.json");

    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(Path_))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path_));
                if (s != null) return s;
            }
        }
        catch { /* 配置文件损坏时回退出厂默认 */ }
        return AppSettings.CreateDefault(); // 无配置/损坏 → 免费版出厂默认
    }

    public static void Save(AppSettings s)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path_)!);
            File.WriteAllText(Path_, JsonSerializer.Serialize(s, Opts));
        }
        catch { /* 保存失败不影响运行（下次启动回退默认） */ }
    }

    /// <summary>导出配置到用户选择的文件（手感分享 / 备份）。</summary>
    public static bool SaveTo(string file, AppSettings s)
    {
        try
        {
            AppSettings.EnsurePacks(s);
            File.WriteAllText(file, JsonSerializer.Serialize(s, Opts));
            return true;
        }
        catch { return false; }
    }

    /// <summary>从用户选择的文件导入配置；失败/内容非法返回 null（由调用方提示并保持原设置）。</summary>
    public static AppSettings? LoadFrom(string file)
    {
        try
        {
            if (!File.Exists(file)) return null;
            var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(file));
            if (s == null) return null;
            s.VersionLevel = s.VersionLevel >= 2 ? 2 : 0; // 两档制：0=免费版（全调节开放） 2=Pro 版（+语音/震动）；旧"高级版(1)"并入免费
            s.FreeSensLevel = Math.Clamp(s.FreeSensLevel, 0, 19);
            s.AdsMultiplier = Math.Clamp(s.AdsMultiplier is 0 ? 0.9f : s.AdsMultiplier, 0.5f, 1.5f);
            s.RunThreshold = s.RunThreshold is >= 50 and <= 100 ? s.RunThreshold : 90;
            s.InteractKey = SettingsStore.IsValidBind(s.InteractKey) ? s.InteractKey : 0x0100;
            s.MapAim = IsValidBind(s.MapAim) ? s.MapAim : KeyMap.LT;
            s.MapShoot = IsValidBind(s.MapShoot) ? s.MapShoot : KeyMap.RT;
            s.MapTactical = IsValidBind(s.MapTactical) ? s.MapTactical : 0x0100;
            s.MapLethal = IsValidBind(s.MapLethal) ? s.MapLethal : 0x0200;
            // 按键映射区：逐条解析（无效条目丢弃）；同一手柄键只保留第一条，避免双映射
            var padMap = new List<PadKeyBind>();
            var seenPad = new HashSet<int>();
            if (s.PadKeyMap is { Count: > 0 })
            {
                foreach (var raw in s.PadKeyMap)
                {
                    if (raw == null || !seenPad.Add(raw.PadBit)) continue;
                    var resolved = PadKeyBind.Resolve(raw);
                    if (resolved != null) padMap.Add(resolved);
                }
            }
            s.PadKeyMap = padMap;
            s.FireModeHoldMs = s.FireModeHoldMs is >= 100 and <= 1200 ? s.FireModeHoldMs : 250;
            s.FireModeKey = IsValidBind(s.FireModeKey) ? s.FireModeKey : 0x0010;
            AppSettings.EnsurePacks(s);
            return s;
        }
        catch { return null; }
    }
}
