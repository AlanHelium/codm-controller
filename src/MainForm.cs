using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;

namespace CODMControllerProbe;

/// <summary>
/// v0.5：只做 DS4 协议。打开即运行。
/// 新增：① 键位修正（第三方手柄 A/B/X/Y 报告错位时重新绑定动作）；
///       ② 实体手柄自动隐藏（HidHide：游戏只看到虚拟 DS4，避免双设备冲突）。
/// </summary>
public partial class MainForm : Form
{
    // ---- 控件 ----
    private Button btnToggle = null!;
    private Button btnRemap = null!;
    private Label lblMode = null!;             // 输入模式状态显示（只读，切换仅用 L3+R3 组合键）
    private Button btnRumble = null!;          // 右侧列"震动"入口（三版本可见，进震动设置窗口）
    private Label _lblThanks = null!;       // 版本致谢/引导行（免费=引导升级，高级/Pro=感谢购买，无容器）
    private Label lblStatus = null!;
    private Label lblRaw1 = null!;
    private Label lblRaw2 = null!;
    private Label lblHidHide = null!;
    private Button btnVoice = null!;
    private Button btnVersion = null!;
    private Button btnSettings = null!;             // v0.2 极简主界面：一切设置收进"设置"窗口
    private Button btnChangelog = null!;            // 右上角"更新日志"：历史更新日志（改了什么 / 为什么）
    private GlassPanel _tipCard = null!;            // Buy me Coffee 赞赏卡（右下角，免费版可见）
    private Label _lblTipCardTitle = null!;         // 赞赏卡标题
    private Label _lblTipCardSub = null!;           // 赞赏卡副文案
    private PictureBox _picTipQr = null!;           // 赞赏卡内嵌微信赞赏码
    private Button btnUpgrade = null!;              // 主页"升级 Pro 版"入口（免费版可见，点击打开解锁窗口）

    // ---- 手感（反死区 + 响应曲线）----
    // 出厂默认 = 免费版调教定稿（AppSettings.CreateDefault）：左 10%、右腰射 22%、右开镜 24%，
    // 灵敏度右 1.0/0.9；调试期通过"手感设置"对话框实时编辑；Pro 版开放，免费版锁定出厂预设。
    private static readonly AppSettings _factoryFeel = AppSettings.CreateDefault();
    // 母版曲线（总指挥）：归一化手感形状（反死区起点 + 控制点），持久化于激活封装的 Left/Right/RightAds。
    // 3 套封装（COD20 × 动态/线性/标准）各自独立；所有 1~20 档灵敏度都由母版按档位系数派生：
    // 改母版任意点/反死区 → 各档按各自可用空间等比例传播（低档变化量小、高档变化量大），起点全局统一。
    private List<AppSettings.CurvePack> _packs = AppSettings.CreateDefault().Packs;
    private string _activePackKey = "cod20-lin";
    private FeelCurve _masterLeft = _factoryFeel.Left.ToCurve();
    private FeelCurve _masterRight = _factoryFeel.Right.ToCurve();
    private FeelCurve _masterRightAds = _factoryFeel.RightAds.ToCurve();
    // 转发曲线（运行时）：母版 × 当前档位系数，游戏输入走这条（画布显示这条）。
    private FeelCurve _curveLeft = _factoryFeel.Left.ToCurve();
    private FeelCurve _curveRight = _factoryFeel.Right.ToCurve();
    // 开镜专用右摇杆曲线（按住 LT 时启用）：默认与腰射相同，手感窗口可分别编辑。
    // 射击游戏常见需求：开镜时轻推精细瞄准（低灵敏度曲线），腰射时快速转向（灵敏曲线）。
    private FeelCurve _curveRightAds = _factoryFeel.RightAds.ToCurve();

    // ---- 输入模式（键鼠 / 手柄原生）----
    // CODM 模拟器默认按键鼠处理输入；切出光标后进入"手柄原生输入模式"（辅助瞄准大）。
    // 用户可自选：界面按钮切换，或游戏中双击方向键右键快速切换。
    // 切换动作 = 发送一次 Alt 键让游戏切光标 + 把光标移到屏幕角落（不挡视野）。
    // 注意：这是单次按键（切游戏输入模式），不是连发/宏。
    private bool _nativeInput = true;              // true=手柄原生模式（默认，辅助瞄准大）
    private int _voiceMode = VoicePlayer.Tts;       // 语音模式：0=关 1=TTS 2=真人（语音设置中选）
    private readonly int _versionLevel = 2;                      // 0=免费版（全部调节开放） 1=旧高级版（并入免费，兼容存量） 2=Pro 版（仅多语音+震动）
    private float[] _fineTuneGains = { 1f, 1f, 1f, 1f, 1f, 1f }; // 高级版档位基础上横纵微调记忆（不覆盖档位默认值）
    private bool _comboWasDown;                     // L3+R3 组合键边沿检测
    // 双击方向键左 → 注入 I 键（方式同 U：PostMessage 直投，无后续动作）
    private bool _dpadLeftWasDown;                  // DPadLeft 边沿检测
    private int _lastDpadLeftPressTick;             // 上次按下时刻（TickCount）
    // 长按 B → 左 Ctrl（趴下）：按住超阈值注入 Ctrl 按下，松开注入 Ctrl 抬起；
    // 长按期间暂停 B 的 DS4 Circle 转发，避免游戏收到双重趴下指令。
    private bool _bWasDown;                         // B 边沿检测
    private int _bDownTick;                         // B 按下时刻
    private bool _ctrlHeld;                         // 当前是否已注入 Ctrl 按住
    private int _holdProneDelayMs = 150;            // 快速趴下长按阈值（独立可调）
    private int _nukeHoldMs = 400;                  // 核弹连杀长按阈值（独立可调，防误触）
    private int _interactHoldMs = 150;              // 快速交互长按阈值（独立可调）
    private bool _enableViewHoldTab = true;          // 视图键长按 → Tab（打开记分表），默认开
    private bool _touchpadTab = true;                 // 触摸板按下 → Tab（DS4/DS5/DSE 专用），默认开
    private int _viewHoldMs = 200;                   // 视图键长按阈值（独立可调，防误触）
    private bool _viewWasDown, _viewHeld;
    private int _viewDownTick, _viewRepeatTick;
    private bool _touchpadWasDown, _touchpadHeld;
    private int _touchpadRepeatTick;
    private const ushort KEY_TAB_VK = 0x09;          // Tab
    private const ushort KEY_TAB_SCAN = 0x0F;        // Tab 的 Set-1 扫描码
    private bool _enableFastProne = true;           // 快速趴下开关
    private bool _enableNukeCombo = true;            // 核弹连杀组合键开关（长按 LB+RB → J，键位修正窗口可勾选）
    // 肩键/扳机交换预设（LT↔LB、RT↔RB）：同时修改物理来源绑定，输出不再二次交换。
    private bool _swapShoulderTriggers = false;
    private bool _lastAimOut;     // 上次 L2（瞄准）输出状态（边沿日志用）
    private bool _lastShootOut;   // 上次 R2（射击）输出状态（边沿日志用）
    private bool _lastTacticalOut; // 上次 L1（战术）输出状态（边沿日志用）
    private bool _lastLethalOut;   // 上次 R1（杀伤）输出状态（边沿日志用）
    private string _lastAimSrc = "—";       // 上次触发 L2 输出的物理键（OFF 日志沿用）
    private string _lastShootSrc = "—";     // 上次触发 R2 输出的物理键（OFF 日志沿用）
    private string _lastTacticalSrc = "—";  // 上次触发 L1 输出的物理键（OFF 日志沿用）
    private string _lastLethalSrc = "—";    // 上次触发 R1 输出的物理键（OFF 日志沿用）
    // 快速交互（同快速趴下规格）：长按触发键注入 F（游戏交互键），期间暂停触发键原生转发
    private bool _enableFastInteract = false;       // 快速交互开关
    private int _interactKey = 0x0100;              // 快速交互触发键（默认 LB，键位修正可录制）
    private bool _interactWasDown;                  // 触发键边沿检测
    private int _interactDownTick;                  // 触发键按下时刻
    private bool _interactHeld;                     // 当前是否已注入 F 按住（期间暂停触发键原生转发）
    // 左摇杆奔跑（键鼠模式）：轻推左摇杆注入 W 长按（走路），跨越奔跑阈值按一下 H 切换静步/奔跑
    private bool _enableRun = true;                 // 左摇杆奔跑开关（按下 L3 注入 H）
    private bool _autoRun = true;                   // 自动奔跑：推满/回落边沿自动注入 H（轻推走路/重推奔跑）
    private bool _runGameDefaultRun = true;         // 游戏默认开启奔跑模式（H 初始状态：true=已在奔跑，false=走路）
    private bool _runReturnWalk = true;             // 回中自动切回走路：true=回落边沿按 H 切走路；false=保持奔跑（L3 手动切走路）
    private int _runThreshold = 90;                 // 自动奔跑触发阈值百分比（50~100，默认 90）
    private bool _l3Prev = false;                   // 上次 L3（左摇杆按下）状态，边沿触发一次 H 切换静步/奔跑
    private int _runZone = 1;                       // 自动奔跑状态机：0=走路区，1=奔跑区（初始按"游戏默认开启奔跑"=1；带滞环防阈值抖动）
    private bool _runCalibrated = true;             // 首次推杆校准：游戏默认奔跑开时，首次进入移动区按一次 H 切到走路（否则前推"轻推走路"永远失效）

    // ---- 按键映射区（手柄键 → 键盘任意键）：绑定键按下注入键盘键、原生输出暂停（动作跟着键位走） ----
    private readonly List<PadKeyBind> _padKeyMap = new();   // 映射列表（含 Home/Logo 与 LT/RT）
    // Successfully delivered keyboard states are owned by GameKeyboardRouter.
    // 实验：R3 组合注入 V+B（一个手柄键 → 两个键盘键，V/B 功能不冲突）
    private bool _expRsCombo;                        // 实验开关（默认关）
    private bool _rsComboDown;                       // V+B 当前按住状态
    // 游戏事件：切换武器射击模式（键盘 B 键）——单键长按或双键组合超阈值触发一次 B
    private bool _fireModeEnabled;                   // 开关（默认关）
    private int _fireModeHoldMs = 250;               // 长按判定阈值（独立可调）
    private bool _fireModeUseCombo = true;           // true=双键组合触发；false=单键长按触发
    private int _fireModeKey = 0x0010;               // 单键触发键（默认 Option，不占动作/奔跑/近战键位）
    private int _fireModeCombo;                      // 物理来源掩码，包含 LT/RT
    private bool _fireModeWasDown, _fireModeSent;    // 边沿检测 + 已触发标记（触发后屏蔽触发键原生转发）
    private int _fireModeDownTick;                   // 触发键按下时刻
    private long _tapHDownTick;                      // H 按下时刻（0=未按下；按下后保持 80ms 再抬起，确保游戏采样到"一次点击"）
    private bool _rumbleOn = true;                  // 震动转发开关（游戏震动 → 实体手柄）
    private int _rumbleStrength = 100;              // 震动强度 0~100%
    private bool _rumbleByShot = true;              // 枪声触发震动（采集系统音频检测枪声爆音）
    private int _rumbleMinLevel = 75;                // 枪声触发的力度下限 50~100%（开枪即有该力度起步）
    private bool _rumbleLowBoost = true;             // 低频增强（重型枪/爆炸/部分消音枪更易触发）
    private float _shotSens = 3f;                   // 枪声检测灵敏度（能量超过基线×倍数视为枪声；1.5~8，越小越灵敏）
    private readonly ShotRumbleListener _shotRumble = new(); // 音频枪声监听器
    // 免费版灵敏度档位：20 档（1~20），腰射 40%~100% 非线性（√ 分布，低档区分度大、高档细腻）；
    // 开镜 70%~100% 非线性 × ADS 倍率。免费版曲线锁定为出厂调教基准 × 档位系数（整体等比缩放极值），
    // 且强制第 10 档（索引 9）；高级版可选 20 档并在档位基础上横纵微调（fine tune）；
    private int _freeSensLevel = 19;                // 当前档位（免费版加载后强制中档=第 10 档，索引 9）
    private float _adsMultiplier = 0.9f;            // ADS（开镜）灵敏度倍率 0.50~1.50，默认 0.90（正作惯例）
    private readonly List<Button> _sensBtns = new(); // 主页 10 个胶囊档位按钮（三档统一显示）
    private Button btnFineTune = null!;            // 主页灵敏度区"微调手感"入口（高级/Pro 显示）
    private Button btnTest = null!;                // 左下角"按键测试"入口（全版本，实时读取手柄原始输入）
    private Button btnTestLog = null!;             // 左下角"测试记录"入口（全版本，自动记录测试日志）
    private Button btnDiagReport = null!;          // 左下角"诊断报告"入口（全版本，一键复制定位问题）
    private Button btnSop = null!;                 // 左下角"转向排查"入口：按扳机带转向的 5 步排查方案（全版本）
    private Button btnDiag = null!;                // 状态卡右上角"一键诊断"入口（全版本）
    private bool _firstRun = true;                 // 首次启动 → 弹出使用引导
    private Button btnReset = null!;               // 左下角"恢复默认"入口（全版本，一键回到出厂设置，保留版本档位）
    private Button btnExport = null!;              // "导出配置"：手感一键分享（全版本）
    private Button btnImport = null!;              // "导入配置"：读取他人手感文件（全版本）
    private Button btnLang = null!;                // 右上角语言切换（中文/English，三版本都有）
    private Label _lblSensLock = null!;            // 主页灵敏度区：档位说明 / 免费版锁定提示
    private Label _lblSummary = null!;       // 当前配置总结行（模式/封装/灵敏度/快速趴下，无容器）
    private Label lblProBadge = null!;       // 右上角 PRO 已激活徽标
    private Button btnOpenFeel = null!;      // 总览卡"打开手感调校"
    private PictureBox picCat = null!;       // 右下角猫咪插画（品牌主视觉）
    private PictureBox picLogo = null!;            // 顶部品牌小 logo
    private Label _lblTitle = null!;               // 标题
    private Label _lblSub = null!;                 // 副标题
    private GlassPanel _sensCard = null!;         // 灵敏度卡（毛玻璃，主界面恒隐藏，仅设置窗口内可见）
    private Label _lblTip = null!;                 // 底部使用顺序提示
    private Label _lblCredit = null!;              // 底部品牌 credit（作者 · B站）
    private OutlineLabel _lblKeyInfo = null!;      // 关键设置信息行（ADS / 输出频率 / 震动 / 移动方案 / 驱动清理）
    private GlassPanel _changelogPanel = null!;    // 本版更新信息卡（核心更新，主页可见）
    private Label _lblChangelogTitle = null!;      // 更新卡标题（本版更新 vX.X）
    private OutlineLabel _lblChangelog = null!;    // 更新卡内容（两行核心更新）
    // 切出光标后延迟把光标移开：PostMessage 投递的 U 键由游戏异步处理，
    // 切出的光标会出现在屏幕中间；等游戏处理完（约 400ms）再移角落，避免被覆盖。
    private readonly System.Windows.Forms.Timer _cursorMoveTimer;

    // ---- 底层状态 ----
    // ViGEm 虚拟手柄客户端。目标机器未装 ViGEmBus 驱动时，new ViGEmClient() 会抛
    // VigemBusNotFoundException —— 若放在字段初始化器里会直接崩溃（用户双击无反应）。
    // 因此在构造函数内 try-catch 安全创建，缺失时降级为"可诊断"状态并引导装驱动。
    private ViGEmClient? _vigem;
    private bool _vigemBusMissing;
    private string _vigemError = "";
    private IDualShock4Controller? _ds4;
    private bool _running;
    private int _sourceSlot = -1;
    private readonly KeyMap _map = new();
    private ushort _comboMask = 0x00C0; // 切换光标组合键（默认 L3+R3，可在键位修正中自定义）
    private int _cursorSwitchMode;      // 切光标触发方式：0=组合键（L3+R3） 1=单键（Xbox=Home / DS=触摸板）
    private bool _outputPaused = true;
    private long _lastSonyFrameTick; // Sony 最后收到输入帧的时间（连续无数据才暂停，防瞬时抖动误判断开）
    private long _startedTick;   // 本次转换启动时刻（诊断报告运行时长用）
    private bool _noPadWarned;
    private readonly System.Windows.Forms.Timer _timer;

    // ---- 手感/震动/驱动生命周期开关（设置持久化）----
    private bool _nudgeCursor = true;              // 推左摇杆>90% 光标移左上角（重生避让）
    private bool _rumbleFocusOnly = true;          // 枪声震动仅游戏前台触发（后台不检测）
    private int _gameProcId;                       // "识别当前游戏"锁定的前台进程 PID（0=未锁定）

    // ---- 高频输出注入（v2.2.18）：独立后台线程按用户设定频率读手柄→写虚拟 DS4；UI 线程只做显示刷新 ----
    private int _outputHz = 1000;                  // 输出刷新率（60~1000，默认 1000；1000=1ms 注入）
    private Thread? _loopThread;
    private volatile bool _loopStop;
    private volatile bool _uiStatusDirty, _uiRawDirty, _uiHidTipDirty;
    private string _pendingStatus = "";
    private Color _pendingStatusColor = Color.Gray;
    private string _pendingRaw1 = "", _pendingRaw2 = "";
    private string _pendingHidTip = "";
    private Color _pendingHidTipColor = Color.Gray;

    // ---- Gyro 体感（实验性，默认全关；仅 Sony 六轴手柄有陀螺仪）----
    private bool _gyroEnabled = false;             // Gyro 总开关
    private float _gyroSens = 1f;                  // 灵敏度倍率 0.2~3.0
    private int _gyroDeadzone = 600;               // 角速度死区（raw）
    private float _gyroSmooth = 0.3f;              // 一阶低通系数 0~0.8
    private bool _gyroInvertX;                     // 水平翻转
    private bool _gyroInvertY;                     // 垂直翻转
    private float _gyroSx, _gyroSy;                // 平滑滤波状态（右摇杆叠加量）

    // ---- 实验：左摇杆移动替代（WASD + 外圈 Shift / 模拟触屏；默认全关）----
    private bool _expWasdMove;
    private int _expWasdThreshold = 85;
    private bool _expTouchMove;
    private int _expTouchRadius = 120;
    private int _expTouchSens = 100;
    private byte _wasdMask;                        // 当前按下的 WASD 位掩码（W=1 A=2 S=4 D=8）
    private bool _wasdShiftDown;                   // 当前 Shift 按住状态
    private float _touchCX = 0.22f, _touchCY = 0.72f; // 触屏中心（前台窗口坐标比例，左下虚拟摇杆区）

    // 扳机诊断日志：游戏里按扳机时自动记录手柄原始值（用于排查扳机→转向问题）
    private readonly string _triggerLogPath = Path.Combine(Path.GetTempPath(), "CODMProbeTrigger.log");
    private DateTime _lastTriggerLog = DateTime.MinValue;

    public MainForm()
    {
        _gameKeys=CreateGameKeyRouter();
        _wasdShiftHold=CreateWasdShiftHold();
        // ViGEm Bus 驱动缺失会抛 VigemBusNotFoundException：安全创建，不崩溃（见 TryInitVigem）。
        if (!UiPreview.Enabled) TryInitVigem();

        // 布局按窗口实际 DPI 等比缩放（多显示器/虚拟屏下字体与控件同步放大，永不错位）。
        Dpi.Scale = Math.Max(1f, DeviceDpi / 96f);
        Text = Lang.T("CODM 手柄转换", "CODM Controller Bridge");
        // 窗口尺寸同样随 DPI 等比放大（否则控件放大了、窗口不放大，高 DPI 下内容溢出/遮挡）
        ClientSize = new Size(Math.Min(Dpi.Px(1120),Screen.FromControl(this).WorkingArea.Width-Dpi.Px(40)), Math.Min(Dpi.Px(800),Screen.FromControl(this).WorkingArea.Height-Dpi.Px(80)));
        MinimumSize = new Size(Dpi.Px(900), Dpi.Px(640));
        MaximumSize = Size.Empty; // 锁定窗口：不可拖拽缩放（行距已按 1.5 倍整体放大，文字不重叠）
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        Font = Assets.Ui(10F);
        Assets.Skin(this);
        // 主界面射击游戏主题背景图：Stretch 满幅铺满窗口，任何窗口尺寸/比例都无留白
        BackgroundImage = Assets.Background();
        BackgroundImageLayout = ImageLayout.Stretch;

        BuildLayout();
        WireEvents();
        UiPreview.Attach(this);
        _timer = new System.Windows.Forms.Timer { Interval = 50 };
        _timer.Tick += (_, _) => RefreshUiFromLoop();

        // 切光标后延迟移开：等游戏异步切出光标完成后再把光标移到角落
        _cursorMoveTimer = new System.Windows.Forms.Timer { Interval = 400 };
        _cursorMoveTimer.Tick += (_, _) =>
        {
            _cursorMoveTimer.Stop();
            if (_running && IsGameForeground()) MoveCursorToGameCorner();
        };

        // 枪声检测震动：检测到爆音 → 实体手柄震动脉冲
        _shotRumble.ShotDetected += OnShotDetected;

        // 打开即自动运行；启动前先恢复上次保存的设置（反死区/曲线/灵敏度/键位修正/版本）
        Load += (_, _) =>
        {
            LoadSettings();
            ApplyLang(); // 关键修复：BuildLayout 创建控件时是默认文本，必须按配置语言整体刷新一次，否则开屏英文
            RefreshSensArea();
            RefreshVersionBadge();
            if (!UiPreview.Enabled) System.Threading.Tasks.Task.Run(AutoInstallDrivers); // 后台安装驱动，UI 不卡死
        };
        // 防卡死：AutoInstallDrivers 可能耗时数分钟（驱动安装器 WaitForExit），
        // 绝不能在 UI 线程同步执行（否则窗口显示后假死/卡死）。改后台线程，UI 只负责状态提示。
        // 首次启动（或升级后的新配置）：窗口显示后弹出 3 步使用引导；看完即标记并保存
        Shown += (_, _) =>
        {
            if (_firstRun && !UiPreview.Enabled)
            {
                _firstRun = false;
                var guide = new GuideForm();
                MountStudioForm(guide);
                SaveSettings();
            }
        };
        FormClosing += (_, _) =>
        {
            if (UiPreview.Enabled) return;
            _closingInputSession=true;
            SaveSettings();
            StopConversion();
        };
    }

    // ================= 设置持久化（%AppData%\CODMControllerProbe\settings.json） =================
    // 所有调整实时生效于内存中的 FeelCurve/KeyMap 对象；改动即保存（SaveSettingsNow），
    // 关闭窗口再兜底保存一次 —— 强杀/崩溃也不丢最近的设置；更新版本后设置依然保留。

    private void LoadSettings()
    {lock(_keyboardBindingGate){ResetMappingTransient();LoadSettingsCore();PublishMappingSnapshots();}}
    private void LoadSettingsCore()
    {
        try
        {
            var s = SettingsStore.Load();
            MappingPolicy.Migrate(s);
            _map.Jump = s.MapJump;
            _map.Crouch = s.MapCrouch;
            _map.Reload = s.MapReload;
            _map.Switch = s.MapSwitch;
            _map.Aim = s.MapAim;
            _map.Shoot = s.MapShoot;
            _map.Tactical = s.MapTactical;
            _map.Lethal = s.MapLethal;
            _comboMask = (ushort)(s.ComboMask == 0 ? 0x00C0 : s.ComboMask);
            _cursorSwitchMode = s.CursorSwitchMode is 0 or 1 ? s.CursorSwitchMode : 0;
            _voiceMode = s.VoiceMode is >= VoicePlayer.Off and <= VoicePlayer.Human
                ? s.VoiceMode
                : VoicePlayer.Tts;
            _holdProneDelayMs = s.HoldProneDelayMs is >= 100 and <= 1000 ? s.HoldProneDelayMs : 150;
            _nukeHoldMs = s.NukeHoldMs is >= 200 and <= 1200 ? s.NukeHoldMs : 400;
            _interactHoldMs = s.InteractHoldMs is >= 100 and <= 1000 ? s.InteractHoldMs : 150;
            _enableViewHoldTab = s.EnableViewHoldTab;
            _viewHoldMs = s.ViewHoldMs is >= 100 and <= 1000 ? s.ViewHoldMs : 200;
            _enableFastProne = s.EnableFastProne;
            _enableNukeCombo = s.EnableNukeCombo;
            _swapShoulderTriggers = s.SwapShoulderTriggers;
            _enableFastInteract = s.EnableFastInteract;
            _interactKey = SettingsStore.IsValidBind(s.InteractKey) ? s.InteractKey : 0x4000;
            _map.MeleePing = SettingsStore.IsValidBind(s.MeleeKey) ? s.MeleeKey : 0x0080;
            _enableRun = s.EnableLeftStickRun;
            _autoRun = false;                          // 自动奔跑方案暂时移除：恒关闭（代码保留，恢复时改这里+设置区）
            _runGameDefaultRun = true;
            _runReturnWalk = true;
            _runCalibrated = true;                     // 自动奔跑关闭 → 无需首次校准
            _runThreshold = 90;
            _rumbleOn = s.RumbleOn;
            _rumbleStrength = s.RumbleStrength is >= 0 and <= 100 ? s.RumbleStrength : 100;
            _rumbleByShot = s.RumbleByShot;
            _rumbleMinLevel = s.RumbleMinLevel is >= 50 and <= 100 ? s.RumbleMinLevel : 75;
            _rumbleLowBoost = s.RumbleLowBoost;
            _shotSens = s.ShotSensitivity is >= 1.5f and <= 8f ? s.ShotSensitivity : 3f;
            _nudgeCursor = s.NudgeCursor;
            _rumbleFocusOnly = s.RumbleFocusOnly;
            // ---- Gyro 实验项（默认关；读入后恒生效，不影响其它功能）----
            _gyroEnabled = s.GyroEnabled;
            _gyroSens = s.GyroSens is >= 0.2f and <= 3f ? s.GyroSens : 1f;
            _gyroDeadzone = s.GyroDeadzone is >= 0 and <= 3000 ? s.GyroDeadzone : 600;
            _gyroSmooth = s.GyroSmooth is >= 0f and <= 0.8f ? s.GyroSmooth : 0.3f;
            _gyroInvertX = s.GyroInvertX;
            _gyroInvertY = s.GyroInvertY;
            _gyroSx = _gyroSy = 0f;
            // ---- 实验：左摇杆移动替代（默认关）----
            _expWasdMove = s.ExpWasdMove;
            _expWasdThreshold = s.ExpWasdThreshold is >= 50 and <= 99 ? s.ExpWasdThreshold : 85;
            _expTouchMove = s.ExpTouchMove && !_expWasdMove;
            _expTouchRadius = s.ExpTouchRadius is >= 40 and <= 300 ? s.ExpTouchRadius : 120;
            _expTouchSens = s.ExpTouchSens is >= 50 and <= 150 ? s.ExpTouchSens : 100;
            // ---- 按键映射区 + 实验组合 + 切换武器射击模式 ----
            _padKeyMap.Clear();
            _gameKeys.Release("pad:");
            if (s.PadKeyMap is { Count: > 0 })
                foreach (var raw in s.PadKeyMap)
                {
                    if(raw==null)continue;
                    var resolved = PadKeyBind.Resolve(raw);
                    if (resolved != null) _padKeyMap.Add(resolved);
                }
            PublishKeyboardBindings();
            _expRsCombo = false;
            LoadExperimentalMappings(s);
            _map.NormalizeConflicts();
            _fireModeEnabled = s.FireModeEnabled;
            _fireModeHoldMs = s.FireModeHoldMs is >= 0 and <= 1200 ? s.FireModeHoldMs : 250;
            _fireModeUseCombo = s.FireModeUseCombo;
            _fireModeKey = SettingsStore.IsValidBind(s.FireModeKey) ? s.FireModeKey : 0x0010;
            _fireModeCombo = s.FireModeCombo;
            _fireModeTarget=KeyboardKeys.ByName(s.FireModeKeyboardKey)?.Name??"B";
            ResolveShortcutOwnership();
            _fireModeWasDown = _fireModeSent = false;
            _outputHz = s.OutputHz is >= 60 and <= 1000 ? s.OutputHz : 1000;
            _freeSensLevel = s.FreeSensLevel is >= 0 and <= 19 ? s.FreeSensLevel : 9;
            _adsMultiplier = s.AdsMultiplier is >= 0.5f and <= 1.5f ? s.AdsMultiplier : 0.9f;
            _firstRun = s.FirstRun;
            // 版本档位：0=免费版（全部调节开放） 2=Pro 版（+语音/震动）。
            // 正式版默认免费，凭激活码/作者解锁升 Pro；FreeDemo 恒为免费版。
            // All features are open; legacy tier fields are retained only for configuration compatibility.
            Lang.En = s.Language == 1;
            _fineTuneGains = s.FineTuneGains is { Length: 6 } ? s.FineTuneGains : new[] { 1f, 1f, 1f, 1f, 1f, 1f };
            ApplyVersionedCurves(s); // 按版本档位重建曲线（免费/高级=出厂×档位，Pro=自定义）
            // 会话起点留痕：下次测试日志第一行即可确认运行版本与关键开关，避免"旧进程没退干净/跑错版本"争议。
            TestLog.Add($"会话启动: 版本=v3.4.7 交换肩键扳机={_swapShoulderTriggers} WASD移动={_expWasdMove} 映射区绑定={_padKeyMap.Count} 条");
        }
        catch { /* 设置加载失败使用出厂默认 */ }
    }

    /// <summary>一键恢复出厂设置：重建默认设置并保存（保留当前版本档位；语音库自定义内容不动），再整体重载界面。</summary>
    private void ResetToDefaults()
    {lock(_keyboardBindingGate){ResetMappingTransient();ResetToDefaultsCore();PublishMappingSnapshots();}}
    private void ResetToDefaultsCore()
    {
        var d = AppSettings.CreateDefault();
        d.VersionLevel = _versionLevel;          // 恢复默认不降级：保留当前版本档位
        d.ProUnlocked = _versionLevel >= 2;
        d.Language = Lang.En ? 1 : 0;            // 恢复默认保留语言选择
        if (!UiPreview.Enabled) SettingsStore.Save(d);

        // 内存状态同步重置（与 LoadSettings 一致，保证运行中的转发/界面立刻用默认值）
        _map.Jump = d.MapJump;
        _map.Crouch = d.MapCrouch;
        _map.Reload = d.MapReload;
        _map.Switch = d.MapSwitch;
        _map.Aim = d.MapAim;
        _map.Shoot = d.MapShoot;
        _map.Tactical = d.MapTactical;
        _map.Lethal = d.MapLethal;
        _comboMask = (ushort)(d.ComboMask == 0 ? 0x00C0 : d.ComboMask);
        _cursorSwitchMode = d.CursorSwitchMode is 0 or 1 ? d.CursorSwitchMode : 0;
        _voiceMode = d.VoiceMode;
        _holdProneDelayMs = d.HoldProneDelayMs;
        _nukeHoldMs = d.NukeHoldMs;
        _interactHoldMs = d.InteractHoldMs;
        _enableViewHoldTab = d.EnableViewHoldTab;
        _viewHoldMs = d.ViewHoldMs;
        _enableFastProne = d.EnableFastProne;
        _enableNukeCombo = d.EnableNukeCombo;
        _swapShoulderTriggers = d.SwapShoulderTriggers;
        _enableFastInteract = d.EnableFastInteract;
        _interactKey = d.InteractKey;
        _map.MeleePing = d.MeleeKey;
        _enableRun = d.EnableLeftStickRun;
        ResetExpMoveKeys();
        _expWasdMove = d.ExpWasdMove;
        _expWasdThreshold = d.ExpWasdThreshold;
        _expTouchMove = d.ExpTouchMove && !_expWasdMove;
        _expTouchRadius = d.ExpTouchRadius;
        _expTouchSens = d.ExpTouchSens;
        _autoRun = false;                          // 自动奔跑方案暂时移除：恒关闭
        _runGameDefaultRun = true;
        _runReturnWalk = true;
        _runCalibrated = true;
        _runThreshold = 90;
        _rumbleOn = d.RumbleOn;
        _rumbleStrength = d.RumbleStrength;
        _rumbleByShot = d.RumbleByShot;
        _rumbleMinLevel = d.RumbleMinLevel is >= 50 and <= 100 ? d.RumbleMinLevel : 75;
        _rumbleLowBoost = d.RumbleLowBoost;
        _shotSens = d.ShotSensitivity;
        _nudgeCursor = d.NudgeCursor;
        _rumbleFocusOnly = d.RumbleFocusOnly;
            _shotRumble.Sensitivity = _shotSens;
            _shotRumble.LowBoost = _rumbleLowBoost;
            _shotRumble.Gate = () => !_rumbleFocusOnly || IsGameForeground();
        _freeSensLevel = d.FreeSensLevel;
        _adsMultiplier = d.AdsMultiplier;
        _fineTuneGains = d.FineTuneGains;
        ReplaceKeyboardBindings(Array.Empty<PadKeyBind>());
        _expRsCombo = false;
        _keyboardChords.Clear();_keyboardChordsEnabled=false;_experimentalCursorEnabled=false;PublishMappingSnapshots();
        _fireModeEnabled = false;
        _fireModeHoldMs = 250;
        _fireModeUseCombo = true;
        _fireModeKey = 0x0010;
        _fireModeCombo = 0;
        _fireModeTarget="B";
        _fireModeWasDown = _fireModeSent = false;
        // Legacy tier selection no longer changes feature access.
        ApplyVersionedCurves(d); // 重建 3 套封装 + 激活封装母版 + 按档位派生
        RefreshSensArea();       // 档位胶囊/封装行/总结行/致谢条同步
        RefreshVersionBadge();
        SetStatus("已恢复默认设置", Assets.PrimaryOrange);
    }

    /// <summary>
    /// 按版本档位重建三套曲线：激活封装的母版（总曲线）→ 转发曲线（母版 × 当前档位系数）。
    /// 免费版(0) = 母版 × 中档（第 10 档，腰射 ≈81% / 开镜 ≈84.6%×ADS 倍率，左摇杆不缩放）；
    /// 高级版(1) = 母版 × 用户所选档位，再叠加 fine tune 横纵微调；
    /// Pro(2) = 母版可自由编辑，转发同样按档位派生。
    /// 开镜曲线额外 × ADS 倍率（0.50~1.50，默认 0.90，对应正作 ADS Sensitivity Multiplier）。
    /// 母版加载时统一补足 5 个控制点（含首末），保证手感窗口总有中间点可拖。
    /// </summary>
    private void ApplyVersionedCurves(AppSettings? s = null)
    {
        s ??= SettingsStore.Load();
        AppSettings.EnsurePacks(s);
        _packs = s.Packs;
        _activePackKey = s.ActivePack;
        var pack = _packs.First(p => p.Key == _activePackKey);
        // 两三点旧母版 → 补足 5 点（保持原形状不变），否则手感窗口没有中间点可拖
        pack.Left = AppSettings.CurveData.From(EnsureCurvePoints(pack.Left.ToCurve()));
        pack.Right = AppSettings.CurveData.From(EnsureCurvePoints(pack.Right.ToCurve()));
        pack.RightAds = AppSettings.CurveData.From(EnsureCurvePoints(pack.RightAds.ToCurve()));
        _masterLeft.LoadShapeFrom(pack.Left.ToCurve(), 1f);
        _masterRight.LoadShapeFrom(pack.Right.ToCurve(), 1f);
        _masterRightAds.LoadShapeFrom(pack.RightAds.ToCurve(), 1f);
        DeriveAllFromMaster();
        SaveSettingsNow();
    }

    /// <summary>母版曲线补足到至少 5 个控制点（形状不变），保证画布中间有点可拖。</summary>
    private static FeelCurve EnsureCurvePoints(FeelCurve c)
    {
        c.EnsurePointCount(5);
        return c;
    }

    /// <summary>切换激活封装：装载该封装的母版三根 → 按当前档位派生转发 → 保存。</summary>
    public void SwitchPack(string key)
    {
        var pack = _packs.FirstOrDefault(p => p.Key == key);
        if (pack == null) return;
        _activePackKey = key;
        _masterLeft.LoadShapeFrom(pack.Left.ToCurve(), 1f);
        _masterRight.LoadShapeFrom(pack.Right.ToCurve(), 1f);
        _masterRightAds.LoadShapeFrom(pack.RightAds.ToCurve(), 1f);
        DeriveAllFromMaster();
        SaveSettingsNow();
        RefreshSummary(); // 主页总结行同步
    }

    /// <summary>版本致谢/引导条：免费版引导升级，高级/Pro 版按购买深度致谢。</summary>
    private void RefreshThanks()
    {
        if (_lblThanks == null) return;
        _lblThanks.Text = _versionLevel >= 2
            ? Lang.T("全部功能免费 · 谢谢你的使用、反馈与自愿支持",
                     "All features are free · thank you for using, sharing and supporting this tool")
            : Lang.T("感谢使用免费版 · 全部调校能力已开放 · 如果你觉得好用，支持我可以解锁语音 / 震动",
                     "Thanks for using Free · all tuning unlocked · if you like it, supporting me unlocks voice / rumble");
    }

    /// <summary>主页"当前配置"总结行 + 关键设置信息行 + 手感卡四列（方案/档位/ADS/快速趴下），三版本都有。</summary>
    private void RefreshSummary()
    {
        if (_lblSummary == null) return;
        string mode = _nativeInput ? Lang.T("手柄原生", "Pad native") : Lang.T("键鼠", "K&M");
        string pack = _packs.FirstOrDefault(p => p.Key == _activePackKey)?.Name ?? _activePackKey;
        string sens = Lang.T($"第 {_freeSensLevel + 1} 档 / 20", $"Tier {_freeSensLevel + 1} / 20");
        string prone = _enableFastProne ? Lang.T("开", "On") : Lang.T("关", "Off");
        _lblSummary.Text = Lang.T("当前配置", "Current") + " · " + Lang.T("模式", "Mode") + $"：{mode}"
            + $" · " + Lang.T("封装", "Pack") + $"：{pack} · " + Lang.T("灵敏度", "Sens") + $"：{sens}"
            + $" · " + Lang.T("快速趴下", "Fast prone") + $"：{prone}";

        // 关键设置信息行：ADS 倍率 / 输出频率 / 震动 / 移动方案 / 退出清理驱动（主页可见，一目了然）
        if (_lblKeyInfo != null)
        {
            string vib = _rumbleOn ? Lang.T("开", "On") : Lang.T("关", "Off");
            string move = _expWasdMove ? Lang.T("WASD（推荐）", "WASD (rec.)")
                : (_enableRun ? Lang.T("L3 切换奔跑", "L3 run") : Lang.T("原始", "Native"));
            _lblKeyInfo.Text = Lang.T("关键设置", "Key settings") + $" · ADS {_adsMultiplier:P0} · "
                + Lang.T("输出", "Output") + $" {_outputHz}Hz · " + Lang.T("震动", "Rumble") + $"：{vib} · "
                + Lang.T("移动", "Move") + $"：{move}";
        }
    }

    /// <summary>总曲线工作台改动后，把当前母版三根写回激活封装（Packs 为权威数据）。</summary>
    private void SyncMasterToPack()
    {
        var pack = _packs.FirstOrDefault(p => p.Key == _activePackKey);
        if (pack == null) return;
        pack.Left = AppSettings.CurveData.From(_masterLeft);
        pack.Right = AppSettings.CurveData.From(_masterRight);
        pack.RightAds = AppSettings.CurveData.From(_masterRightAds);
    }

    /// <summary>
    /// 总曲线母版 → 转发曲线：母版 × 当前档位系数（左摇杆不缩放、右腰射 40%~100%、右开镜 70%~100% × ADS 倍率）。
    /// 起点（反死区）锚定 → 所有档位起点一致；形状按各档可用空间等比例压缩 → 单调递增、曲率风格保持。
    /// 高级/Pro 再叠加 fine tune 横纵微调（记忆值，不改母版）。
    /// </summary>
    private void DeriveAllFromMaster()
    {
        int level = _freeSensLevel; // 免费版同样完整使用 20 档（全调节开放）
        _curveLeft.LoadScaledFrom(_masterLeft, 1f);
        _curveRight.LoadScaledFrom(_masterRight, FeelCurve.FreeSensScale(level));
        _curveRightAds.LoadScaledFrom(_masterRightAds, FeelCurve.AdsSensScale(level) * _adsMultiplier);
        if (true) ApplyFineTune(); // 免费版同样应用 fine tune（全调节开放）
    }

    /// <summary>高级版 fine tune：把记忆的横纵微调写到三条转发曲线的 GainX/GainY（[0]=左X [1]=左Y [2]=右腰X [3]=右腰Y [4]=右开X [5]=右开Y）。</summary>
    private void ApplyFineTune()
    {
        var g = _fineTuneGains;
        _curveLeft.GainX = g[0]; _curveLeft.GainY = g[1];
        _curveRight.GainX = g[2]; _curveRight.GainY = g[3];
        _curveRightAds.GainX = g[4]; _curveRightAds.GainY = g[5];
    }

    private void SaveSettings()
    {
        if (UiPreview.Enabled) return;
        try
        {
            SettingsStore.Save(BuildSettings());
        }
        catch { /* 保存失败不影响运行 */ }
    }

    /// <summary>把当前运行状态汇总为一个可持久化的 AppSettings（导出/保存共用同一份权威数据）。</summary>
    private AppSettings BuildSettings()
    {
        return new AppSettings
        {
            // 权威数据：3 套封装 + 激活封装（每套母版由总曲线工作台编辑）
            Packs = _packs,
            ActivePack = _activePackKey,
            // 兼容旧版本 exe：当前激活封装母版写入旧字段（旧版按 CurvesAreMaster 原样读回）
            Left = AppSettings.CurveData.From(_masterLeft),
            Right = AppSettings.CurveData.From(_masterRight),
            RightAds = AppSettings.CurveData.From(_masterRightAds),
            CurvesAreMaster = true,
            ActionMappingVersion = 2,
            EnableKeyboardChords = _keyboardChordsEnabled,
            KeyboardChords = _keyboardChords.Select(c=>c.Normalize()!).ToList(),
            ExperimentalCursorEnabled = _experimentalCursorEnabled,
            ExperimentalCursorKey = _experimentalCursorKey,
            MapJump = _map.Jump,
            MapCrouch = _map.Crouch,
            MapReload = _map.Reload,
            MapSwitch = _map.Switch,
            MapAim = _map.Aim,
            MapShoot = _map.Shoot,
            MapTactical = _map.Tactical,
            MapLethal = _map.Lethal,
            ComboMask = _comboMask,
            CursorSwitchMode = _cursorSwitchMode,
            VoiceMode = _voiceMode,
            ProUnlocked = _versionLevel >= 2,
            VersionLevel = _versionLevel,
            FineTuneGains = _fineTuneGains,
            HoldProneDelayMs = _holdProneDelayMs,
            NukeHoldMs = _nukeHoldMs,
            InteractHoldMs = _interactHoldMs,
            EnableViewHoldTab = _enableViewHoldTab,
            ViewHoldMs = _viewHoldMs,
            EnableFastProne = _enableFastProne,
            EnableNukeCombo = _enableNukeCombo,
            SwapShoulderTriggers = _swapShoulderTriggers,
            EnableFastInteract = _enableFastInteract,
            InteractKey = _interactKey,
            MeleeKey = _map.MeleePing,
            EnableLeftStickRun = _enableRun,
            AutoRun = _autoRun,
            RunGameDefaultRun = _runGameDefaultRun,
            RunReturnWalk = _runReturnWalk,
            RunThreshold = _runThreshold,
            RumbleOn = _rumbleOn,
            RumbleStrength = _rumbleStrength,
            RumbleByShot = _rumbleByShot,
            RumbleMinLevel = _rumbleMinLevel,
            RumbleLowBoost = _rumbleLowBoost,
            ShotSensitivity = _shotSens,
            NudgeCursor = _nudgeCursor,
            RumbleFocusOnly = _rumbleFocusOnly,
            CleanDriversOnExit = false,
            GyroEnabled = _gyroEnabled,
            GyroSens = _gyroSens,
            ExpWasdMove = _expWasdMove,
            ExpWasdThreshold = _expWasdThreshold,
            ExpTouchMove = _expTouchMove,
            ExpTouchRadius = _expTouchRadius,
            ExpTouchSens = _expTouchSens,
            OutputHz = _outputHz,
            GyroDeadzone = _gyroDeadzone,
            GyroSmooth = _gyroSmooth,
            GyroInvertX = _gyroInvertX,
            GyroInvertY = _gyroInvertY,
            FreeSensLevel = _freeSensLevel,
            AdsMultiplier = _adsMultiplier,
            Language = Lang.En ? 1 : 0,
            PadKeyMap = new List<PadKeyBind>(_padKeyMap),
            ExpRsCombo = _expRsCombo,
            FireModeEnabled = _fireModeEnabled,
            FireModeHoldMs = _fireModeHoldMs,
            FireModeUseCombo = _fireModeUseCombo,
            FireModeKey = _fireModeKey,
            FireModeCombo = _fireModeCombo,
            FireModeKeyboardKey = _fireModeTarget,
            FirstRun = false,
        };
    }

    /// <summary>导出当前配置（手感分享 / 备份）：保存为 .json 文件，对方导入后一键获得同一手感。</summary>
    private void ExportConfig()
    {
        using var dlg = new SaveFileDialog
        {
            Title = Lang.T("导出配置", "Export Config"),
            Filter = "CODM 手感配置 (*.codm.json)|*.json|所有文件 (*.*)|*.*",
            FileName = "codm-handfeel.json",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        if (SettingsStore.SaveTo(dlg.FileName, BuildSettings()))
        {
            SetStatus(Lang.T("配置已导出：" + System.IO.Path.GetFileName(dlg.FileName), "Config exported: " + System.IO.Path.GetFileName(dlg.FileName)), Color.FromArgb(5, 150, 105));
        }
        else
        {
            SetStatus(Lang.T("导出失败，请检查保存位置", "Export failed, check the save location"), Color.FromArgb(239, 68, 68));
        }
    }

    /// <summary>导入配置：读取他人导出的手感文件 → 立即应用并保存（当前档位/封装/曲线/键位全部覆盖）。</summary>
    private void ImportConfig()
    {
        using var dlg = new OpenFileDialog
        {
            Title = Lang.T("导入配置", "Import Config"),
            Filter = "CODM 手感配置 (*.codm.json)|*.json|所有文件 (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var s = SettingsStore.LoadFrom(dlg.FileName);
        if (s == null)
        {
            SetStatus(Lang.T("导入失败：文件无效或已损坏", "Import failed: invalid or corrupted file"), Color.FromArgb(239, 68, 68));
            return;
        }
        // 导入覆盖当前设置：写入默认配置路径 → 重走一次加载逻辑（曲线/键位/封装/震动全部应用）
        if (!UiPreview.Enabled) SettingsStore.Save(s);
        LoadSettings();
        ApplyVersionedCurves(s);
        RefreshSensArea();
        RefreshVersionBadge();
        _shotRumble.Sensitivity = _shotSens;
        SetStatus(Lang.T("配置已导入并生效", "Config imported and applied"), Color.FromArgb(5, 150, 105));
        BeginInvoke(()=>ShowStudioSection(_studioSection));
    }

    /// <summary>手感/键位改动后立即保存（FeelForm/RemapForm 的变更回调）。</summary>
    private void SaveSettingsNow() => SaveSettings();

    private void BuildLayout()
    {
        // ================= 顶部品牌行：圆形头像 + 标题 + 副标题 + 版本徽标 =================
        // 按参考图复刻：左侧圆形卡通玩家头像，右侧产品名 + 一句话副标题
        picLogo = new PictureBox
        {
            Image = Assets.CharacterHead(),
            SizeMode = PictureBoxSizeMode.Zoom,
            Location = new Point(Dpi.Px(60), Dpi.Px(12)), Size = new Size(Dpi.Px(54), Dpi.Px(54)),
            BackColor = Color.Transparent,
        };
        ClipCircle(picLogo); // 圆形头像剪裁
        _lblTitle = new Label
        {
            Text = Lang.T("CODM 手柄转换", "CODM Controller Bridge"),
            Location = new Point(Dpi.Px(124), Dpi.Px(14)), Size = new Size(Dpi.Px(360), Dpi.Px(32)),
            Font = Assets.Ui(20F, FontStyle.Bold),
            ForeColor = Color.FromArgb(0xEA, 0xF1, 0xFC),   // 主标题：蓝白（取自深蓝背景的高光层）
            BackColor = Color.Transparent,   // 透明：文字直接叠在背景上，无"框"感
        };
        _lblSub = new OutlineLabel
        {
            Text = Lang.T("把 Xbox 手柄变成游戏认识的 DS4 协议，打开即用", "Turn your Xbox pad into a game-recognized DS4, ready to play"),
            Location = new Point(Dpi.Px(124), Dpi.Px(48)), Size = new Size(Dpi.Px(460), Dpi.Px(20)),
            ForeColor = Color.FromArgb(0xB9, 0xC8, 0xE2), Font = Assets.Ui(9.5F),   // 副标题：蓝灰（背景色系中层）
            BackColor = Color.Transparent,
        };

        btnVersion = new StudioButton
        {
            Text = Lang.T("免费版", "Free"),
            Location = new Point(Dpi.Px(540), Dpi.Px(16)), Size = new Size(Dpi.Px(78), Dpi.Px(30)),
            Font = Assets.Ui(9F, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
        };
        Assets.StyleOutlineDark(btnVersion, 14);

        // ================= 设置（v0.2 极简主界面）：所有可调选项收进"设置"窗口 =================
        btnSettings = new StudioButton
        {
            Text = Lang.T("设置", "Settings"),
            Location = new Point(Dpi.Px(596), Dpi.Px(16)), Size = new Size(Dpi.Px(92), Dpi.Px(30)),
            Font = Assets.Ui(9F, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
        };
        Assets.StyleOutlineDark(btnSettings, 14);
        btnSettings.Click += (_, _) => OpenSettings();

        // ================= 语言切换（右上角版本徽标旁，三版本都有） =================
        btnLang = new StudioButton
        {
            Text = Lang.SwitchLabel,
            Location = new Point(Dpi.Px(626), Dpi.Px(16)), Size = new Size(Dpi.Px(52), Dpi.Px(30)),
            Font = Assets.Ui(9F, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
        };
        Assets.StyleOutlineDark(btnLang, 14);

        // ================= 历史更新日志（右上角，设置旁）：每个版本改了什么 / 为什么 / 怎么思考 =================
        btnChangelog = new StudioButton
        {
            Text = Lang.T("更新日志", "Changelog"),
            Location = new Point(Dpi.Px(556), Dpi.Px(16)), Size = new Size(Dpi.Px(92), Dpi.Px(30)),
            Font = Assets.Ui(9F, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
        };
        Assets.StyleOutlineDark(btnChangelog, 14);
        btnChangelog.Click += (_, _) =>
        {
            var f = new ChangelogForm();
            MountStudioForm(f);
        };

        // ================= 状态区（无容器，文字直接放背景，消除"方框"感）：仅状态 + 实体隐藏两行 =================
        // 实时按键/轴数据不显示在主页（用户反馈：留在「按键测试」窗口里看即可）
        lblStatus = new OutlineLabel
        {
            Text = Lang.T("正在启动……", "Starting…"), TextAlign = ContentAlignment.MiddleLeft,
            Location = new Point(Dpi.Px(40), Dpi.Px(104)), Size = new Size(Dpi.Px(596), Dpi.Px(32)),
            Font = Assets.Ui(16F, FontStyle.Bold),   // 左侧关键状态字号放大
            BackColor = Color.Transparent,
        };

        // "一键诊断"入口（状态卡右上角，全版本）：驱动/隐藏/手柄/音频四个状态灯
        btnDiag = new StudioButton
        {
            Text = Lang.T("诊断", "Check"),
            Location = new Point(Dpi.Px(636), Dpi.Px(12)), Size = new Size(Dpi.Px(76), Dpi.Px(28)),
            Font = Assets.Ui(9F, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
        };
        Assets.StyleOutlineDark(btnDiag, 8);
        btnDiag.Click += (_, _) => OpenDiag();

        // 隐藏的实时数据行（仅保留给代码引用；用户可见的主页不显示）
        lblRaw1 = new Label
        {
            Text = Lang.T("按键：等待手柄……", "Keys: waiting for pad…"),
            Location = new Point(Dpi.Px(40), Dpi.Px(138)), Size = new Size(Dpi.Px(716), Dpi.Px(20)),
            ForeColor = Color.FromArgb(55, 65, 81), Font = new Font("Consolas", 9.5F),
            BackColor = Color.Transparent,
            Visible = false,
        };

        lblRaw2 = new Label
        {
            Text = Lang.T("轴：等待手柄……", "Axes: waiting for pad…"),
            Location = new Point(Dpi.Px(40), Dpi.Px(162)), Size = new Size(Dpi.Px(716), Dpi.Px(20)),
            ForeColor = Color.FromArgb(55, 65, 81), Font = new Font("Consolas", 9.5F),
            BackColor = Color.Transparent,
            Visible = false,
        };

        lblHidHide = new OutlineLabel
        {
            Text = Lang.T("实体手柄隐藏：检测中……", "Physical pad hiding: detecting…"),
            Location = new Point(Dpi.Px(40), Dpi.Px(144)), Size = new Size(Dpi.Px(716), Dpi.Px(24)),
            ForeColor = Color.FromArgb(0xB9, 0xC8, 0xE2), Font = Assets.Ui(12F),   // 蓝灰（背景色系中层）
            BackColor = Color.Transparent,
        };

        // ================= 灵敏度卡（三档统一显示快捷切换；高级/Pro 下方含封装快捷行） =================
        _sensCard = new GlassPanel { Location = new Point(Dpi.Px(24), Dpi.Px(228)), Size = new Size(Dpi.Px(752), Dpi.Px(132)) };

        _lblSensLock = new Label
        {
            Text = "", Location = new Point(Dpi.Px(16), Dpi.Px(10)), Size = new Size(Dpi.Px(500), Dpi.Px(22)),
            ForeColor = Assets.SubText, Font = Assets.Ui(10.5F), // 字号已调大（用户反馈太小）
            BackColor = Color.Transparent,
        };

        // 10 个胶囊档位按钮：1（最低）~ 10（最高）
        string[] names = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "10" };
        for (int i = 0; i < 10; i++)
        {
            int level = i;
            var b = new StudioButton
            {
                Text = names[i],
                Location = new Point(Dpi.Px(16 + i * 52), Dpi.Px(8)), Size = new Size(Dpi.Px(48), Dpi.Px(30)),
                Font = Assets.Ui(9F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
            };
            b.Click += (_, _) =>
            {
                _freeSensLevel = level;                 // 免费版也可自由切换 20 档（全调节开放）
                ApplySensLevel(); // 出厂×档位 + fine tune（Pro 同样以档位为起点，可继续微调）
                RefreshSensArea();
            };
            _sensBtns.Add(b);
        }

        // ================= 当前配置总结行（无容器，三版本都有）：一眼看清 模式/封装/灵敏度/快速趴下 =================
        _lblSummary = new OutlineLabel
        {
            Text = "", TextAlign = ContentAlignment.MiddleLeft,
            Location = new Point(Dpi.Px(36), Dpi.Px(225)), Size = new Size(Dpi.Px(726), Dpi.Px(24)),
            ForeColor = Color.FromArgb(0xF4, 0xF8, 0xFF), Font = Assets.Ui(12F, FontStyle.Bold),   // 配置总结：近白带蓝（最亮信息层）
            BackColor = Color.Transparent,
        };

        // 版本致谢/引导行（无容器，三版本都有）：免费=引导升级，高级/Pro=感谢购买（橙调文字）
        _lblThanks = new OutlineLabel
        {
            Text = "", TextAlign = ContentAlignment.MiddleLeft,
            Location = new Point(Dpi.Px(36), Dpi.Px(197)), Size = new Size(Dpi.Px(726), Dpi.Px(24)),
            ForeColor = Assets.PrimaryOrange, Font = Assets.Ui(11.5F),   // 致谢行字号放大
            BackColor = Color.Transparent,
        };

        // ================= 关键设置信息行（主页可见）：ADS / 输出频率 / 震动 / 移动方案 / 退出清理 =================
        _lblKeyInfo = new OutlineLabel
        {
            Text = "", TextAlign = ContentAlignment.MiddleLeft,
            Location = new Point(Dpi.Px(36), Dpi.Px(262)), Size = new Size(Dpi.Px(726), Dpi.Px(24)),
            ForeColor = Color.FromArgb(0xCF, 0xDD, 0xF0), Font = Assets.Ui(9.5F),   // 亮蓝灰：关键设置第二信息层
            BackColor = Color.Transparent,
        };

        // ================= 本版更新信息卡（主页可见，GlassPanel 毛玻璃）：核心更新一看即知 =================
        _changelogPanel = new GlassPanel
        {
            Location = new Point(Dpi.Px(24), Dpi.Px(296)), Size = new Size(Dpi.Px(752), Dpi.Px(96)),
        };
        _lblChangelogTitle = new Label
        {
            Text = Lang.T("v3.4.2 · 本版更新", "v3.4.2 · What's new"),
            Location = new Point(Dpi.Px(16), Dpi.Px(6)), Size = new Size(Dpi.Px(720), Dpi.Px(22)),
            Font = Assets.Ui(10F, FontStyle.Bold), ForeColor = Assets.PrimaryOrange,
            BackColor = Color.Transparent,
        };
        _lblChangelog = new OutlineLabel
        {
            Text = Lang.T("DS4 / DS5 / DSE：设备识别 · 映射替换 · 连续输出",
                          "DS4 / DS5 / DSE: shoulder/trigger swap · USB priority · direct-drive output")
                + "\n" + Lang.T("先启动转换再打开游戏；游戏只接收转换结果，避免原动作重复",
                          "Direct-drive output, no isolation wait; keep in-game controller settings default"),
            TextAlign = ContentAlignment.TopLeft,
            Location = new Point(Dpi.Px(16), Dpi.Px(32)), Size = new Size(Dpi.Px(720), Dpi.Px(56)),
            Font = Assets.Ui(9F), ForeColor = Color.FromArgb(0xB9, 0xC8, 0xE2),
            BackColor = Color.Transparent,
        };
        _changelogPanel.Controls.Add(_lblChangelogTitle);
        _changelogPanel.Controls.Add(_lblChangelog);

        // ================= "微调手感"入口：高级版=横纵微调；Pro=完整曲线编辑；免费版隐藏 =================
        btnFineTune = new StudioButton
        {
            Text = Lang.T("微调手感 →", "Fine-tune →"),
            Location = new Point(Dpi.Px(634), Dpi.Px(8)), Size = new Size(Dpi.Px(104), Dpi.Px(30)),
            Font = Assets.Ui(9.5F, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
        };
        Assets.StyleCapsule(btnFineTune, false, false);
        btnFineTune.Visible = false;

        // ================= 主按钮：启动/停止（全宽大橙钮） =================
        btnToggle = new StudioButton
        {
            Text = Lang.T("启动转换", "Start"),
            Location = new Point(Dpi.Px(24), Dpi.Px(322)), Size = new Size(Dpi.Px(752), Dpi.Px(56)),
            Font = Assets.Ui(13F, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
        };
        Assets.StylePrimary(btnToggle, 14);

        // ================= "支持一下 · 解锁语音+震动"入口（主页显眼位，免费版可见）：怕用户找不到 → 主界面直接给入口 =================
        btnUpgrade = new StudioButton
        {
            Text = Lang.T("支持一下 · 解锁语音+震动 →", "Support me · unlock voice + rumble →"),
            Location = new Point(Dpi.Px(24), Dpi.Px(322)), Size = new Size(Dpi.Px(200), Dpi.Px(30)),
            Font = Assets.Ui(10F, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
        };
        Assets.StylePrimary(btnUpgrade, 12);
        btnUpgrade.Click += (_, _) => OpenUnlock();

        // ================= 功能行：键位修正 / 总曲线 / 语音设置（输入模式为只读显示，靠 L3+R3 组合键切换） =================
        btnRemap = new StudioButton
        {
            Text = Lang.T("键位修正", "Key Remap"),
            Location = new Point(Dpi.Px(24), Dpi.Px(330)), Size = new Size(Dpi.Px(190), Dpi.Px(36)),
            Font = Assets.Ui(10F),
            FlatStyle = FlatStyle.Flat,
        };
        Assets.StyleOutline(btnRemap, 10);

        lblMode = new Label
        {
            Text = Lang.T("输入模式：手柄原生（辅助瞄准大）", "Input: Pad native (strong aim assist)"),
            Location = new Point(Dpi.Px(586), Dpi.Px(330)), Size = new Size(Dpi.Px(190), Dpi.Px(36)),
            Font = Assets.Ui(10F, FontStyle.Bold),
            ForeColor = Assets.PrimaryOrange,
            TextAlign = ContentAlignment.MiddleRight,
            BackColor = Color.Transparent,
        };

        // ================= 震动设置入口（右侧列，三版本可见）：进震动设置窗口（开关/枪声触发/强度/灵敏度） =================
        btnRumble = new StudioButton
        {
            Text = Lang.T("震动", "Rumble"),
            Location = new Point(Dpi.Px(428), Dpi.Px(330)), Size = new Size(Dpi.Px(188), Dpi.Px(36)),
            Font = Assets.Ui(10F),
            FlatStyle = FlatStyle.Flat,
        };
        Assets.StyleOutline(btnRumble, 10);
        btnRumble.Click += (_, _) => OpenRumbleSettings();

        btnVoice = new StudioButton
        {
            Text = Lang.T("语音设置", "Voice"),
            Location = new Point(Dpi.Px(428), Dpi.Px(330)), Size = new Size(Dpi.Px(188), Dpi.Px(36)),
            Font = Assets.Ui(10F),
            FlatStyle = FlatStyle.Flat,
        };
        Assets.StyleOutline(btnVoice, 10);

        // ================= "按键测试"入口（左下角，全版本）：实时读取实体手柄原始输入 =================
        btnTest = new StudioButton
        {
            Text = Lang.T("按键测试", "Test Keys"),
            Location = new Point(Dpi.Px(24), Dpi.Px(484)), Size = new Size(Dpi.Px(92), Dpi.Px(26)),
            Font = Assets.Ui(9F),
            FlatStyle = FlatStyle.Flat,
        };
        Assets.StyleOutline(btnTest, 8);

        // ================= "恢复默认"入口（左下角，全版本）：一键回到出厂设置（保留版本档位） =================
        btnReset = new StudioButton
        {
            Text = Lang.T("恢复默认", "Reset"),
            Location = new Point(Dpi.Px(124), Dpi.Px(484)), Size = new Size(Dpi.Px(92), Dpi.Px(26)),
            Font = Assets.Ui(9F),
            FlatStyle = FlatStyle.Flat,
        };
        Assets.StyleOutline(btnReset, 8);

        // ================= "导出配置 / 导入配置"（左下角，全版本）：手感一键分享（.json） =================
        btnExport = new StudioButton
        {
            Text = Lang.T("导出配置", "Export"),
            Location = new Point(Dpi.Px(224), Dpi.Px(484)), Size = new Size(Dpi.Px(92), Dpi.Px(26)),
            Font = Assets.Ui(9F),
            FlatStyle = FlatStyle.Flat,
        };
        Assets.StyleOutline(btnExport, 8);
        btnExport.Click += (_, _) => ExportConfig();

        btnImport = new StudioButton
        {
            Text = Lang.T("导入配置", "Import"),
            Location = new Point(Dpi.Px(324), Dpi.Px(484)), Size = new Size(Dpi.Px(92), Dpi.Px(26)),
            Font = Assets.Ui(9F),
            FlatStyle = FlatStyle.Flat,
        };
        Assets.StyleOutline(btnImport, 8);
        btnImport.Click += (_, _) => ImportConfig();

        // ================= "测试记录"入口（按键测试正下方，全版本）：自动记录手柄测试日志，一键复制给开发者 =================
        btnTestLog = new StudioButton
        {
            Text = Lang.T("测试记录", "Test Log"),
            Location = new Point(Dpi.Px(24), Dpi.Px(516)), Size = new Size(Dpi.Px(92), Dpi.Px(26)),
            Font = Assets.Ui(9F),
            FlatStyle = FlatStyle.Flat,
        };
        Assets.StyleOutline(btnTestLog, 8);
        btnTestLog.Click += (_, _) => { var f = new TestLogForm(); MountStudioForm(f); };

        // ================= "诊断报告"入口（左下角测试记录右侧，全版本）：一键汇总运行状态/隔离/日志，复制给开发者 =================
        btnDiagReport = new StudioButton
        {
            Text = Lang.T("诊断报告", "Diagnose"),
            Location = new Point(Dpi.Px(124), Dpi.Px(516)), Size = new Size(Dpi.Px(92), Dpi.Px(26)),
            Font = Assets.Ui(9F),
            FlatStyle = FlatStyle.Flat,
        };
        Assets.StyleOutline(btnDiagReport, 8);
        btnDiagReport.Click += (_, _) => { var f = new DiagnosticForm(BuildDiagnosticReport); MountStudioForm(f); };

        // ================= "LT/RT 带转向"排查入口（左下角，全版本）：按扳机带转向（键位互串）的 5 步排查 SOP =================
        btnSop = new StudioButton
        {
            Text = Lang.T("LT/RT 带转向", "LT/RT steer?"),
            Location = new Point(Dpi.Px(524), Dpi.Px(484)), Size = new Size(Dpi.Px(132), Dpi.Px(26)),
            Font = Assets.Ui(9F),
            FlatStyle = FlatStyle.Flat,
            TextAlign = ContentAlignment.MiddleCenter,   // 文字水平垂直居中，不留偏置
        };
        Assets.StyleOutlineDark(btnSop, 10);             // 深色实底 + 亮青蓝边框：与主界面其余按钮风格统一
        btnSop.Click += (_, _) =>
        {
            string msg = Lang.En
                ? "Trigger (LT/RT) causing unwanted turning? Try these 5 steps:\n\n"
                  + "1. Stop conversion and quit the game\n"
                  + "2. Unplug the pad, wait 3s, plug it back in\n"
                  + "3. Close the app and reopen it (drivers reinstall silently)\n"
                  + "4. Confirm \"Pad connected\" on the main screen\n"
                  + "5. Relaunch the game, then start conversion\n\n"
                  + "Still acting up? Try another USB port / cable, make sure only ONE pad is plugged in, and re-record LT/RT in Key Remap."
                : "按扳机（LT/RT）带转向（键位互串）？按下面 5 步排查：\n\n"
                  + "1. 关闭转换，退出游戏\n"
                  + "2. 拔下实体手柄，等 3 秒重新插上\n"
                  + "3. 关闭软件并重新打开（驱动自动静默重装）\n"
                  + "4. 主界面确认「手柄已连接」\n"
                  + "5. 重新进入游戏，再开启转换\n\n"
                  + "仍异常？换 USB 口/线；确保只接入一个手柄；在键位修正里重新录制 LT/RT。";
            MessageBox.Show(this, msg,
                Lang.T("LT/RT 带转向？", "LT/RT causing turning?"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        };

        _lblTip = new OutlineLabel
        {
            Text = Lang.T("先插手柄 → 打开工具 → 再启动游戏 · 关闭窗口即停止",
                          "Plug pad → open app → launch game · Close window to stop"),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(Dpi.Px(24), Dpi.Px(376)), Size = new Size(Dpi.Px(592), Dpi.Px(24)),
            ForeColor = Color.FromArgb(0x9D, 0xB0, 0xCC), Font = Assets.Ui(8.5F),   // 使用提示：暗蓝灰（层级最低）
            BackColor = Color.Transparent,
        };

        // 底部品牌 credit：作者署名（克制小字，层次低于使用提示）
        _lblCredit = new OutlineLabel
        {
            Text = "Made with ♥ by Burrito 什么饼（B站）",
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(Dpi.Px(24), Dpi.Px(478)), Size = new Size(Dpi.Px(592), Dpi.Px(22)),
            ForeColor = Color.FromArgb(0x94, 0xA6, 0xC2), Font = Assets.Ui(8F),   // credit：暗蓝灰（层级最低）
            BackColor = Color.Transparent,
        };


        // ================= UI v2：右下猫咪插画（品牌主视觉，深色底上唯一装饰图） =================
        picCat = new PictureBox
        {
            Image = Assets.CharacterCorner(), SizeMode = PictureBoxSizeMode.Zoom,
            Location = new Point(Dpi.Px(880), Dpi.Px(390)), Size = new Size(Dpi.Px(150), Dpi.Px(150)),
            BackColor = Color.Transparent,
        };

        // ================= Buy me Coffee 赞赏卡（右下角，免费版可见）：扫码赞赏任意金额即解锁 Pro =================
        _tipCard = new GlassPanel
        {
            Location = new Point(Dpi.Px(548), Dpi.Px(456)), Size = new Size(Dpi.Px(276), Dpi.Px(132)),
        };
        _lblTipCardTitle = new Label
        {
            Text = Lang.T("Buy me Coffee ☕", "Buy me Coffee ☕"),
            Font = Assets.Ui(10F, FontStyle.Bold), ForeColor = Assets.PrimaryOrange,
            Location = new Point(Dpi.Px(16), Dpi.Px(10)), Size = new Size(Dpi.Px(244), Dpi.Px(22)),
            BackColor = Color.Transparent,
        };
        _lblTipCardSub = new Label
        {
            Text = Lang.T("全部功能免费\n喜欢这个工具？请我吃点东西",
                          "All features are free\nEnjoy it? Buy me a meal"),
            Font = Assets.Ui(8.5F), ForeColor = Color.FromArgb(0x9D, 0xB0, 0xCC),
            Location = new Point(Dpi.Px(16), Dpi.Px(38)), Size = new Size(Dpi.Px(130), Dpi.Px(46)),
            BackColor = Color.Transparent,
        };
        _picTipQr = new PictureBox
        {
            SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White,
            Location = new Point(Dpi.Px(152), Dpi.Px(26)), Size = new Size(Dpi.Px(100), Dpi.Px(100)),
        };
        try { _picTipQr.Image = Assets.RewardQr(); } catch { /* 资源缺失时保留空白底 */ }
        _tipCard.Controls.Add(_lblTipCardTitle);
        _tipCard.Controls.Add(_lblTipCardSub);
        _tipCard.Controls.Add(_picTipQr);
        // 点击卡片任意位置 → 打开解锁窗口（扫码 → 赞赏 → 解锁）
        void TipClick(object? s, EventArgs e) => OpenUnlock();
        _tipCard.Click += TipClick;
        _lblTipCardTitle.Click += TipClick;
        _lblTipCardSub.Click += TipClick;
        _picTipQr.Click += TipClick;

        Controls.Add(picLogo);
        Controls.Add(_lblTitle);
        Controls.Add(_lblSub);
        Controls.Add(btnVersion);
        Controls.Add(btnSettings);
        Controls.Add(btnChangelog);
        Controls.Add(_tipCard);
        Controls.Add(lblStatus);
        Controls.Add(lblRaw1);
        Controls.Add(lblRaw2);
        Controls.Add(lblHidHide);
        Controls.Add(_lblThanks);
        Controls.Add(_lblSummary);
        Controls.Add(_lblKeyInfo);
        Controls.Add(_changelogPanel);
        Controls.Add(_sensCard);
        _sensCard.Controls.Add(_lblSensLock);
        _sensCard.Controls.Add(btnFineTune);
        foreach (var b in _sensBtns) _sensCard.Controls.Add(b);
        Controls.Add(btnToggle);
        Controls.Add(btnUpgrade);
        Controls.Add(btnRemap);
        Controls.Add(lblMode);
        Controls.Add(btnRumble);
        Controls.Add(_lblTip);
        Controls.Add(_lblCredit);
        Controls.Add(btnVoice);
        Controls.Add(btnTest);
        Controls.Add(btnSop);
        Controls.Add(btnReset);
        Controls.Add(btnExport);
        Controls.Add(btnImport);
        Controls.Add(btnTestLog);
        Controls.Add(btnDiagReport);

        BuildHomeShell();
        Reflow(); // 按当前窗口尺寸排布（窗口可缩放，拖动时由 OnResize 重排）
    }

    /// <summary>
    /// 语言切换后全界面文本重填：标题/副标题/版本徽标/语言钮/功能按钮/封装行/总结/致谢/灵敏度说明。
    /// 状态与实时数据（lblStatus/lblRaw/lblHidHide）由下次轮询或状态更新自动按新语言重写。
    /// </summary>
    private void ApplyLang()
    {
        if (_lblTitle == null) return; // 布局尚未构建
        Text = Lang.T("CODM 手柄转换", "CODM Controller Bridge");
        _lblTitle.Text = Lang.T("CODM 手柄转换", "CODM Controller Bridge");
        _lblSub.Text = "PAD/BRIDGE · GAMEPAD TOOL";   // 品牌副标固定不翻译
        btnLang.Text = Lang.SwitchLabel;
        btnVersion.Text = VersionName(_versionLevel);
        btnUpgrade.Text = Lang.T("支持一下 · 解锁语音+震动 →", "Support me · unlock voice + rumble →");
        btnFineTune.Text = Lang.T("完整调校", "Full Tuning");
        btnToggle.Text = _running ? Lang.T("停止转换", "Stop") : Lang.T("启动转换", "Start");
        btnRemap.Text = Lang.T("键位修正", "Key Remap");
        btnVoice.Text = Lang.T("语音设置", "Voice");
        btnTest.Text = Lang.T("按键测试", "Test Keys");
        btnSop.Text = Lang.T("LT/RT 带转向", "LT/RT steer?");
        btnReset.Text = Lang.T("恢复默认", "Reset");
        btnExport.Text = Lang.T("导出配置", "Export");
        btnImport.Text = Lang.T("导入配置", "Import");
        btnDiag.Text = Lang.T("诊断", "Check");
        btnChangelog.Text = Lang.T("更新日志", "Changelog");
        if (_lblTipCardTitle != null)
        {
            _lblTipCardTitle.Text = Lang.T("Buy me Coffee ☕", "Buy me Coffee ☕");
            _lblTipCardSub.Text = Lang.T("全部功能免费\n喜欢这个工具？请我吃点东西",
                                         "All features are free\nEnjoy it? Buy me a meal");
        }
        lblMode.Text = _nativeInput
            ? Lang.T("输入模式：手柄原生（辅助瞄准大）", "Input: Pad native (strong aim assist)")
            : Lang.T("输入模式：键鼠（辅助瞄准小）", "Input: K&M (light aim assist)");
        btnRumble.Text = Lang.T("震动反馈", "Rumble");
        _lblTip.Text = Lang.T("顺序：先开本工具（隐藏实体手柄生效）→ 再启动游戏。",
                              "Order: start this tool first (pad hiding active) → then start the game.");
        // 本版更新卡：语言切换后重填标题与内容
        if (_lblChangelogTitle != null)
        {
            _lblChangelogTitle.Text = Lang.T("v3.4.2 · 本版更新", "v3.4.2 · What's new");
            _lblChangelog.Text = Lang.T("DS4 / DS5 / DSE：实体隔离验证 · 肩键扳机交换 · USB 优先验证",
                                        "DS4 / DS5 / DSE: verified isolation · shoulder/trigger swap · USB validation")
                + "\n" + Lang.T("先等待「隔离检查通过」，再启动游戏；未通过时输出暂停",
                                "Wait for isolation verification before launching the game; output pauses on failure");
        }
        RefreshStatusForLang();  // 状态区双语重建：切换语言后状态行/实体隐藏行不再残留旧语言
        RefreshSensArea();  // 灵敏度说明/致谢/总结统一刷新
        Reflow();
    }

    /// <summary>DS 系手柄连接状态的可见文案：型号 + 读取模式（共享直连）。</summary>
    private string BuildSonyStatus()
        => Lang.T("运行中 · ", "Running · ") + SonyHidPad.KindName() + "（" + SonyHidPad.ModeNote() + "）";

    /// <summary>语言切换后重建状态区：lblStatus/lblHidHide 由后台循环动态写入，切换语言时按当前状态字段重填双语文本。</summary>
    private void RefreshStatusForLang()
    {
        if (_running)
        {
            if (SonyHidPad.IsOpen)
                SetStatus(BuildSonyStatus(), Color.FromArgb(5, 150, 105));
            else if (XboxHidPad.IsOpen)
                SetStatus(Lang.T("运行中 · 手柄已连接（", "Running · pad connected (") + XboxHidPad.KindName() + Lang.T("，Xbox HID）", ", Xbox HID)"), Color.FromArgb(5, 150, 105));
            else if (_sourceSlot >= 0)
                SetStatus(Lang.T("运行中 · 手柄已连接（XInput 槽位 ", "Running · pad connected (XInput slot ") + _sourceSlot + "）", Color.FromArgb(5, 150, 105));
            else
                SetStatus(Lang.T("运行中 · 未检测到手柄 · 请插入手柄", "Running · no pad detected · plug one in"), Color.FromArgb(217, 119, 6));
        }
        else
        {
            SetStatus(Lang.T("已停止", "Stopped"), Color.FromArgb(168, 184, 208));   // 蓝灰：深色背景可读且与主题统一
        }
        lblHidHide.Text = Lang.T("输出协议：虚拟 DS4 · 详细选项见左侧分类",
                                "Output: virtual DS4 · use the navigation for settings");
    }

    /// <summary>
    /// 自适应排布：所有区块宽度跟随窗口（内容宽度 = 窗口宽 - 2×24 边距），
    /// 纵向节奏固定、互不重叠；窗口拖大拖小时文字始终完整显示。
    /// </summary>
    private void Reflow() => ReflowHome();

    /// <summary>把头像控件裁剪为圆形（复刻参考图的圆形 logo 位）。</summary>
    private static void ClipCircle(Control c)
    {
        using var gp = new System.Drawing.Drawing2D.GraphicsPath();
        gp.AddEllipse(0, 0, c.Width - 1, c.Height - 1);
        c.Region = new Region(gp);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (lblStatus == null) return; // 布局尚未构建
        SuspendLayout();
        Reflow();
        ResumeLayout();
        Invalidate();
    }

    /// <summary>窗口移动到不同 DPI 的显示器时，更新缩放因子并按新比例重排。</summary>
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        Dpi.Scale = Math.Max(1f, DeviceDpi / 96f);
        if (lblStatus == null) return;
        SuspendLayout();
        // 窗口/最小尺寸随新 DPI 同步放大，避免控件放大后窗口不变导致内容溢出遮挡；窗口锁定不可缩放
        ClientSize = new Size(Math.Min(Dpi.Px(1120),Screen.FromControl(this).WorkingArea.Width-Dpi.Px(40)), Math.Min(Dpi.Px(800),Screen.FromControl(this).WorkingArea.Height-Dpi.Px(80)));
        MinimumSize = new Size(Dpi.Px(900), Dpi.Px(640));
        MaximumSize = Size.Empty;
        Reflow();
        ResumeLayout();
        Invalidate();
    }

    /// <summary>主页灵敏度区随版本/档位刷新：20 档 + 曲线精调对免费版全开放；Pro 额外标注语音 / 震动。</summary>
    private void RefreshSensArea()
    {
        // 主页极简：灵敏度/微调/宣传全部收进"设置"窗口，主界面只留状态 + 停止转换 + credit
        _sensCard.Visible = false;
        foreach (var b in _sensBtns) b.Visible = false;
        btnFineTune.Visible = false;
        btnVoice.Visible = false;
        btnRumble.Visible = false;
        if (_versionLevel >= 2)
        {
            _lblSensLock.Text = Lang.T("20 档 / 曲线 / 语音 / 震动 · 全部免费开放",
                                       "20 tiers / curves / voice / rumble · all free");
        }
        else
        {
            _lblSensLock.Text = Lang.T("20 档灵敏度（1 低 ~ 20 高）· 曲线精调全开放（免费版即可）",
                                       "20 sens tiers (1 low ~ 20 high) · full curve tuning (free tier)");
        }
        _lblSensLock.ForeColor = Assets.SubText;
        RefreshThanks();   // 版本致谢/引导条随版本切换
        RefreshSummary(); // 主页总结行：模式/灵敏度/快速趴下
        Reflow();          // 布局重排
    }

    /// <summary>档位选定后的转发曲线重建：母版 × 档位系数（左摇杆不缩放，右腰射 50%~100%、右开镜 85%~100%），高级/Pro 叠加 fine tune。</summary>
    private void ApplySensLevel()
    {
        DeriveAllFromMaster(); // 母版形状不被覆盖——切档不丢你调好的总曲线
        SaveSettingsNow();
    }

    private void WireEvents()
    {
        // 语言切换（设置窗口入口）：翻转语言 → 保存 → 全界面文本重填
        btnLang.Click += (_, _) => ToggleLanguage();

        btnToggle.Click += (_, _) =>
        {
            if(UiPreview.Enabled)return;
            if (_running) StopConversion();
            else StartConversion();
        };
        btnRemap.Click += (_, _) => OpenRemap();
        btnFineTune.Click += (_, _) => OpenFeelForm();
        // "按键测试"（左下角，全版本）：实时读取实体手柄原始按键/摇杆/扳机
        btnTest.Click += (_, _) =>
        {
            var form = new TestKeysForm();
            MountStudioForm(form);
        };

        // "恢复默认"：确认后回到出厂设置（保留当前版本档位；语音库自定义内容不动）
        btnReset.Click += (_, _) =>
        {
            var r = MessageBox.Show(
                Lang.T(
                    "确定恢复默认设置？\n\n手感曲线、灵敏度档位、反死区、快速趴下与键位修正将回到出厂调校状态。\n（当前版本档位会保留，自定义语音库不受影响）",
                    "Restore defaults?\n\nCurves, sensitivity, anti-deadzone, fast prone and key remap will return to factory tuning.\n(Your version tier is kept; custom voice library is untouched.)"),
                Lang.T("恢复默认", "Restore Defaults"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;
            ResetToDefaults();
        };

        // 输入模式切换不做按钮：游戏内 L3+R3 组合键切换（手柄侧），主界面仅显示当前模式

        // 语音设置：Pro=完整语音库；免费/高级版不可用（固定 TTS）
        btnVoice.Click += (_, _) => OpenVoice();
        // 版本徽标 = 只读状态显示（当前发布形态：正式版=Pro 全功能版，免费 Demo=免费版）。
        // 不提供三版本循环切换；正式发售拆分 免费/高级/Pro 三档时，以激活码/授权等级区分。
    }

    /// <summary>安全创建 ViGEm 客户端；驱动缺失时置空并记录（不崩溃，由 AutoInstallDrivers 引导安装）。</summary>
    private void TryInitVigem()
    {
        if (_vigem != null) return;
        try
        {
            _vigem = new ViGEmClient();
            _vigemBusMissing = false;
            _vigemError = "";
        }
        catch (Exception ex)
        {
            _vigem = null;
            _vigemBusMissing = true;
            _vigemError = ex.Message;
            CrashLog.Write("ViGEm Bus 驱动初始化失败: " + ex);
        }
    }

    /// <summary>
    /// 取得驱动安装器可执行路径：优先用主程序同目录的旁置文件（兼容旧分发），
    /// 否则从程序集内嵌资源释放到 %LOCALAPPDATA%\CODMControllerProbe\drivers\。
    /// 单文件分发时安装包随 exe 携带，缺驱动时自动释放 → 静默安装，体验=一个程序全搞定。
    /// </summary>
    private string? EnsureDriverInstaller(string exeName)
    {
        var beside = Path.Combine(AppContext.BaseDirectory, exeName);
        if (File.Exists(beside)) return beside;

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CODMControllerProbe", "drivers");
        var target = Path.Combine(dir, exeName);
        try
        {
            if (File.Exists(target)) return target; // 已释放过，复用
            Directory.CreateDirectory(dir);
            var res = exeName switch
            {
                "ViGEmBus_Setup.exe" => "assets.drivers.vigembus_setup.exe",
                "HidHide_1.5.230_x64.exe" => "assets.drivers.hidhide_setup.exe",
                _ => null,
            };
            if (res == null) return null;
            using var s = typeof(MainForm).Assembly.GetManifestResourceStream(res);
            if (s == null)
            {
                CrashLog.Write("内嵌驱动资源缺失: " + res);
                return null;
            }
            using var fs = File.Create(target);
            s.CopyTo(fs);
            CrashLog.Write("已从内嵌资源释放驱动安装包: " + target);
            return target;
        }
        catch (Exception ex)
        {
            CrashLog.Write("释放驱动安装包失败 " + exeName + ": " + ex);
            return null;
        }
    }

    /// <summary>
    /// 静默运行官方驱动安装包。本进程为管理员（app.manifest requireAdministrator），
    /// 子进程继承权限 → 不再弹 UAC。
    /// 参数按安装器类型区分（踩坑记录）：
    ///   · HidHide_*.exe 是 WiX Burn 引导（非 MSI），只认 /quiet /norestart；
    ///   · ViGEmBus_Setup.exe 是 MSI 引导，认 /exenoui /qn /norestart。
    /// 若把 MSI 参数传给 WiX Burn，它不认识参数会按交互模式安装 → 弹窗 + 桌面图标 + 触发重启。
    /// </summary>
    private bool RunSetupSilent(string exeName)
    {
        var path = EnsureDriverInstaller(exeName);
        if (path == null)
        {
            CrashLog.Write("驱动安装包不可用: " + exeName);
            return false;
        }
        try
        {
            string args = exeName.Contains("HidHide", StringComparison.OrdinalIgnoreCase)
                ? (IsHidHideBundleRegistered() ? "/repair /quiet /norestart" : "/quiet /norestart")
                : "/exenoui /qn /norestart";
            var psi = new ProcessStartInfo(path, args)
            {
                UseShellExecute = true,
                CreateNoWindow = true
            };
            var p = Process.Start(psi);
            if (p == null) { CrashLog.Write("驱动安装进程启动失败: " + exeName); return false; }
            if (!p.WaitForExit(120000))
            {
                p.Kill();
                CrashLog.Write("驱动安装超时: " + exeName);
                return false;
            }
            CrashLog.Write($"驱动安装完成 {exeName} 退出码 {p.ExitCode}");
            CancelPendingReboot(); // 安装器可能向系统挂起重启，主动取消，绝不打扰用户
            return p.ExitCode == 0;
        }
        catch (Exception ex)
        {
            CrashLog.Write("驱动安装启动异常 " + exeName + ": " + ex);
            return false;
        }
    }

    /// <summary>取消安装器挂起的自动重启（shutdown /a）。驱动为免重启的 KMDF 过滤器，装完即用，无需重启。</summary>
    private static void CancelPendingReboot()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("shutdown", "/a")
            {
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            CrashLog.Write("已执行 shutdown /a（取消挂起的自动重启）");
        }
        catch (Exception ex) { CrashLog.Write("取消挂起重启失败: " + ex); }
    }

    private static bool IsHidHideBundleRegistered()
    {
        foreach (string root in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
        {
            using var uninstall = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(root);
            if (uninstall == null) continue;
            foreach (string name in uninstall.GetSubKeyNames())
            {
                using var item = uninstall.OpenSubKey(name);
                if ((item?.GetValue("DisplayName") as string)?.Contains("HidHide", StringComparison.OrdinalIgnoreCase) == true) return true;
            }
        }
        return false;
    }

    /// <summary>残留服务项不等于可用驱动：退出卸载可能留下禁用的服务，必须同时检查过滤器注册。</summary>
    private static bool IsHidHideInstalled()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\HidHide");
            using var hidClass = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{745a17a0-74d3-11d0-b6fe-00a0c90f57da}");
            return key != null && key.GetValue("Start") is int start && start != 4 &&
                hidClass?.GetValue("UpperFilters") is string[] filters && filters.Contains("HidHide", StringComparer.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>
    /// 一键驱动自举：检测缺失 → 直接静默安装（不询问用户，零打扰）→ 清理安装器桌面图标
    /// → 重检 → 就绪后提示并自动启动转换。已装驱动时直接启动。
    /// 只有安装全部完成时才提示"已就绪"，绝不让用户决定"要不要装"。
    /// 注意：本方法在后台线程执行（Task.Run）；所有 UI 操作必须 Invoke 回主线程。
    /// </summary>
    private async Task AutoInstallDrivers()
    {
        try
        {
            Ui(()=>SetStatus(Lang.T("正在检查组件启动状态…","Checking component startup…"),Color.FromArgb(245,158,11)));
            var readiness=await ComponentProbe.WaitAsync(AppContext.BaseDirectory);
            if(_closingInputSession)return;
            Ui(()=>{_componentReadyObserved=readiness.Ready;_componentWaiting=!readiness.Ready;if(readiness.Ready)StartConversion();else{_conversionError=readiness.Message;SetStatus(_conversionError,Color.FromArgb(245,158,11));RefreshStudioState();}});
        }
        catch(Exception ex){Ui(()=>{_conversionError=Lang.T("组件检查未完成：","Component check failed: ")+ex.Message;SetStatus(_conversionError,Assets.ErrRed);RefreshStudioState();});}
    }
    /// <summary>后台线程 → 主线程：安全更新 UI（窗口已销毁则静默忽略）。</summary>
    private void Ui(Action act)
    {
        try
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke(act);
        }
        catch { /* 窗口关闭竞态：忽略 */ }
    }

    /// <summary>
    /// 清理驱动安装器（HidHide / ViGEm）在用户桌面与公共桌面创建的快捷方式图标，
    /// 保证用户体验 = 只有一个程序，桌面不出现第三方图标。
    /// 双重匹配：快捷方式文件名，以及快捷方式指向的 exe 目标路径（某些安装器快捷方式名不含关键词）。
    /// </summary>
    private static void CleanDriverDesktopShortcuts()
    {
        try
        {
            var dirs = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            };
            foreach (var dir in dirs)
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var lnk in Directory.GetFiles(dir, "*.lnk"))
                {
                    var name = Path.GetFileNameWithoutExtension(lnk);
                    var target = ShortcutTarget(lnk);
                    bool hit = name.IndexOf("HidHide", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               name.IndexOf("ViGEm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               (target != null && (target.IndexOf("HidHide", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                   target.IndexOf("ViGEm", StringComparison.OrdinalIgnoreCase) >= 0));
                    if (!hit) continue;
                    try { File.Delete(lnk); CrashLog.Write("已清理桌面快捷方式: " + lnk + " → " + target); }
                    catch (Exception ex) { CrashLog.Write("清理快捷方式失败 " + lnk + ": " + ex); }
                }
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("清理桌面快捷方式异常: " + ex);
        }
    }

    /// <summary>读取 .lnk 快捷方式指向的目标 exe 路径（WScript.Shell COM）。失败返回 null。</summary>
    private static string? ShortcutTarget(string lnkPath)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return null;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic sc = shell.CreateShortcut(lnkPath);
            return (string?)sc.TargetPath;
        }
        catch { return null; }
    }

    /// <summary>打开手感窗口（微调 / 曲线编辑共用入口，免费版同样开放）。</summary>
    private void OpenFeelForm()
    {
        var form = new FeelForm(
            _masterLeft, _masterRight, _masterRightAds,       // 编辑对象：母版（出厂基准）
            _curveLeft, _curveRight, _curveRightAds,          // 显示对象：母版 × 当前档位（画布所见即所得）
            () => { SyncMasterToPack(); DeriveAllFromMaster(); SaveSettingsNow(); }, // 改动母版 → 写回封装 → 全档重派生 + 保存
            _versionLevel, _holdProneDelayMs, ms =>
            {
                _holdProneDelayMs = ms;
                SaveSettingsNow();
            },
            _enableFastProne, v => { _enableFastProne = v; SaveSettingsNow(); RefreshSummary(); },
            ft => { _fineTuneGains = ft; SaveSettingsNow(); },
            _freeSensLevel, level =>
            {
                _freeSensLevel = level;
                ApplySensLevel();   // 母版 × 档位 + fine tune，就地重建三转发曲线（手感窗口画布实时反映）
                RefreshSensArea();  // 主页 20 档胶囊同步高亮
            },
            _nudgeCursor, v => { _nudgeCursor = v; SaveSettingsNow(); }, // 推左摇杆移动光标（重生避让）开关
            _outputHz, hz => { _outputHz = hz; SaveSettingsNow(); }); // 输出频率（后台注入线程刷新率，真实生效）
        MountStudioForm(form);
    }

    /// <summary>版本显示名（两档：免费版 / Pro 版）。</summary>
    private static string VersionName(int v) => Lang.T("支持作者", "Support");

    /// <summary>刷新版本徽标：免费=灰描边；Pro=品牌橙实心。</summary>
    private void RefreshVersionBadge()
    {
        btnVersion.Text = VersionName(_versionLevel);
        btnUpgrade.Visible = false;   // 免费版显示"升级 Pro 版"入口；Pro 版隐藏
        _tipCard.Visible = true;     // Buy me Coffee 赞赏卡：免费版可见，Pro 隐藏
        if (_versionLevel >= 2)
        {
            btnVersion.FlatStyle = FlatStyle.Flat;
            btnVersion.FlatAppearance.BorderSize = 0;
            btnVersion.BackColor = Assets.PrimaryOrange;
            btnVersion.ForeColor = Color.White;
            btnVersion.FlatAppearance.MouseOverBackColor = Color.FromArgb(249, 115, 22);
            Assets.Round(btnVersion, 14);
        }
        else
        {
            Assets.StyleOutlineDark(btnVersion, 14);   // 免费版：灰描边徽标
        }
    }

    // ================= 输入模式切换（键鼠 / 手柄原生） =================

    /// <summary>
    /// 切换输入模式：向游戏发送一次 Alt 键（模拟器用它切出/收回光标，进入/退出原生手柄输入模式），
    /// 切到原生模式时把光标移到屏幕左上角，不挡视野。单次按键，不是连发/宏。
    /// 说明（用户手册口径）：光标切出 = 原生手柄输入（辅助瞄准大、转动极速固定）；
    /// 光标收回 = 键鼠输入路径（辅助瞄准小、可在游戏内调键鼠灵敏度）。
    /// </summary>
    // 切换键：U 键（VK 0x55，XT 扫描码 0x16）。真实键盘按 U 可让 CODM 模拟器切出光标。
    // 注意：扫描码注入时 wVk 必须传 0（见 SendVirtualKey），wScan 才是真正发给系统的键。
    private const ushort SWITCH_KEY_VK = 0x55;    // 'U'
    private const ushort SWITCH_KEY_SCAN = 0x16;  // U 的 Set-1 扫描码（0x2C 是 Z，别搞混）

    // 双击方向键左 → I 键（映射到游戏"打开背包/信息"等自定义键位，方式同 U：直投无后续动作）
    private const ushort KEY_I_VK = 0x49;         // 'I'
    private const ushort KEY_I_SCAN = 0x17;       // I 的 Set-1 扫描码
    private const int DBL_PRESS_WINDOW_MS = 350;  // 双击判定窗口

    // 长按 B → 左 Ctrl（趴下）。CODM 手柄原生趴下响应慢，用键盘 Ctrl 直投更快。
    private const ushort KEY_LCONTROL_VK = 0xA2;  // VK_LCONTROL
    private const ushort KEY_LCONTROL_SCAN = 0x1D; // 左 Ctrl Set-1 扫描码

    // 长按 LB+RB → J 键（核弹连杀奖励）。键盘上原本按 J 触发，手柄长按两肩键注入同键，方式同 U 直投。
    private const ushort KEY_J_VK = 0x4A;          // 'J'
    private const ushort KEY_J_SCAN = 0x24;        // J 的 Set-1 扫描码
    private bool _shouldersWasDown;                 // 上帧 LB+RB 是否同按
    private bool _shoulderComboSent;                // 本次长按是否已发 J（一次长按只触发一次）
    private int _shouldersDownTick;                 // 同按起始时间
    private bool _shoulderComboActive;              // 触发后到松开期间屏蔽 LB/RB 转发（避免游戏收到 L1/R1 双重指令）

    /// <summary>
    /// 切换输入模式：forceNative=true（手柄组合键）时强制切到手柄原生模式，
    /// 否则翻转当前模式。核心动作 = 向游戏投递一次 U 键（切光标）+
    /// 延迟移开光标（等游戏异步切出完成）+ 可选语音播报。
    /// </summary>
    private void ToggleInputMode(bool forceNative = false)
    {
        _nativeInput = forceNative || !_nativeInput;
        uint sent = SendVirtualKey(SWITCH_KEY_VK, SWITCH_KEY_SCAN, "U", out string target);
        // 无论切到哪个模式，都把光标移开：组合键/按钮的意图就是"确保锁定在手柄模式"，
        // 且游戏有时切出光标后系统对 On/Off 感知不准，统一移光标最省心。
        _cursorMoveTimer.Stop();
        _cursorMoveTimer.Start();
        // 语音播报：
        // Pro 版 = 完整语音库（自定义 TTS / 真人语音 / 音量）；
        // 免费版/高级版 = 固定 TTS "Let's Go"，仅可开/关，不可自定义
        if (_versionLevel >= 2)
            VoicePlayer.Play(_voiceMode);
        else if (_voiceMode != VoicePlayer.Off)
            VoicePlayer.SpeakLine("Let's go");
        lblMode.Text = _nativeInput
            ? Lang.T("输入模式：手柄原生（辅助瞄准大）", "Input: Pad native (strong aim assist)")
            : Lang.T("输入模式：键鼠（辅助瞄准小）", "Input: K&M (light aim assist)");
        lblMode.ForeColor = _nativeInput ? Assets.PrimaryOrange : Assets.SubText;
        RefreshSummary(); // 模式切换后主页总结同步
        SetStatus(_nativeInput
            ? Lang.T($"运行中 · 输入模式：手柄原生（{target} {sent}/2）", $"Running · Input: Pad native ({target} {sent}/2)")
            : Lang.T($"运行中 · 输入模式：键鼠（{target} {sent}/2）", $"Running · Input: K&M ({target} {sent}/2)"), Color.FromArgb(5, 150, 105));
    }

    /// <summary>
    /// 发送一次完整的按键（按下+抬起）给 CODM 游戏窗口。
    /// 注入顺序：
    /// 1. PostMessage 直接投递 WM_KEYDOWN/WM_KEYUP 到游戏窗口 —— 绕过 SendInput 的
    ///    INJECTED 标记（用户实测：键盘按 U/Alt 有效，SendInput 模拟 U 游戏无响应，
    ///    即游戏过滤了带注入标记的输入）。PostMessage 不要求窗口在前台，更可靠。
    /// 2. 找不到游戏窗口时回退 SendInput（ScanCode 方式，wVk=0、wScan 才是真键）。
    /// 返回注入成功的输入事件数（0=失败，用于诊断），out 返回投递目标描述。
    /// </summary>
    private uint SendVirtualKey(ushort vk, ushort scanCode, string keyName, out string target)
    {
        uint n = 0;
        n += SendKeyState(vk, scanCode, keyName, down: true, out target);
        n += SendKeyState(vk, scanCode, keyName, down: false, out _);
        return n;
    }

    /// <summary>发送单个按键事件（按下或抬起）给 CODM 游戏窗口，逻辑同 SendVirtualKey。
    /// extended=true 表示方向键/右修饰键等扩展键（PostMessage 需 lParam bit24；SendInput 需 KEYEVENTF_EXTENDEDKEY）。</summary>
    private uint SendKeyState(ushort vk, ushort scanCode, string keyName, bool down, out string target, bool extended = false)
    {
        string owner=$"event:{vk}:{scanCode}:{extended}";
        _gameKeys.SetDesired(owner,new GameKey(vk,scanCode,extended),down);_gameKeys.Pump();
        var destination=_gameKeys.CurrentTarget;
        bool success=down?_gameKeys.Delivered(owner):destination.Handle!=IntPtr.Zero&&!_gameKeys.ReleasePending(new GameKey(vk,scanCode,extended));
        target=success?$"已投递 {keyName} 到「{destination.Title}」":$"投递待重试 {keyName}（无可用游戏窗口或投递失败）";
        return success?1u:0u;
    }

    /// <summary>游戏进程和游戏标题必须同时匹配；用户明确选择的进程优先。</summary>
    private static IntPtr FindGameWindow(int processHint=0)
    {
        IntPtr found = IntPtr.Zero;
        IntPtr foreground=GetForegroundWindow();
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            // 排除本进程自己的窗口：本软件标题「CODM 手柄转换」也含 "CODM"，
            // 若被误认成游戏窗口，WASD/Tab/映射键会 PostMessage 打回本软件界面，
            // 直接驱动按钮焦点（实测日志「已投递 A 到「CODM 手柄转换」」即此 bug）。
            GetWindowThreadProcessId(h, out uint pid);
            if (pid == Environment.ProcessId) return true;
            var sb = new StringBuilder(256);
            GetWindowText(h, sb, 256);
            string t = sb.ToString();
            if(!(processHint>0&&pid==(uint)processHint)&&!GameWindowPolicy.HasGameTitle(t))return true;
            try
            {
                using var process=System.Diagnostics.Process.GetProcessById((int)pid);
                if(!GameWindowPolicy.Matches(process.ProcessName,t,_gameProcKeywords,processHint>0&&pid==(uint)processHint))return true;
                if(h==foreground||(processHint>0&&pid==(uint)processHint)){found=h;return false;}
                if(found==IntPtr.Zero)found=h;return true;
            }
            catch{return true;}
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>
    /// 切光标触发（游戏中，不挡操作）。两种方式（设置 → 手感与键位 → 光标与输出）：
    /// 0=组合键（默认 L3+R3）：强制锁定手柄模式（大辅瞄）——按下 L3+R3 的意图就是确保进入 On；
    ///   要切回键鼠模式请用主界面「输入模式」按钮（翻转切换）。
    /// 1=单键一键触发：DS 系按触摸板、Xbox 系按 Home（西瓜键，0x0400；仅 XboxHidPad 有线通道可读）。
    /// 单键模式与触摸板记分表互斥：触摸板已用于切光标，Tab 记分表改由视图键长按。
    /// </summary>
    private void TrackComboPress(XInputNative.XInputGamepad g, bool isDs)
    {
        bool comboDown;
        if (_experimentalCursorEnabled)
        {
            comboDown=KeyMap.Hit(_experimentalCursorKey,g.wButtons,g.bLeftTrigger,g.bRightTrigger);
        }
        else if (_cursorSwitchMode == 1)
        {
            comboDown = isDs ? SonyHidPad.TouchpadPressed : (g.wButtons & 0x0400) != 0;
        }
        else
        {
            ushort combo = _comboMask;
            if (combo == 0) combo = (ushort)(XInputNative.GamepadButtons.LeftThumb | XInputNative.GamepadButtons.RightThumb);
            comboDown = (g.wButtons & combo) == combo;
        }
        if (comboDown && !_comboWasDown)
        {
            // 组合键在后台注入线程被检测到；ToggleInputMode 内部会操作 UI Timer（移开光标）与
            // lblMode/RefreshSummary 等控件，必须调度到 UI 线程执行，否则跨线程调用静默失败（光标不移开）。
            int revision=_mappingRevision;
            Action trigger=()=>{if(_running&&!_mappingCaptureActive&&!_mappingConflictActive&&revision==_mappingRevision&&IsGameForeground())ToggleInputMode(forceNative:!_experimentalCursorEnabled);};
            if (InvokeRequired) BeginInvoke(trigger);
            else trigger();
        }
        _comboWasDown = comboDown;
    }

    /// <summary>
    /// 事件语音播报：语音开启（_voiceMode != Off）时用 TTS 播报固定英文文案。
    /// 检视 / 核弹这类语义明确的事件不走随机语音池（随机词与事件无关），
    /// 统一用固定 TTS 文案，与切换输入模式时的 "Let's go" 口径一致。
    /// </summary>
    private void AnnounceEvent(string line)
    {
        if (_voiceMode != VoicePlayer.Off)
            VoicePlayer.SpeakLine(line);
    }

    /// <summary>
    /// 双击方向键左 → 注入一次 I 键（方式同 U：PostMessage 直投，无后续动作，不切光标）。
    /// 用于把游戏自定义按键（如背包/信息）绑到手柄上，双击是为了与单次 DPadLeft 方向输入区分。
    /// 触发成功时播报语音（检视）。
    /// </summary>
    private void TrackDpadLeftDoublePress(XInputNative.XInputGamepad g)
    {
        bool down = (g.wButtons & (ushort)XInputNative.GamepadButtons.DPadLeft) != 0;
        if (down && !_dpadLeftWasDown)
        {
            int now = Environment.TickCount;
            if (now - _lastDpadLeftPressTick <= DBL_PRESS_WINDOW_MS)
            {
                SendVirtualKey(KEY_I_VK, KEY_I_SCAN, "I", out _);
                SetStatus("双击方向键左 → 已发送 I 键", Color.FromArgb(5, 150, 105));
                AnnounceEvent("Inspect");
            }
            _lastDpadLeftPressTick = now;
        }
        _dpadLeftWasDown = down;
    }

    /// <summary>
    /// 长按蹲键 → 注入左 Ctrl（趴下）。CODM 原生手柄趴下响应慢，用键盘 Ctrl 直投更快。
    /// 触发键跟随键位修正：用户重绑蹲键（_map.Crouch，默认 B）后，快速趴自动跟随新蹲键，
    /// 不再写死 B —— 例如把蹲改到 A，则长按 A 触发快速趴（转发层同样把 A 视为蹲、不再跳）。
    /// 按住超阈值注入 Ctrl 按下，松开时注入 Ctrl 抬起；长按期间暂停蹲键的 DS4 Circle
    /// 转发（避免游戏同时收到 Circle 与 Ctrl 的双重趴下指令），短按蹲键保持原生行为不变。
    /// 阈值 _holdProneDelayMs 默认 150ms，免费版锁定默认值，高级版可调。
    /// 勾选关闭快速趴下时本功能整体停用，蹲键全程走原生。
    /// </summary>
    private void TrackHoldBProne(XInputNative.XInputGamepad g)
    {
        if (!_enableFastProne) { _bWasDown = false; _ctrlHeld = false; return; }
        string keyName = KeyMap.BitName(_map.Crouch);
        bool down = KeyMap.Hit(_map.Crouch, g.wButtons, g.bLeftTrigger, g.bRightTrigger);
        if (down && !_bWasDown)
        {
            _bDownTick = Environment.TickCount;
            _ctrlHeld = false;
        }
        else if (down && _bWasDown && !_ctrlHeld)
        {
            if (Environment.TickCount - _bDownTick >= _holdProneDelayMs)
            {
                SendKeyState(KEY_LCONTROL_VK, KEY_LCONTROL_SCAN, "左 Ctrl", down: true, out _);
                _ctrlHeld = true;
                SetStatus(Lang.T("长按 ", "Hold ") + keyName + Lang.T(" → 已发送左 Ctrl（趴下）", " → sent L-Ctrl (prone)"), Color.FromArgb(5, 150, 105));
            }
        }
        else if (!down && _bWasDown && _ctrlHeld)
        {
            SendKeyState(KEY_LCONTROL_VK, KEY_LCONTROL_SCAN, "左 Ctrl", down: false, out _);
            _ctrlHeld = false;
            SetStatus(keyName + Lang.T(" 松开 → 已抬起左 Ctrl", " released → L-Ctrl up"), Color.FromArgb(107, 114, 128));
        }
        _bWasDown = down;
    }

    // ---- 快速交互（同快速趴下规格）：长按触发键 → 注入 F（游戏交互键）----
    private const ushort KEY_F_VK = 0x46;     // F
    private const ushort KEY_F_SCAN = 0x21;   // F 的 Set-1 扫描码
    private const ushort KEY_H_VK = 0x48;     // H（游戏静步/奔跑切换键）
    private const ushort KEY_H_SCAN = 0x23;   // H 的 Set-1 扫描码

    /// <summary>
    /// 绑定统一使用物理来源。保留此方法作为旧调用适配，不再变换输入。
    /// </summary>
    private (ushort wButtons, byte bLeftTrigger, byte bRightTrigger) GameView(ushort wButtons, byte bLeftTrigger, byte bRightTrigger)
    {
        return (wButtons, bLeftTrigger, bRightTrigger);
    }

    /// <summary>
    /// 长按触发键 → 注入 F（交互键）。同快速趴下规格：短按保持原生行为，
    /// 按住超阈值直投 F 按下（松开时抬起），长按期间暂停触发键的原生 DS4 转发
    /// （避免游戏同时收到该键与 F 的双重指令）。触发键默认 LB，可在键位修正中录制。
    /// </summary>
    private void TrackHoldFastInteract(XInputNative.XInputGamepad g)
    {
        if (!_enableFastInteract) { _interactWasDown = false; _interactHeld = false; return; }
        var (gb, glt, grt) = GameView(g.wButtons, g.bLeftTrigger, g.bRightTrigger); // 跟随肩键/扳机交换
        bool down = KeyMap.Hit(_interactKey, gb, glt, grt);
        if (down && !_interactWasDown)
        {
            _interactDownTick = Environment.TickCount;
            _interactHeld = false;
        }
        else if (down && _interactWasDown && !_interactHeld)
        {
            if (Environment.TickCount - _interactDownTick >= _interactHoldMs)
            {
                SendKeyState(KEY_F_VK, KEY_F_SCAN, "F", down: true, out _);
                _interactHeld = true;
                SetStatus("长按 → 已发送 F（快速交互）", Color.FromArgb(5, 150, 105));
            }
        }
        else if (!down && _interactWasDown && _interactHeld)
        {
            SendKeyState(KEY_F_VK, KEY_F_SCAN, "F", down: false, out _);
            _interactHeld = false;
            SetStatus("触发键松开 → 已抬起 F", Color.FromArgb(107, 114, 128));
        }
        _interactWasDown = down;
    }

    /// <summary>
    /// 长按视图键（View）→ 注入 Tab（打开记分表）。同 U/J/H 键的已验证注入通道（PostMessage 直投）：
    /// 按住超阈值先投一次 Tab 按下，之后按住期间每 80ms 重发一次按下（兼容"消息驱动 + 轮询"两种
    /// 引擎的记分板检测），松开时投 Tab 抬起；长按期间暂停 View 的原生 Share 转发（避免双重指令）。
    /// </summary>
    private void TrackHoldViewTab(XInputNative.XInputGamepad g)
    {
        if (!_enableViewHoldTab) { _viewWasDown = false; _viewHeld = false; return; }
        bool down = (g.wButtons & 0x0020) != 0; // View（Share）
        if (down && !_viewWasDown)
        {
            _viewDownTick = Environment.TickCount;
            _viewRepeatTick = 0;
            _viewHeld = false;
        }
        else if (down && _viewWasDown && !_viewHeld)
        {
            if (Environment.TickCount - _viewDownTick >= _viewHoldMs)
            {
                SendNamedKeyState("view-tab",KEY_TAB_VK, KEY_TAB_SCAN, "Tab", down: true, out _);
                _viewHeld = true;
                _viewRepeatTick = Environment.TickCount;
                SetStatus("长按视图键 → 已发送 Tab（记分表）", Color.FromArgb(5, 150, 105));
            }
        }
        else if (down && _viewWasDown && _viewHeld)
        {
            // 按住保持：每 80ms 重发一次 Tab 按下，保证游戏持续检测到按键（记分板按住显示）
            if (Environment.TickCount - _viewRepeatTick >= 80)
            {
                SendNamedKeyState("view-tab",KEY_TAB_VK, KEY_TAB_SCAN, "Tab", down: true, out _);
                _viewRepeatTick = Environment.TickCount;
            }
        }
        else if (!down && _viewWasDown && _viewHeld)
        {
            SendNamedKeyState("view-tab",KEY_TAB_VK, KEY_TAB_SCAN, "Tab", down: false, out _);
            _viewHeld = false;
            SetStatus("视图键松开 → 已抬起 Tab", Color.FromArgb(107, 114, 128));
        }
        _viewWasDown = down;
    }

    /// <summary>
    /// 触摸板按下 → 注入 Tab（打开记分表）。DS 系列专属（DS4 / DS5 / DSE，标准布局 report[33] bit5）。
    /// 复用视图键 Tab 的已验证注入通道：按下即投 Tab 按下，按住期间每 80ms 重发一次按下，
    /// 松开时投 Tab 抬起。触摸板是独立按键，不与任何原生按钮冲突。
    /// </summary>
    private void TrackTouchpadTab()
    {
        // 单键切光标开启时触摸板让位给切光标（DS 系一键切光标默认键），记分表 Tab 请用视图键长按
        if (_cursorSwitchMode == 1 || !_touchpadTab || !SonyHidPad.IsOpen) { _touchpadWasDown = false; _touchpadHeld = false; return; }
        bool down = SonyHidPad.TouchpadPressed;
        if (down && !_touchpadWasDown)
        {
            SendNamedKeyState("touchpad-tab",KEY_TAB_VK, KEY_TAB_SCAN, "Tab", down: true, out _);
            _touchpadHeld = true;
            _touchpadRepeatTick = Environment.TickCount;
            TestLog.Add($"触摸板按下 → Tab ON");
            SetStatus("触摸板按下 → 已发送 Tab（记分表）", Color.FromArgb(5, 150, 105));
        }
        else if (down && _touchpadWasDown && _touchpadHeld)
        {
            // 按住保持：每 80ms 重发一次 Tab 按下，保证游戏持续检测到按键（记分板按住显示）
            if (Environment.TickCount - _touchpadRepeatTick >= 80)
            {
                SendNamedKeyState("touchpad-tab",KEY_TAB_VK, KEY_TAB_SCAN, "Tab", down: true, out _);
                _touchpadRepeatTick = Environment.TickCount;
            }
        }
        else if (!down && _touchpadWasDown && _touchpadHeld)
        {
            SendNamedKeyState("touchpad-tab",KEY_TAB_VK, KEY_TAB_SCAN, "Tab", down: false, out _);
            _touchpadHeld = false;
            TestLog.Add($"触摸板松开 → Tab OFF");
            SetStatus("触摸板松开 → 已抬起 Tab", Color.FromArgb(107, 114, 128));
        }
        _touchpadWasDown = down;
    }

    /// <summary>物理触发键位（XInput 位 / LT/RT 虚拟位）当前在 DS4 上输出到哪个按钮；其他键返回 null。</summary>
    private DualShock4Button? BindToDs4Button(int k)
    {
        return k switch
        {
            0x0100 => DualShock4Button.ShoulderLeft,
            0x0200 => DualShock4Button.ShoulderRight,
            0x1000 => DualShock4Button.Cross,
            0x2000 => DualShock4Button.Circle,
            0x4000 => DualShock4Button.Square,
            0x8000 => DualShock4Button.Triangle,
            KeyMap.LT => DualShock4Button.TriggerLeft,
            KeyMap.RT => DualShock4Button.TriggerRight,
            0x0040 => DualShock4Button.ThumbLeft,
            0x0080 => DualShock4Button.ThumbRight,
            0x0010 => DualShock4Button.Options,
            0x0020 => DualShock4Button.Share,
            _ => null,
        };
    }

    /// <summary>快速交互长按期间，该 DS4 按钮是否应被暂停转发。</summary>
    private bool InteractSuppresses(DualShock4Button btn) => BindToDs4Button(_interactKey) == btn;

    // ---- 左摇杆奔跑：推满自动触发（可选）或 按下 L3 触发 H 切换（静步/奔跑），摇杆轴保持全向移动 ----
    /// <summary>
    /// 勾选「左摇杆奔跑」后生效（摇杆轴仍正常输出、全向移动）。H 触发方式：
    /// ① 物理 L3（左摇杆按下）边沿 → 始终触发（两种模式都可用，长按不重复）；
    /// ② 自动奔跑（勾选 AutoRun）→ 带滞环的推程状态机，按「游戏默认开启奔跑模式」决定方向：
    ///    - 游戏默认奔跑（RunGameDefaultRun=true，CODM 多数版本）：初始已在奔跑——推满边沿无需按 H，
    ///      回落到"触发阈值-10"（默认 80%）以下按一次 H 切回走路；再推满按一次 H 切回奔跑。
    ///    - 游戏默认走路（false）：推满边沿按一次 H 切奔跑，回落边沿按一次 H 切走路（原逻辑）。
    /// 滞环（进入 ≥90%、退出 <80%）防止推满保持时幅度抖动导致 H 反复乱切。
    /// 同一帧若推满边沿与 L3 边沿同时发生则合并为一次（防止双触发把切换抵消）。
    /// H 为"按下→保持 80ms→抬起"的完整点击，保证游戏能采样到。
    /// </summary>
    private void TrackLeftStickRun(XInputNative.XInputGamepad g)
    {
        // 先处理 H 的自动抬起：按下满 80ms 后抬起，保证游戏采样到一次完整点击
        if (_tapHDownTick != 0)
        {
            if (Environment.TickCount64 - _tapHDownTick >= 80)
            {
                SendKeyState(KEY_H_VK, KEY_H_SCAN, "H", down: false, out _);
                _tapHDownTick = 0;
            }
            if (!_enableRun) return;   // H 还按着且功能已关闭：等抬起后自然结束
        }
        else if (!_enableRun)
        {
            return;
        }

        if(_expWasdMove||_expTouchMove)return; // Only the selected movement scheme may control sprint.
        // L3 = 左摇杆按下（GamepadButtons.LeftThumb=0x0010）
        bool l3 = (g.wButtons & (ushort)XInputNative.GamepadButtons.LeftThumb) != 0;
        // 推杆总幅度
        int mag = (int)Math.Sqrt((double)g.sThumbLX * g.sThumbLX + (double)g.sThumbLY * g.sThumbLY);
        // 滞环：进入奔跑区 ≥ 触发阈值（默认 90%）；退出奔跑区 < 触发阈值-10（默认 80%），防抖动反复切换
        int enterThresh = (int)(32767f * _runThreshold / 100f);
        int exitThresh = (int)(32767f * Math.Max(40, _runThreshold - 10) / 100f);
        bool enter = mag >= enterThresh;
        bool exit = mag < exitThresh;

        // 首次推杆校准（仅自动奔跑开启时需要；当前自动奔跑已暂时移除，恒跳过）
        if (_autoRun && !_runCalibrated)
        {
            if (mag <= 800) return;                       // 仍停留在死区内：等用户真正推杆
            if (mag < enterThresh) { TapH("首次推杆 → 已校准为走路（轻推=走路）"); _runZone = 0; }
            else { _runZone = 1; }                        // 首次即推满：游戏已在奔跑，无需按键
            _runCalibrated = true;
        }

        // 触发：物理 L3 边沿（始终）+ 自动奔跑的滞环状态机边沿（可选）；同帧合并为一次
        // 方向由"游戏默认开启奔跑模式"决定：默认奔跑时，推满已在跑（不按），回落才按 H 切回走路；
        // 默认走路时，推满按 H 切奔跑，回落按 H 切走路。
        // "回中自动切回走路"关闭时，回落边沿不触发（保持奔跑模式，走路靠 L3 手动）。
        bool autoEnter = _autoRun && enter && _runZone == 0;   // 走路区→推满：切到奔跑
        bool autoExit = _autoRun && _runReturnWalk && exit && _runZone == 1; // 奔跑区→回落：切到走路（可关闭）
        bool l3Only = l3 && !_l3Prev && !autoEnter && !autoExit;
        bool trigger = (l3 && !_l3Prev) || autoEnter || autoExit;
        if (trigger)
        {
            if (autoEnter) { TapH("推满 → 已切换 H（奔跑）"); _runZone = 1; }
            else if (autoExit) { TapH("回落 → 已切换 H（走路）"); _runZone = 0; }
            else if (l3Only) { TapH("按 L3 → 已切换 H（静步/奔跑）"); _runZone = 1 - _runZone; } // L3 手动翻转，与自动状态机对齐
            else { TapH("按 L3 → 已切换 H（静步/奔跑）"); }
        }
        _l3Prev = l3;
    }

    /// <summary>按一次 H（按下→保持 80ms→自动抬起；若上一次还没抬起则先抬起再按，保证干净的单次点击）。</summary>
    private void TapH(string hint = "左摇杆跨越奔跑阈值 → 已切换 H（静步/奔跑）")
    {
        if (_tapHDownTick != 0)
        {
            SendKeyState(KEY_H_VK, KEY_H_SCAN, "H", down: false, out _);
            _tapHDownTick = 0;
        }
        SendKeyState(KEY_H_VK, KEY_H_SCAN, "H", down: true, out _);
        _tapHDownTick = Environment.TickCount64;
        SetStatus(hint, Color.FromArgb(5, 150, 105));
    }

    /// <summary>
    /// 长按 LB+RB → 注入一次 J 键（核弹连杀奖励）。键盘原本按 J 触发，手柄长按两肩键直投同键，
    /// 注入方式与 U 键（切换光标）一致：PostMessage 直投游戏窗口，回退 SendInput。
    /// 两肩键同时按住超阈值（默认 400ms）触发一次 J，触发后到全部松开期间屏蔽 LB/RB 的
    /// DS4 转发（避免游戏同时收到 L1/R1 与 J 的双重指令），下次同按重新判定。
    /// 勾选关闭核弹连杀时本功能整体停用，LB/RB 全程走原生转发。
    /// 触发成功时播报语音（核弹）。
    /// </summary>
    private void TrackHoldShoulderCombo(XInputNative.XInputGamepad g)
    {
        if (!_enableNukeCombo)
        {
            _shouldersWasDown = false;
            _shoulderComboSent = false;
            _shoulderComboActive = false;
            return;
        }
        bool down = false;
        {
            // 核弹组合 = 长按"游戏 L1+R1"（默认物理 LB+RB；开启肩键/扳机交换后自动变为物理 LT+RT；战术/杀伤重绑后跟随新键）
            var (gb, glt, grt) = GameView(g.wButtons, g.bLeftTrigger, g.bRightTrigger);
            down = KeyMap.Hit(_map.Tactical, gb, glt, grt) && KeyMap.Hit(_map.Lethal, gb, glt, grt);
        }
        if (down && !_shouldersWasDown)
        {
            _shouldersDownTick = Environment.TickCount;
            _shoulderComboSent = false;
            _shoulderComboActive = false;
        }
        else if (down && _shouldersWasDown && !_shoulderComboSent)
        {
            if (Environment.TickCount - _shouldersDownTick >= _nukeHoldMs)
            {
                SendVirtualKey(KEY_J_VK, KEY_J_SCAN, "J", out _);
                _shoulderComboSent = true;
                _shoulderComboActive = true;   // 触发后到松开：屏蔽 LB/RB 转发
                SetStatus("长按 LB+RB → 已发送 J（核弹连杀）", Color.FromArgb(5, 150, 105));
                AnnounceEvent("Nuke ready");
            }
        }
        else if (!down)
        {
            _shoulderComboActive = false;      // 全部松开：恢复 LB/RB 原生转发
        }
        _shouldersWasDown = down;
    }

    // ================= 按键映射区：手柄键 → 键盘任意键（同 U/J 直投通道） =================
    private const ushort KEY_V_VK = 0x56, KEY_V_SCAN = 0x2F;   // V
    private const ushort KEY_B_VK = 0x42, KEY_B_SCAN = 0x30;   // B（切换武器射击模式 / 实验组合用）

    /// <summary>
    /// 按键映射区注入：对每条「手柄键 → 键盘键」绑定，按下边沿注入键盘键按下、松开注入抬起
    /// （PostMessage 直投，同 U/J/F/H/Tab 已验证通道；扩展键带 lParam bit24）。对应手柄键的原生
    /// DS4 输出由 ForwardToDs4 的 KeyMapSuppresses 暂停——动作跟着键位走，不双触发。
    /// 同一键盘键可被多个手柄键复用；同一手柄键只绑一个键盘键（组合多键走实验功能）。
    /// 键盘绑定使用物理按键，先消费来源再按物理绑定处理原生动作。
    /// </summary>
    private void TrackKeyMapInject(XInputNative.XInputGamepad g)
    {
        lock(_keyboardBindingGate)
        {
            foreach(var binding in Volatile.Read(ref _keyboardBindings))
                _gameKeys.SetDesired("pad:"+binding.PadBit,new GameKey(binding.Vk,binding.Scan,binding.Extended),
                    KeyMap.Hit(binding.PadBit,g.wButtons,g.bLeftTrigger,g.bRightTrigger));
            _gameKeys.Pump();
        }
    }

    /// <summary>映射触发时标注实际物理来源。</summary>
    private string DescribePadSource(int padBit, XInputNative.XInputGamepad g, ushort gb)
    {
        return KeyMap.Hit(padBit,g.wButtons,g.bLeftTrigger,g.bRightTrigger)
            ? "（物理 "+KeyMap.BitName(padBit)+"）" : "";
    }

    /// <summary>
    /// 实验：右摇杆（R3）组合注入 V + B 两个键盘键（组合注入示例——一个手柄键同时输出两个不冲突的键）。
    /// 按住期间同时注入 V、B 按下（直投），松开一起抬起；期间 R3 的原生转发由 ExpRsSuppresses 暂停。
    /// 默认关闭（实验性功能）。
    /// </summary>
    private void TrackExpRsCombo(XInputNative.XInputGamepad g)
    {
        if (!_expRsCombo) { _rsComboDown = false; return; }
        bool down = (g.wButtons & 0x0080) != 0;   // 物理 R3
        if (down && !_rsComboDown)
        {
            SendKeyState(KEY_V_VK, KEY_V_SCAN, "V", down: true, out _);
            SendKeyState(KEY_B_VK, KEY_B_SCAN, "B", down: true, out _);
            _rsComboDown = true;
            TestLog.Add($"实验 R3 组合 → V+B ON");
            SetStatus("实验 · R3 组合：已注入 V + B", Color.FromArgb(5, 150, 105));
        }
        else if (!down && _rsComboDown)
        {
            SendKeyState(KEY_V_VK, KEY_V_SCAN, "V", down: false, out _);
            SendKeyState(KEY_B_VK, KEY_B_SCAN, "B", down: false, out _);
            _rsComboDown = false;
            TestLog.Add($"实验 R3 组合 → V+B OFF");
        }
    }

    /// <summary>
    /// 游戏事件「切换武器射击模式」：触发后向游戏注入一次 B 键（键盘默认键）。
    /// 触发方式可选（设置页「长按触发」小节）：
    ///   单键：触发键（默认 Option）长按超 FireModeHoldMs → 注入一次 B（按下+抬起，同 J 规格）；
    ///   双键：组合键（L3+R3 默认 0=未设置，需录制；不占用切光标/核弹默认组合）两键同按超时 → 注入一次 B。
    /// 触发后到松开期间屏蔽触发键原生转发（FireModeSuppresses），避免双重指令。
    /// </summary>
    private void TrackFireModeSwap(XInputNative.XInputGamepad g)
    {
        if (_shortcutMask==0)
        {
            if(_fireModeWasDown||_fireModeSent)_gameKeys.Release("shortcut");
            _fireModeWasDown = false;
            _fireModeSent = false;
            _fireModeDownTick = 0;
            return;
        }
        bool down=(MappingPolicy.Pressed(g)&_shortcutMask)==_shortcutMask;
        if (down && !_fireModeWasDown)
        {
            _fireModeDownTick = Environment.TickCount;
            _fireModeSent = false;
        }
        if (down && !_fireModeSent)
        {
            if (Environment.TickCount - _fireModeDownTick >= _fireModeHoldMs)
            {
                var key=_shortcutKeyboard;
                _gameKeys.SetDesired("shortcut",new(key.Vk,key.Scan,key.Extended),true);_gameKeys.Pump();
                _fireModeSent=_gameKeys.Delivered("shortcut");
                if(_fireModeSent){TestLog.Add("手柄快捷键 → "+_fireModeTarget+" ON");SetStatus(ReadPadShortcut().Description,Color.FromArgb(5,150,105));}
            }
        }
        else if (!down)
        {
            _gameKeys.Release("shortcut");
            _fireModeSent = false;   // 松开：允许下次重新触发
        }
        _fireModeWasDown = down;
    }

    // ================= 启动 / 停止 =================

    private async void StartConversion()
    {
        if (_running||_startingConversion||UiPreview.Enabled) return;
        _startingConversion=true;_conversionError="";_componentWaiting=false;btnToggle.Enabled=false;RefreshStudioState();
        bool prepared=await Task.Run(()=>PreparePhysicalInputSession());
        _startingConversion=false;
        if(IsDisposed||Disposing||_closingInputSession){RestorePhysicalInputSession();return;}
        btnToggle.Enabled=true;
        if(!prepared){SetStatus(_conversionError,Assets.ErrRed);RefreshStudioState();return;}

        try
        {
            _outputPaused = true;

            if (_vigem == null) TryInitVigem(); // Retry after Windows finishes initializing the driver.

            if (_ds4 == null)
            {
                if (_vigem == null)
                {
                    StopConversion();
                    MessageBox.Show(this,
                        Lang.T("ViGEmBus 接口暂不可用，请在设备检测中刷新组件状态。\n", "ViGEmBus interface is unavailable; refresh device diagnostics.\n") + _vigemError,
                        Lang.T("组件尚未就绪", "Component not ready"),
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                var pad = _vigem.CreateDualShock4Controller();
                pad.AutoSubmitReport = false;
                pad.Connect();
#pragma warning disable CS0618 // FeedbackReceived 已过时但功能正常，正式版再迁移
                pad.FeedbackReceived += OnDs4Feedback;
#pragma warning restore CS0618
                _ds4 = pad;
                pad.ResetReport(); pad.SubmitReport();
            }

            _running = true;
            _startedTick = Environment.TickCount64;
            _sourceSlot = -1;
            _nextSourceProbe=0;
            _noPadWarned = false;
            btnToggle.Text = Lang.T("停止转换", "Stop");
            _timer.Start(); // UI 刷新定时器（显示快照）
            // 高频注入线程：按 _outputHz 读手柄→转换→写虚拟 DS4（真实降低输出延迟，非摆设）
            _loopStop = false;
            _loopThread = new Thread(LoopCore) { IsBackground = true, Name = "InjectLoop" };
            _loopThread.Start();
            // 枪声检测震动：转换运行期间采集系统音频（游戏枪声 → 实体手柄震动），仅 Pro 版
            _shotRumble.Sensitivity = _shotSens;
            _shotRumble.LowBoost = _rumbleLowBoost;
            if (_rumbleByShot && _versionLevel == 2) _shotRumble.Start();
            SetStatus(Lang.T("运行中 · 等待手柄……", "Running · waiting for pad…"), Color.FromArgb(5, 150, 105));
            // The worker establishes the native image whitelist and hiding rules before acquisition.
            // Existing game handles require restarting the game; rule writes alone do not prove isolation.
        }
        catch (Exception ex)
        {
            StopConversion();
            MessageBox.Show(this,
                Lang.T("转换启动失败，请打开设备检测查看实际组件状态。\n\n", "Conversion failed to start; check actual component status in diagnostics.\n\n") + ex.Message,
                Lang.T("CODM 手柄转换", "CODM Controller Bridge"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void StopConversion()
    {
        _timer.Stop();
        _running = false;
        _loopStop = true;
        _cursorMoveTimer.Stop();
        if (_loopThread != null)
        {
            if (_loopThread.IsAlive && _loopThread.ManagedThreadId != Environment.CurrentManagedThreadId)
                _loopThread.Join(9000); // 最多等 300ms：注入线程正在 Sleep 也能及时退出
            _loopThread = null;
        }
        PauseOutput();
        _sourceSlot = -1;
        _shotRumble.Stop();       // 停止枪声监听
        SonyHidPad.Close();   // 关闭 Sony HID 读取通道
        XboxHidPad.Close();   // 关闭 Xbox One/Series HID 直读通道
        // 若 B 还按着（长按已触发 Ctrl），停止时抬起 Ctrl，防止游戏内 Ctrl 卡死
        if (_ctrlHeld)
        {
            SendKeyState(KEY_LCONTROL_VK, KEY_LCONTROL_SCAN, "左 Ctrl", down: false, out _);
            _ctrlHeld = false;
        }
        // 若快速交互长按还触发着（F 按住），停止时一并抬起，防止卡键
        if (_interactHeld)
        {
            SendKeyState(KEY_F_VK, KEY_F_SCAN, "F", down: false, out _);
            _interactHeld = false;
        }
        _l3Prev = false;
        _runZone = _runGameDefaultRun ? 1 : 0;
        _runCalibrated = !_runGameDefaultRun;
        ResetExpMoveKeys(); // 实验移动：松开残留的 WASD/Shift 键 + 触点抬起，防止卡键
        if (_tapHDownTick != 0) { SendKeyState(KEY_H_VK, KEY_H_SCAN, "H", down: false, out _); _tapHDownTick = 0; }
        try { _ds4?.Disconnect(); } catch { /* 忽略 */ }
        _ds4 = null;
        _gameKeys.Release();RestorePhysicalInputSession();
        SetHidHideTip(Lang.T("已停止 · 可安全拔出/更换手柄", "Stopped · safe to unplug or switch pads"), Assets.SubText);
        btnToggle.Text = Lang.T("启动转换", "Start");
        SetStatus(Lang.T("已停止", "Stopped"), Color.FromArgb(107, 114, 128));
    }

    // ================= 输出闸门：隔离只在准备阶段验证，输入循环读取就绪标志 =================

    private bool OutputReady()
    {
        // 启动阶段完成隔离验证后连续输出；高频循环不枚举设备、不写驱动。
        // 转停止（_loopStop）时暂停并释放已注入按键，防止卡键。
        if (!_loopStop&&_physicalSessionReady) { _outputPaused = false; return true; }
        PauseOutput();
        return false;
    }

    /// <summary>生成一键诊断报告（主界面「诊断报告」入口）。覆盖：输入源/设备、虚拟输出、隔离隐藏、
    /// 键位配置、实时原始状态、日志尾段、系统环境——任何问题（串键/检测不到/断连/不顺畅）都能定位。</summary>
    public string BuildDiagnosticReport()
    {
        var sb = new System.Text.StringBuilder(4096);
        void Line(string s) => sb.AppendLine(s);
        string Tail(string path, int n, string fallback)
        {
            try
            {
                if (!System.IO.File.Exists(path)) return fallback;
                var all = System.IO.File.ReadAllLines(path);
                return string.Join("\n", all.Skip(Math.Max(0, all.Length - n)));
            }
            catch { return fallback; }
        }

        Line("===== CODM 手柄转换器 诊断报告 =====");
        Line("生成时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        Line("软件版本：v" + typeof(MainForm).Assembly.GetName().Version?.ToString(3) + "   |   运行：" + (_running ? "转换中" : "已停止"));
        if (_running && _startedTick != 0)
        {
            var d = TimeSpan.FromMilliseconds(Environment.TickCount64 - _startedTick);
            Line("运行时长：" + (d.TotalMinutes >= 1 ? $"{(int)d.TotalMinutes} 分 {d.Seconds} 秒" : $"{d.Seconds} 秒"));
        }
        Line("");

        // ---- 1. 输入源与设备连接 ----
        Line("【1. 输入源与设备连接】");
        var physical=ControllerVisuals.Select(DeviceCatalog.Enumerate());
        Line("  实体设备识别："+(physical.Connected?physical.Title:"等待连接手柄"));
        Line("  当前输入读取：" + (_sourceName.Length > 0 ? _sourceName : "尚未开始读取"));
        if (SonyHidPad.IsOpen)
        {
            Line($"  Sony HID：已打开（{SonyHidPad.KindName()}）");
            Line("  打开模式：" + SonyHidPad.ModeNote());
            Line("  设备路径：" + SonyHidPad.DeviceId);
            if (_lastSonyFrameTick != 0)
                Line("  最后收到帧：" + (Environment.TickCount64 - _lastSonyFrameTick) + " ms 前（>2000 说明已超时暂停）");
            var probe = SonyHidPad.LastProbeInfo ?? "";
            if (probe.Length > 0)
                Line("  最近探测：" + probe.Replace("\n", " | "));
        }
        else Line("  Sony HID：读取通道尚未打开");
        Line($"  Xbox HID：{(XboxHidPad.IsOpen ? "已打开（" + XboxHidPad.KindName() + "）" : "未打开")}");
        Line($"  XInput 槽位：{(_sourceSlot >= 0 ? _sourceSlot.ToString() : "未使用")}");
        Line("");

        // ---- 2. 虚拟输出 ----
        Line("【2. 虚拟输出（ViGEm）】");
        Line("  虚拟 DS4：" + (_ds4 != null ? "已创建并连接" : "未创建"));
        Line("  输出频率：" + _outputHz + " Hz");
        Line("  输出暂停：" + (_outputPaused ? "是" : "否"));
        Line("");

        // ---- 3. 键位配置 ----
        Line("【3. 关键开关与键位绑定】");
        Line("  交换肩键/扳机：" + (_swapShoulderTriggers ? "开（物理 LT↔LB、RT↔RB 对调）" : "关"));
        Line("  左摇杆移动：" + (_expTouchMove ? "触屏（实验）" : _expWasdMove ? "WASD（外圈 Shift）" : "关（摇杆直出）"));
        Line("  按键映射区（" + _padKeyMap.Count + " 条）：");
        foreach (var b in _padKeyMap)
            Line("    " + KeyMap.BitName(b.PadBit) + " → " + b.KeyName);
        Line("  动作绑定：跳跃=" + KeyMap.BitName(_map.Jump) + " 蹲=" + KeyMap.BitName(_map.Crouch)
            + " 换弹=" + KeyMap.BitName(_map.Reload) + " 切枪=" + KeyMap.BitName(_map.Switch)
            + " 瞄准=" + KeyMap.BitName(_map.Aim) + " 射击=" + KeyMap.BitName(_map.Shoot)
            + " 战术=" + KeyMap.BitName(_map.Tactical) + " 杀伤=" + KeyMap.BitName(_map.Lethal)
            + " 近战/标点=" + KeyMap.BitName(_map.MeleePing));
        Line($"  长按阈值：趴下={_holdProneDelayMs}ms 交互={_interactHoldMs}ms 核弹={_nukeHoldMs}ms 视图Tab={_viewHoldMs}ms");
        Line("");

        // ---- 4. 实时原始状态 ----
        Line("【5. 手柄实时原始状态（最近一次读取）】");
        string lastReport = SonyHidPad.LastReportHex ?? "";
        if (SonyHidPad.IsOpen && lastReport.Length > 0)
            Line("  最近 HID 报告：" + lastReport);
        else
            Line("  （无 Sony 原始报告 —— 若已插 DS 手柄仍为空 = 连接/驱动问题）");
        Line("");

        // ---- 5. 日志尾段 ----
        Line("【6. 日志尾段】");
        Line("-- test_log.txt 最后 40 行 --");
        Line(Tail(TestLog.FilePath, 40, "（无日志）"));
        Line("");
        Line("-- sony_diag.log 最后 15 行 --");
        Line(Tail(SonyHidPad.DiagLogPath, 15, "（无日志）"));
        Line("");
        Line("-- crash.log --");
        Line(Tail(CrashLog.FilePath, 15, "（无崩溃记录）"));
        Line("");

        // ---- 6. 系统环境 ----
        Line("【7. 系统环境】");
        Line("  系统：" + Environment.OSVersion.VersionString);
        bool admin;
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            admin = new System.Security.Principal.WindowsPrincipal(identity)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { admin = false; }
        Line("  管理员权限：" + (admin ? "是" : "否"));
        Line("  游戏进程：" + (GameRunningNow() ? "检测到（CODM/模拟器 运行中）" : "未运行"));
        Line("  配置文件：" + SettingsStore.Path_);
        Line("  测试日志：" + TestLog.FilePath);
        Line("");
        Line("===== 报告结束（直接复制以上全部内容粘贴即可） =====");
        return sb.ToString();
    }

    private void PauseOutput()
    {
        _wasdShiftHold.Release();
        if (_outputPaused) return;
        _outputPaused = true;
        try{_ds4?.ResetReport();_ds4?.SubmitReport();}catch{} // Neutralize a held frame without disconnecting the virtual device.
        // 中立报告不等于断开设备；同时释放已注入的键盘键，防止卡键。
        _gameKeys.Release();
        if (_rsComboDown) {
            SendKeyState(KEY_V_VK, KEY_V_SCAN, "V", false, out _);
            SendKeyState(KEY_B_VK, KEY_B_SCAN, "B", false, out _);
            _rsComboDown = false;
        }
        if (_tapHDownTick != 0) {
            SendKeyState(KEY_H_VK, KEY_H_SCAN, "H", false, out _);
            _tapHDownTick = 0;
        }
        _fireModeWasDown = _fireModeSent = false;
        _lastAimOut = _lastShootOut = _lastTacticalOut = _lastLethalOut = false;
        if (_ctrlHeld) SendKeyState(KEY_LCONTROL_VK, KEY_LCONTROL_SCAN, "Ctrl", false, out _);
        if (_interactHeld) SendKeyState(KEY_F_VK, KEY_F_SCAN, "F", false, out _);
        if (_viewHeld || _touchpadHeld) SendKeyState(KEY_TAB_VK, KEY_TAB_SCAN, "Tab", false, out _);
        _ctrlHeld = _interactHeld = _viewHeld = _touchpadHeld = false;
        _comboWasDown = _viewWasDown = _touchpadWasDown = _interactWasDown = false;
        _bDownTick = _interactDownTick = _viewDownTick = 0;
        _shouldersWasDown = _shoulderComboActive = _shoulderComboSent = false;
        _sourceName = "";
        ResetExpMoveKeys();
    }

    /// <summary>游戏进程关键词（与枪声前台门控同一套，含手游模拟器）。</summary>
    private static readonly string[] _gameProcKeywords =
    {
        "codm", "callofduty", "call of duty", "使命召唤", "现代战争", "战区",
        "gameloop", "androidemulator", "aow_exe", "appmarket", "腾讯手游助手", "tencent gaming buddy", "手游助手", "com.activision",
    };

    /// <summary>当前是否有游戏进程在运行（不要求前台：玩家可能切出工具查看状态）。</summary>
    private static bool GameRunningNow()
    {
        return BlockingGameProcesses().Count>0;
    }

    // ================= 轮询：找手柄并转发 =================

    /// <summary>后台注入线程主循环：按 _outputHz 节流读手柄→转换→写虚拟 DS4。UI 更新一律走脏标记。</summary>
    private void LoopCore()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        long nextMs = 0;
        while (!_loopStop)
        {
            PollCore();
            long intervalMs = Math.Max(1, 1000 / _outputHz);
            nextMs += intervalMs;
            long remain = nextMs - sw.ElapsedMilliseconds;
            if (remain > 0)
                Thread.Sleep((int)Math.Min(remain, 50)); // 补足本帧剩余时间；落后超两帧则重对齐
            else if (remain < -intervalMs * 2)
                nextMs = sw.ElapsedMilliseconds;
        }
    }

    /// <summary>UI 线程：每 50ms 把后台线程缓存的显示快照刷到控件。</summary>
    private void RefreshUiFromLoop()
    {
        if (_uiStatusDirty)
        {
            _uiStatusDirty = false;
            lblStatus.Text = _pendingStatus;
            lblStatus.ForeColor = _pendingStatusColor;
        }
        if (_uiRawDirty)
        {
            _uiRawDirty = false;
            lblRaw1.Text = _pendingRaw1;
            lblRaw2.Text = _pendingRaw2;
        }
        if (_uiHidTipDirty)
        {
            _uiHidTipDirty = false;
            lblHidHide.Text = _pendingHidTip;
            lblHidHide.ForeColor = _pendingHidTipColor;
        }
    }

    /// <summary>线程安全的 SetStatus：后台线程调用时缓存脏标记，由 UI 定时器刷新。</summary>
    private void SetStatus(string text, Color color)
    {
        if (InvokeRequired)
        {
            _pendingStatus = "● " + text;
            _pendingStatusColor = color;
            _uiStatusDirty = true;
            return;
        }
        lblStatus.Text = "● " + text;
        lblStatus.ForeColor = color;
    }

    /// <summary>线程安全的 HidHide 提示行。</summary>
    private void SetHidHideTip(string text, Color color)
    {
        if (InvokeRequired)
        {
            _pendingHidTip = text;
            _pendingHidTipColor = color;
            _uiHidTipDirty = true;
            return;
        }
        lblHidHide.Text = text;
        lblHidHide.ForeColor = color;
    }

    /// <summary>线程安全的原始两行占位文本（等待/断开等无手柄帧）。</summary>
    private void UpdateRawPending(string raw1, string raw2)
    {
        if (InvokeRequired)
        {
            _pendingRaw1 = raw1;
            _pendingRaw2 = raw2;
            _uiRawDirty = true;
            return;
        }
        lblRaw1.Text = raw1;
        lblRaw2.Text = raw2;
    }

    /// <summary>线程安全的原始输入显示（后台线程写入缓存，UI 定时器刷新）。</summary>
    private void UpdateRawDisplay(XInputNative.XInputGamepad g)
    {
        _wasdMagnitudePercent=WasdSprint.MagnitudePercent(g.sThumbLX,g.sThumbLY);
        var b = (XInputNative.GamepadButtons)g.wButtons;
        string K(bool on) => on ? "*" : " ";

        string dpad = (b.HasFlag(XInputNative.GamepadButtons.DPadUp) ||
                       b.HasFlag(XInputNative.GamepadButtons.DPadDown) ||
                       b.HasFlag(XInputNative.GamepadButtons.DPadLeft) ||
                       b.HasFlag(XInputNative.GamepadButtons.DPadRight)) ? "*" : " ";

        string raw1 =
            Lang.T("按键", "Keys") + $": A{K(b.HasFlag(XInputNative.GamepadButtons.A))}" +
            $"B{K(b.HasFlag(XInputNative.GamepadButtons.B))}" +
            $"X{K(b.HasFlag(XInputNative.GamepadButtons.X))}" +
            $"Y{K(b.HasFlag(XInputNative.GamepadButtons.Y))} " +
            $"L1{K(b.HasFlag(XInputNative.GamepadButtons.LeftShoulder))}" +
            $"R1{K(b.HasFlag(XInputNative.GamepadButtons.RightShoulder))} " +
            $"L3{K(b.HasFlag(XInputNative.GamepadButtons.LeftThumb))}" +
            $"R3{K(b.HasFlag(XInputNative.GamepadButtons.RightThumb))} " +
            $"D{dpad} St{K(b.HasFlag(XInputNative.GamepadButtons.Start))}" +
            $"Ba{K(b.HasFlag(XInputNative.GamepadButtons.Back))}";

        string raw2 =
            Lang.T("轴", "Axes") + $": LT {g.bLeftTrigger,3} RT {g.bRightTrigger,3} | " +
            $"L({g.sThumbLX,5},{g.sThumbLY,5}) R({g.sThumbRX,5},{g.sThumbRY,5})";

        if (InvokeRequired)
        {
            _pendingRaw1 = raw1;
            _pendingRaw2 = raw2;
            _uiRawDirty = true;
            return;
        }
        lblRaw1.Text = raw1;
        lblRaw2.Text = raw2;
    }

    private void PollCore()
    {
        if (!_running) return;

        // ---- 尚未拿到实体手柄：探测（直连模式，可直接打开设备）----
        if (_sourceSlot < 0 && !SonyHidPad.IsOpen && !XboxHidPad.IsOpen)
        {
            PauseOutput();
            if (_loopStop||!_physicalSessionReady) return;
            if(Environment.TickCount64<_nextSourceProbe)return;
            _nextSourceProbe=Environment.TickCount64+500;
            int slot = _physicalXInputCandidate?FindConnectedXInputSlot():-1;
            if (slot < 0)
            {
                // XInput 没有 → 尝试 Sony HID 通道（DS4 / DualSense / DualSense Edge）
                if (SonyHidPad.Open())
                {
                    _noPadWarned = false;
                    SetStatus(BuildSonyStatus(), Color.FromArgb(5, 150, 105));
                    return;
                }
                // Sony 也没有 → 尝试 Xbox One/Series 手柄 HID 直读（第三方 One 协议手柄无 XInput 驱动时）
                if (XboxHidPad.Open())
                {
                    _noPadWarned = false;
                    SetStatus(Lang.T("运行中 · 手柄已连接（", "Running · pad connected (") + XboxHidPad.KindName() + Lang.T("，Xbox HID）", ", Xbox HID)"), Color.FromArgb(5, 150, 105));
                    return;
                }
                if (!_noPadWarned)
                {
                    _noPadWarned = true;
                    // Sony/Xbox HID 探测失败时附带诊断信息（候选路径数 + 尝试结果），排查"插了 DS4 检测不到"
                    string diag = SonyHidPad.LastProbeInfo ?? "";
                    diag = diag.Replace("\n", " | ");
                    SetStatus(Lang.T("运行中 · 未检测到手柄 · 请插入手柄", "Running · no pad detected · plug one in")
                              + (diag.Length > 0 ? "  [" + diag + "]" : ""), Color.FromArgb(217, 119, 6));
                    UpdateRawPending(Lang.T("按键：等待手柄……", "Keys: waiting for pad…"), Lang.T("轴：等待手柄……", "Axes: waiting for pad…"));
                    // 若手柄已插仍检测不到：多为拔插/休眠后 XInput 缓存未刷新，拔插一次即可
                    SetHidHideTip(Lang.T("提示：手柄已插入但检测不到时，拔插一次手柄即可恢复",
                                              "Tip: pad plugged but not detected? Unplug and replug once"), Color.FromArgb(217, 119, 6));
                }
                return;
            }

            _noPadWarned = false;
            _sourceSlot = slot;

            // 预热：调用一次 XInputGetState，让 XInput 运行时缓存实体手柄句柄
            XInputNative.XInputGetState(slot, out _);

            // 使用白名单转换进程的缓存句柄连续读取。
            SetStatus(Lang.T("运行中 · 手柄已连接（XInput 槽位 ", "Running · pad connected (XInput slot ") + slot + "）", Color.FromArgb(5, 150, 105));
            return;
        }

        // ---- 已连接：靠缓存句柄读取（直连模式，无 HidHide 周期刷新）----

        if (SonyHidPad.IsOpen && SonyHidPad.Read(out var sg))
        {
            _lastSonyFrameTick = Environment.TickCount64;
            UpdateRawDisplay(sg);
            ProcessPhysicalFrame(sg,isDs:true);
            SetSourceStatus(SonyHidPad.KindName());   // 显示具体型号：DualShock 4 / DualSense / DualSense Edge
            return;
        }
        // Sony 无数据（未插 / 休眠 / 刚插入）：不阻塞，继续尝试 Xbox / XInput 通道
        // （此前此处直接 return 会把 Xbox 通道饿死：DS4 空转时 Xbox 手柄不响应）

        if (XboxHidPad.IsOpen && XboxHidPad.Read(out var xg))
        {
            UpdateRawDisplay(xg);
            ProcessPhysicalFrame(xg,isDs:false);
            SetSourceStatus(Lang.T("Xbox 系", "Xbox"));
            return;
        }
        // Sony 打开但本轮回读无新帧：可能是手柄静止节流或瞬时调度抖动——不立即暂停输出。
        // 只有连续 2 秒收不到任何帧才判定超时暂停（否则一次抖动就让游戏看到手柄"断开-重连"= 时断时连）
        if (SonyHidPad.IsOpen)
        {
            if (_lastSonyFrameTick != 0 && Environment.TickCount64 - _lastSonyFrameTick > 2000)
            {
                PauseOutput();
                SetStatus("Sony 输入报告超时（连续 2 秒无数据）· 输出已暂停", Color.FromArgb(217, 119, 6));
            }
            return;
        }
        if (XboxHidPad.IsOpen) { PauseOutput(); return; } // // Xbox HID 已开但暂无数据：等待唤醒，避免反复枚举

        if (!_physicalXInputCandidate || XInputNative.XInputGetState(_sourceSlot, out var state) != XInputNative.ERROR_SUCCESS)
        {
            // 缓存句柄失效（手柄拔出/驱动重置），直连模式下可重新打开设备
            PauseOutput();
            _sourceSlot = -1;
            SetStatus(Lang.T("手柄连接中断 · 请重启本工具", "Pad connection lost · restart this app"), Color.FromArgb(217, 119, 6));
            UpdateRawPending(Lang.T("按键：已断开", "Keys: disconnected"), Lang.T("轴：已断开", "Axes: disconnected"));
            return;
        }

        UpdateRawDisplay(state.Gamepad);
        ProcessPhysicalFrame(state.Gamepad,isDs:false);
        SetSourceStatus(Lang.T("Xbox 系（XInput）", "Xbox (XInput)"));
    }

    /// <summary>当前活动输入源（用于手柄类型自动检测展示，只在源切换时提示一次，避免刷屏）。</summary>
    private string _sourceName = "";
    private bool _sourceIsDs;   // 当前输入源是否为 DS 系手柄（决定 WASD 奔跑提示等）
    private void SetSourceStatus(string name)
    {
        if (name == _sourceName) return;
        _sourceName = name;
        _sourceIsDs = !name.Contains("Xbox");   // DS 系 = 非 Xbox（DualShock 4 / DualSense / DualSense Edge）
        TestLog.Add($"输入源切换: {name}");
        SetStatus(Lang.T("输入源：" + name, "Source: " + name), Color.FromArgb(5, 150, 105));
    }

    /// <summary>扳机按下时记录一行原始状态到日志，用于排查"按扳机触发转向"。</summary>
    private void LogTriggerState(XInputNative.XInputGamepad g)
    {
        bool triggerDown = g.bLeftTrigger > 10 || g.bRightTrigger > 10;
        if (!triggerDown) return;
        if ((DateTime.Now - _lastTriggerLog).TotalMilliseconds < 500) return; // 按住期间最多 0.5s 记一次
        _lastTriggerLog = DateTime.Now;

        try
        {
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] B=0x{g.wButtons:X4} " +
                       $"LT={g.bLeftTrigger,3} RT={g.bRightTrigger,3} " +
                       $"L({g.sThumbLX,6},{g.sThumbLY,6}) R({g.sThumbRX,6},{g.sThumbRY,6})";
            File.AppendAllText(_triggerLogPath, line + Environment.NewLine);
        }
        catch { /* 日志失败不影响功能 */ }
    }

    private static int FindConnectedXInputSlot()
    {
        for (int i = 0; i < 4; i++)
        {
            if (XInputNative.XInputGetCapabilities(i, XInputNative.XINPUT_FLAG_GAMEPAD, out _) ==
                XInputNative.ERROR_SUCCESS)
                return i;
        }
        return -1;
    }

    // ---- DS4 转发（按键位经 KeyMap 修正；摇杆 0-255；DS4 的 Y 轴 0=上，与 XInput 相反需反转） ----

    private void ForwardToDs4(XInputNative.XInputGamepad g)
    {
        // 物理帧按当前动作绑定输出；交换预设已修改绑定，不再二次交换输入。
        var (gb, glt, grt) = GameView(g.wButtons, g.bLeftTrigger, g.bRightTrigger);
        // 按住瞄准键（L2，默认 LT）→ 右摇杆切换到开镜专用曲线（腰射/开镜两套独立设置）；
        // 瞄准键本身照常转发为 L2，不冲突；阈值 30/255≈12%，防轻微误触抖动
        bool ads = KeyMap.Hit(_map.Aim,gb,glt,grt);
        // 左摇杆轴正常输出（反死区曲线），保持全向移动；奔跑由 TrackLeftStickRun 在推满边沿触发 H 切换，互不冲突
        var (lfX, lfY) = ApplyFeelCircular(g.sThumbLX, g.sThumbLY, _curveLeft);
        // 实验：左摇杆移动替代（默认关）。开启后屏蔽左摇杆轴输出，左摇杆改发键盘 WASD（+外圈 Shift）或模拟触屏滑动。
        // 两者互斥：触屏优先（模拟器场景），其次是 WASD（键鼠场景）。
        if (_expTouchMove) { lfX = lfY = 0; TrackExpTouch(g.sThumbLX, g.sThumbLY); }
        else if (_expWasdMove) { lfX = lfY = 0; TrackExpWasd(g.sThumbLX, g.sThumbLY); }
        _ds4!.SetAxisValue(DualShock4Axis.LeftThumbX, ToByteX(lfX));
        _ds4.SetAxisValue(DualShock4Axis.LeftThumbY, ToByteY(lfY));
        var (rfX, rfY) = ApplyFeelCircular(g.sThumbRX, g.sThumbRY, ads ? _curveRightAds : _curveRight);

        // ---- Gyro 体感（实验性，默认关）：Sony 六轴手柄陀螺仪角速度 → 右摇杆微调叠加 ----
        // 水平=yaw（绕 Z 轴，gyroZ）垂直=pitch（绕 X 轴，gyroX）；死区滤静止漂移，一阶低通平滑，
        // 输出钳到 ±0.7（实验 v1 上限防失控），与物理右摇杆叠加后整体钳 ±1（short 单位）。
        if (_gyroEnabled && SonyHidPad.IsOpen && SonyHidPad.GyroRead(out int gxRaw, out int gyRaw, out int gzRaw))
        {
            float gx = (_gyroInvertX ? -gzRaw : gzRaw) / 32768f;
            float gy = (_gyroInvertY ? -gxRaw : gxRaw) / 32768f;
            if (MathF.Abs(gx * 32768f) < _gyroDeadzone) gx = 0f;
            if (MathF.Abs(gy * 32768f) < _gyroDeadzone) gy = 0f;
            float vx = Math.Clamp(gx * _gyroSens * 1.6f, -0.7f, 0.7f);
            float vy = Math.Clamp(gy * _gyroSens * 1.6f, -0.7f, 0.7f);
            _gyroSx += (vx - _gyroSx) * _gyroSmooth;
            _gyroSy += (vy - _gyroSy) * _gyroSmooth;
            rfX = (short)Math.Clamp((int)rfX + (int)(_gyroSx * 32768f), short.MinValue, short.MaxValue);
            rfY = (short)Math.Clamp((int)rfY + (int)(_gyroSy * 32768f), short.MinValue, short.MaxValue);
        }
        _ds4!.SetAxisValue(DualShock4Axis.RightThumbX, ToByteX(rfX));
        _ds4.SetAxisValue(DualShock4Axis.RightThumbY, ToByteY(rfY));

        // 扳机只发"按下/松开"（数字信号），不转发力度模拟量：
        // 诊断确认手柄无串扰，但游戏会把 DS4 的 L2/R2 模拟值误读成转向轴（RT→左转、LT→右转）。
        // FPS 模式扳机力度无意义，模拟量固定 0 即可，按钮位驱动开火/瞄准。
        _ds4.SetSliderValue(DualShock4Slider.LeftTrigger, 0);
        _ds4.SetSliderValue(DualShock4Slider.RightTrigger, 0);

        bool P(int bind) => KeyMap.Hit(bind, gb, glt, grt);
        // 快速交互长按已触发（注入 F）期间，暂停触发键的原生转发，避免游戏收到双重指令（同快速趴下规格）。
        bool SuppressedByInteract(int source) => _interactHeld && source==_interactKey;
        // 原生动作依唯一绑定输出，额外长按动作按来源抑制，而非按按钮外观。
        bool ViewSuppresses(DualShock4Button btn) => _viewHeld && btn == DualShock4Button.Share;
        // 按键映射区：映射键当前按下（键盘键已注入）→ 该键的原生 DS4 位暂停转发（动作跟着键位走，不双触发）
        // 实验 R3 组合（V+B）按住期间：R3 原生位暂停转发
        int PhysicalSource(int source)=>source;
        bool ExpRsSuppresses(int source)=>_expRsCombo&&_rsComboDown&&PhysicalSource(source)==0x0080;
        bool FireModeSuppresses(int source)
        {
            if(!_fireModeEnabled||!_fireModeSent)return false;
            int physical=PhysicalSource(source);
            return _fireModeUseCombo?(physical&_fireModeCombo)!=0:physical==_fireModeKey;
        }
        bool Suppressed(DualShock4Button btn,int source)=>SuppressedByInteract(source)||ExpRsSuppresses(source)||FireModeSuppresses(source);
        _ds4.SetButtonState(DualShock4Button.Cross, P(_map.Jump) && !Suppressed(DualShock4Button.Cross,_map.Jump));
        // 长按 B（已触发 Ctrl 趴下）时暂停 Circle 转发，避免游戏同时收到双重趴下指令；
        // 短按 B（未超阈值）仍走原生 Circle 行为。
        _ds4.SetButtonState(DualShock4Button.Circle, P(_map.Crouch) && !_ctrlHeld && !Suppressed(DualShock4Button.Circle,_map.Crouch));
        _ds4.SetButtonState(DualShock4Button.Square, P(_map.Reload) && !Suppressed(DualShock4Button.Square,_map.Reload));
        _ds4.SetButtonState(DualShock4Button.Triangle, P(_map.Switch) && !Suppressed(DualShock4Button.Triangle,_map.Switch));

        // 长按游戏 L1+R1（默认物理 LB+RB；交换开启后为物理 LT+RT；战术/杀伤重绑后跟随新键）已触发核弹连杀（J）时，
        // 暂停 L1/R1 转发，避免游戏同时收到双重指令；未触发（或关闭功能）时 L1/R1 走原生转发不变。
        bool l1 = P(_map.Tactical);
        bool r1 = P(_map.Lethal);
        _ds4.SetButtonState(DualShock4Button.ShoulderLeft, !_shoulderComboActive && l1 && !Suppressed(DualShock4Button.ShoulderLeft,_map.Tactical));
        _ds4.SetButtonState(DualShock4Button.ShoulderRight, !_shoulderComboActive && r1 && !Suppressed(DualShock4Button.ShoulderRight,_map.Lethal));
        // 诊断日志：L1/R1（战术/杀伤）输出变化时记录物理来源。交换开启后一一对应：
        // 物理 LT → 游戏 L1（战术）、物理 LB → 游戏 L2（瞄准）、物理 RT → 游戏 R1（杀伤）、物理 RB → 游戏 R2（射击）。
        if (l1 != _lastTacticalOut)
        {
            _lastTacticalOut = l1;
            if (l1)
            {
                _lastTacticalSrc = g.bLeftTrigger > 10 ? "LT" : (g.wButtons & 0x0100) != 0 ? "LB" : "—";
                TestLog.Add("输出 L1(战术)=ON（物理 " + _lastTacticalSrc + " 按下）");
            }
            else
            {
                TestLog.Add("输出 L1(战术)=OFF（物理 " + _lastTacticalSrc + " 松开）");
            }
        }
        if (r1 != _lastLethalOut)
        {
            _lastLethalOut = r1;
            if (r1)
            {
                _lastLethalSrc = g.bRightTrigger > 10 ? "RT" : (g.wButtons & 0x0200) != 0 ? "RB" : "—";
                TestLog.Add("输出 R1(杀伤)=ON（物理 " + _lastLethalSrc + " 按下）");
            }
            else
            {
                TestLog.Add("输出 R1(杀伤)=OFF（物理 " + _lastLethalSrc + " 松开）");
            }
        }
        // L3 走原生转发；若 L3 被重绑为近战/标点，则由 MeleeRemapSuppresses 屏蔽原生位、改由 ThumbRight 输出。
        _ds4.SetButtonState(DualShock4Button.ThumbLeft, (gb & 0x0040) != 0 && !_map.UsesSource(0x0040) && !Suppressed(DualShock4Button.ThumbLeft,0x0040));
        // 近战/标点 = 虚拟 DS4 的 R3 位（游戏手柄模式原生近战+标点）。默认绑物理 R3（原生不变）；
        // 重绑其它键后由新键输出 R3 位，物理 R3 位被释放可改作他用。
        _ds4.SetButtonState(DualShock4Button.ThumbRight, P(_map.MeleePing) && !Suppressed(DualShock4Button.ThumbRight,_map.MeleePing));
        // 瞄准/射击（L2/R2）可由键位修正重绑；交换开启后物理 LB→L2、物理 RB→R2
        bool aimOut = P(_map.Aim) && !Suppressed(DualShock4Button.TriggerLeft,_map.Aim);
        bool shootOut = P(_map.Shoot) && !Suppressed(DualShock4Button.TriggerRight,_map.Shoot);
        _ds4.SetButtonState(DualShock4Button.TriggerLeft, aimOut);
        _ds4.SetButtonState(DualShock4Button.TriggerRight, shootOut);
        // 诊断日志：L2/R2 输出变化时记录物理来源（交换开启时按物理 LT 应显示 L2=OFF、物理 LB 应 L2=ON——
        // 若软件显示 OFF 而游戏里还在瞄准，说明游戏内也开了 L1/L2 互换，双重交换抵消）。
        if (aimOut != _lastAimOut)
        {
            _lastAimOut = aimOut;
            if (aimOut)
            {
                _lastAimSrc = g.bLeftTrigger > 10 ? "LT" : (g.wButtons & 0x0100) != 0 ? "LB" : "—";
                TestLog.Add("输出 L2(瞄准)=ON（物理 " + _lastAimSrc + " 按下）");
            }
            else
            {
                TestLog.Add("输出 L2(瞄准)=OFF（物理 " + _lastAimSrc + " 松开）");
            }
        }
        if (shootOut != _lastShootOut)
        {
            _lastShootOut = shootOut;
            if (shootOut)
            {
                _lastShootSrc = g.bRightTrigger > 10 ? "RT" : (g.wButtons & 0x0200) != 0 ? "RB" : "—";
                TestLog.Add("输出 R2(射击)=ON（物理 " + _lastShootSrc + " 按下）");
            }
            else
            {
                TestLog.Add("输出 R2(射击)=OFF（物理 " + _lastShootSrc + " 松开）");
            }
        }
        _ds4.SetButtonState(DualShock4Button.Options, (gb & 0x0010) != 0 && !_map.UsesSource(0x0010) && !Suppressed(DualShock4Button.Options,0x0010));
        _ds4.SetButtonState(DualShock4Button.Share, (gb & 0x0020) != 0 && !_map.UsesSource(0x0020) && !Suppressed(DualShock4Button.Share,0x0020) && !ViewSuppresses(DualShock4Button.Share));
        ushort dpad=gb;
        foreach(int bit in new[]{1,2,4,8})if(_map.UsesSource(bit))dpad=(ushort)(dpad&~bit);
        _ds4.SetDPadDirection(MapDpad((XInputNative.GamepadButtons)dpad));

        // 重生光标避让：重生后游戏把光标放回屏幕中央会挡视野；
        // 玩家推满左摇杆（>90%，奔跑/移动的必然动作）时把光标移到左上角角落。
        // armed 机制：挪一次后松杆（<75%）才重新武装，避免推着不放时每帧重复挪光标。
        // 实验 WASD/触屏开启时左摇杆已改作键盘移动（屏蔽轴输出），不再承担光标避让：
        // 否则键鼠流推杆会把屏幕鼠标反复甩到左上角，干扰本软件界面操作（按钮 hover 乱跳）。
        _ds4.SubmitReport(); // Submit one complete transformed frame; no intermediate states.
        if (!_expWasdMove && !_expTouchMove)
            NudgeCursorIfFullStick(g.sThumbLX, g.sThumbLY);
    }

    // ---- 实验：左摇杆移动替代（默认关；开启后左摇杆轴被屏蔽，摇杆负责 WASD / 触屏滑动）----

    private const ushort KEY_W_VK = 0x57, KEY_W_SCAN = 0x11;
    private const ushort KEY_A_VK = 0x41, KEY_A_SCAN = 0x1E;
    private const ushort KEY_S_VK = 0x53, KEY_S_SCAN = 0x1F;
    private const ushort KEY_D_VK = 0x44, KEY_D_SCAN = 0x20;
    private const ushort KEY_LSHIFT_VK = 0xA0, KEY_LSHIFT_SCAN = 0x2A;

    private static readonly (byte Mask, ushort Vk, ushort Scan, string Name)[] WasdKeys =
    {
        (1 << 0, KEY_W_VK, KEY_W_SCAN, "W"),
        (1 << 1, KEY_A_VK, KEY_A_SCAN, "A"),
        (1 << 2, KEY_S_VK, KEY_S_SCAN, "S"),
        (1 << 3, KEY_D_VK, KEY_D_SCAN, "D"),
    };

    /// <summary>实验 WASD：把左摇杆量化为 8 方向键盘组合（W/A/S/D），幅度 ≥ 阈值时按住 Shift 冲刺。</summary>
    private void TrackExpWasd(short sx, short sy)
    {
        const float DEAD = 0.14f; // 轻推死区：低于此全松，防止漂移乱键
        float mag = MathF.Sqrt((float)sx * sx + (float)sy * sy) / 32768f;
        if (mag < DEAD)
        {
            SetExpWasdMask(0);
            SetExpShift(false, 0f);
            return;
        }

        // 摇杆上=sy>0 → W；左=sx<0 → A；8 方向按 45° 扇区量化
        float ang = MathF.Atan2(sy, sx) * 180f / MathF.PI; // -180..180，0=右
        byte mask = 0;
        // 方向组合：左= A，左下= A+S，下= S，右下= S+D，右= D，右上= D+W，上= W，左上= W+A
        // (ang+180)/45 起算点 = -180°（正左），故扇区 0..7 依次为：左、左下、下、右下、右、右上、上、左上
        int sector = (int)MathF.Round((ang + 180f) / 45f) % 8;
        switch (sector)
        {
            case 0: mask = 1 << 1; break;                  // 左 A
            case 1: mask = (1 << 1) | (1 << 2); break;     // 左下 A+S
            case 2: mask = 1 << 2; break;                  // 下 S
            case 3: mask = (1 << 2) | (1 << 3); break;     // 右下 S+D
            case 4: mask = 1 << 3; break;                  // 右 D
            case 5: mask = (1 << 3) | (1 << 0); break;     // 右上 D+W
            case 6: mask = 1 << 0; break;                  // 上 W
            default: mask = (1 << 0) | (1 << 1); break;    // 左上 W+A
        }
        float magPct = WasdSprint.MagnitudePercent(sx,sy);
        bool shift = WasdSprint.WantsShift(sx,sy,_expWasdThreshold,_wasdShiftDesired);
        // 测试记录：Shift 状态变化时记一行，含原始摇杆值（诊断轴交换/方向反的关键数据）
        if (shift != _wasdShiftDesired)
            TestLog.Add($"WASD: 方向={DirName(mask)} 角度={(int)ang}° 幅度={magPct:0}% 数据(LX={sx},LY={sy}) Shift={(shift ? "ON" : "OFF")}");
        SetExpWasdMask(mask);
        SetExpShift(shift, magPct, DirName(mask), (int)ang);
    }

    /// <summary>WASD 位掩码 → 方向名（用于状态栏诊断：确认物理推杆方向与数据识别方向是否一致）。</summary>
    private static string DirName(byte mask)
    {
        string v = (mask & 1) != 0 ? "W" : (mask & 4) != 0 ? "S" : "";
        string h = (mask & 2) != 0 ? "A" : (mask & 8) != 0 ? "D" : "";
        return h + v == "" ? "·" : (h + v);
    }

    private void SetExpWasdMask(byte mask)
    {
        foreach(var key in WasdKeys)
            _gameKeys.SetDesired("wasd:"+key.Name,new GameKey(key.Vk,key.Scan),(mask&key.Mask)!=0,repeat:true);
        _gameKeys.Pump();_wasdMask=0;
        foreach(var key in WasdKeys)if(_gameKeys.Delivered("wasd:"+key.Name))_wasdMask|=key.Mask;
    }

    /// <summary>WASD Shift 的系统输入状态；调用方负责前台检查与成对释放。</summary>
    private static bool SendRealKey(ushort scanCode, bool down)
    {
        const uint KEYEVENTF_SCANCODE = 0x0008;
        const uint KEYEVENTF_KEYUP = 0x0002;
        INPUT[] inputs =
        {
            new() { type = 1, U = new InputUnion { ki = new KEYBDINPUT { wVk = 0, wScan = scanCode, dwFlags = KEYEVENTF_SCANCODE | (down ? 0u : KEYEVENTF_KEYUP) } } },
        };
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>())==1;
    }

    /// <summary>保留游戏消息，同时在游戏前台建立系统 Shift 按住状态；后台只允许释放。</summary>
    private void SetExpShift(bool down, float magPct, string dirName = "", int angDeg = 0)
    {
        _wasdShiftDesired=down;
        bool previous=_wasdShiftHold.Delivered;
        _wasdShiftHold.SetDesired(down,_gameKeys.CurrentTarget);
        bool delivered=down&&IsGameKeyTargetForeground(_gameKeys.CurrentTarget)&&_wasdShiftHold.Delivered;
        _gameKeys.SetDesired("wasd:shift",new GameKey(KEY_LSHIFT_VK,KEY_LSHIFT_SCAN),delivered);
        if(previous!=delivered)_gameKeys.RefreshRepeatingKeys();
        _gameKeys.Pump();_wasdShiftDown=delivered&&_gameKeys.Delivered("wasd:shift");
        if(_wasdShiftDown&&dirName.Length>0)_wasdDirName=dirName;
    }
    private string _wasdDirName = "";

    /// <summary>实验触屏：把左摇杆量化为前台窗口左下虚拟摇杆区的一次触摸滑动（模拟器场景，需管理员权限）。</summary>
    private void TrackExpTouch(short sx, short sy)
    {
        const float DEAD = 0.14f;
        float mag = MathF.Sqrt((float)sx * sx + (float)sy * sy) / 32768f;
        if (mag < DEAD)
        {
            ExpTouchUp();
            return;
        }
        ExpTouchMove((float)sx / 32768f, (float)sy / 32768f);
    }

    private bool _touchInited;
    private bool _touchFailedShown;
    private bool _touchActive;

    private void ExpTouchInit()
    {
        if (_touchInited || _touchFailedShown) return;
        _touchInited = InitializeTouchInjection(1, 0x1 /* TOUCH_INJECTION_DEFAULT */);
        if (!_touchInited)
        {
            _touchFailedShown = true;
            var msg = Lang.T("模拟触屏需要管理员权限才能注入触摸。\n请以管理员身份运行本软件后再试。",
                "Touch injection requires administrator rights. Run this app as administrator and retry.");
            var cap = Lang.T("实验：模拟触屏", "Exp: touch screen");
            if (InvokeRequired)
                BeginInvoke(new Action(() => MessageBox.Show(msg, cap, MessageBoxButtons.OK, MessageBoxIcon.Warning)));
            else
                MessageBox.Show(msg, cap, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ExpTouchMove(float nx, float ny)
    {
        if (!EnsureGameFront()) { ExpTouchUp(); return; }
        ExpTouchInit();
        if (!_touchInited) return;
        var (cx, cy) = TouchCenter();
        int px = (int)(cx + nx * _expTouchRadius * _expTouchSens / 100f);
        int py = (int)(cy + ny * _expTouchRadius * _expTouchSens / 100f);
        if (!_touchActive)
        {
            if (!EnsureGameFront()) return;
            _touchActive = InjectTouchPoint(px, py, down: true);
        }
        else
        {
            InjectTouchPoint(px, py);
        }
    }

    private void ExpTouchUp()
    {
        if (!_touchActive) return;
        _touchActive = false;
        InjectTouchPoint(0, 0, up: true);
    }

    private static bool EnsureGameFront()
    {
        IntPtr h = GetForegroundWindow();
        if (h == IntPtr.Zero) return false;
        GetWindowThreadProcessId(h, out uint pid);
        if (pid == Environment.ProcessId) return false;
        var sb = new StringBuilder(128);
        GetWindowText(h, sb, 128);
        string t = sb.ToString();
        // 只允许注入到游戏/模拟器窗口（防误触桌面）
        return t.Contains("CODM", StringComparison.OrdinalIgnoreCase) ||
               t.Contains("使命召唤", StringComparison.OrdinalIgnoreCase) ||
               t.Contains("Call of Duty", StringComparison.OrdinalIgnoreCase) ||
               t.Contains("MuMu", StringComparison.OrdinalIgnoreCase) ||
               t.Contains("Nox", StringComparison.OrdinalIgnoreCase) ||
               t.Contains("BlueStacks", StringComparison.OrdinalIgnoreCase) ||
               t.Contains("GameLoop", StringComparison.OrdinalIgnoreCase) ||
               t.Contains("雷电", StringComparison.OrdinalIgnoreCase) ||
               t.Contains("LDPlayer", StringComparison.OrdinalIgnoreCase) ||
               t.Contains("腾讯手游助手", StringComparison.OrdinalIgnoreCase) ||
               t.Contains("Memu", StringComparison.OrdinalIgnoreCase);
    }

    private static (float X, float Y) TouchCenter()
    {
        IntPtr h = GetForegroundWindow();
        GetWindowRect(h, out var r);
        float cx = r.Left + (r.Right - r.Left) * 0.22f;   // 左下虚拟摇杆区
        float cy = r.Top + (r.Bottom - r.Top) * 0.72f;
        return (cx, cy);
    }

    /// <summary>注入一次触摸事件（down=true 按下 / 默认移动 / up=true 抬起）。</summary>
    private static bool InjectTouchPoint(int x, int y, bool down = false, bool up = false)
    {
        uint flags = 0x00000080u /*IN_RANGE*/ | 0x00000020u /*IN_CONTACT*/;
        if (down) flags |= 0x00000002u; // POINTER_FLAG_DOWN
        if (up) flags = 0x00000008u;    // POINTER_FLAG_UP（松开时不带 CONTACT/IN_RANGE）
        var info = new POINTER_TOUCH_INFO
        {
            pointerInfo = new POINTER_INFO
            {
                pointerType = 0x4 /*PT_TOUCH*/,
                pointerId = 0,
                ptPixelLocation = new POINT { X = x, Y = y },
                pointerFlags = flags,
            },
            touchFlags = 0,
            touchMask = 0x1 /*TOUCH_MASK_CONTACTAREA*/,
            rcContact = new RECT { Left = x - 8, Top = y - 8, Right = x + 8, Bottom = y + 8 },
        };
        return InjectTouchInput(1, new[] { info });
    }

    private void ResetExpMoveKeys()
    {
        _wasdShiftHold.Release();
        SetExpWasdMask(0);
        SetExpShift(false, 0f);
        ExpTouchUp();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool InitializeTouchInjection(uint maxTouches, uint injectionMode);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool InjectTouchInput(uint count, POINTER_TOUCH_INFO[] contacts);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINTER_INFO
    {
        public uint pointerType;
        public uint pointerId;
        public uint frameId;
        public uint pointerFlags;
        public IntPtr sourceDevice;
        public IntPtr hwndTarget;
        public POINT ptPixelLocation;
        public POINT ptHimetricLocation;
        public POINT ptPixelLocationRaw;
        public POINT ptHimetricLocationRaw;
        public uint dwTime;
        public uint historyCount;
        public int inputData;
        public uint dwKeyStates;
        public uint PerformanceCount;
        public int ButtonChangeType;
        public int ButtonPressedState;
        public int ButtonState;
        public int ButtonPressed;
        public int ButtonReleased;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINTER_TOUCH_INFO
    {
        public POINTER_INFO pointerInfo;
        public uint touchFlags;
        public uint touchMask;
        public RECT rcContact;
        public RECT rcContactRaw;
        public uint orientation;
        public uint pressure;
    }

    /// <summary>左摇杆推幅 &gt;90% → 光标移左上角（重生后光标在屏幕中央，推杆时自动避开中央视野）。
    /// 受「推左摇杆移动光标」开关控制：关闭后左摇杆不碰光标。</summary>
    private bool _cursorNudgeArmed = true;
    private void NudgeCursorIfFullStick(short lx, short ly)
    {
        if (!_nudgeCursor || !IsGameForeground()) return;
        float mag = MathF.Sqrt((float)lx * lx + (float)ly * ly) / 32768f;
        if (mag < 0.75f) { _cursorNudgeArmed = true; return; }
        if (mag < 0.90f || !_cursorNudgeArmed) return;
        _cursorNudgeArmed = false;
        SetCursorPos(3, 3); // 屏幕左上角，不挡中央视野
    }

    /// <summary>
    /// 圆形死区 + 极坐标归一化（左右摇杆共用）。
    /// 解决"水平轻推容易往45°斜跑"：旧逻辑对 X/Y 各自独立判 ±800 矩形死区、各自套反死区曲线，
    /// 水平推时 Y 只要稍微过 ±800，反死区就把 Y 直接顶到 10~20%，方向瞬间斜成 45°。
    /// 新逻辑：先算径向 r=√(X²+Y²)，r&lt;800 整体归零（圆形死区）；出死区后按真实方向角
    /// (X/r, Y/r) 分配轴向，径向推幅过一次曲线，水平推时 Y 的小漂移按比例缩小，不再突变。
    /// 不修改 XInput 原始读数，只在转发坐标层做变换。
    /// </summary>
    private static (short outX, short outY) ApplyFeelCircular(short sx, short sy, FeelCurve curve)
    {
        const float DEAD = 800f;
        const float MAX = 32768f;
        float r = MathF.Sqrt((float)sx * sx + (float)sy * sy);
        if (r < DEAD) return (0, 0);
        float mag = (r - DEAD) / (MAX - DEAD);
        if (mag < 0f) mag = 0f;
        if (mag > 1f) mag = 1f;
        float outMag = curve.Eval(mag);
        // 单位方向向量（极坐标归一化，外圈为圆）
        float ux = sx / r;
        float uy = sy / r;
        float fx = ux * outMag * curve.GainX;
        float fy = uy * outMag * curve.GainY;
        fx = Math.Clamp(fx, -1f, 1f);
        fy = Math.Clamp(fy, -1f, 1f);
        return ((short)(fx * 32767f), (short)(fy * 32767f));
    }

    /// <summary>XInput 轴（-32768..32767）→ DS4 轴（0..255，中心 128）。X 轴方向一致：左 0 右 255。</summary>
    private static byte ToByteX(short s) => s < 0 ? (byte)(128 + Math.Round(s * 128.0 / 32768)) : (byte)(128 + Math.Round(s * 127.0 / 32767));

    /// <summary>DS4 的 Y 轴方向与 XInput 相反：DS4 推上 = 0、推下 = 255；XInput 推上 = +32767。</summary>
    private static byte ToByteY(short s) => s > 0 ? (byte)(128 - Math.Round(s * 128.0 / 32767)) : (byte)(128 - Math.Round(s * 127.0 / 32768));

    private static DualShock4DPadDirection MapDpad(XInputNative.GamepadButtons b)
    {
        bool up = b.HasFlag(XInputNative.GamepadButtons.DPadUp);
        bool down = b.HasFlag(XInputNative.GamepadButtons.DPadDown);
        bool left = b.HasFlag(XInputNative.GamepadButtons.DPadLeft);
        bool right = b.HasFlag(XInputNative.GamepadButtons.DPadRight);
        if (up && right) return DualShock4DPadDirection.Northeast;
        if (up && left) return DualShock4DPadDirection.Northwest;
        if (down && right) return DualShock4DPadDirection.Southeast;
        if (down && left) return DualShock4DPadDirection.Southwest;
        if (up) return DualShock4DPadDirection.North;
        if (down) return DualShock4DPadDirection.South;
        if (left) return DualShock4DPadDirection.West;
        if (right) return DualShock4DPadDirection.East;
        return DualShock4DPadDirection.None;
    }

    // ================= 震动转发 =================

    private void OnDs4Feedback(object? sender, DualShock4FeedbackReceivedEventArgs e)
    {
        ForwardVibration(e.LargeMotor, e.SmallMotor);
    }

    /// <summary>枪声检测到 → 实体手柄震动脉冲。
    /// 识别逻辑：底噪追踪（精准、有针对性）。开枪力度下限 = 用户设置的"开枪力度下限"
    /// （默认 75%，比之前 55% 明显更足），越响越强，巨响封顶 100%；
    /// 连发脉冲 240ms、单发 120ms。独立于游戏震动通道，可叠加。</summary>
    private void OnShotDetected(float intensity, bool burst)
    {
        if (!_running || !_rumbleOn || !_rumbleByShot || _versionLevel != 2) return;
        // 响度 → 力度：从用户设置的力度下限起步（默认 75%，明显可感），越响越强，巨响封顶 100%
        float floor = _rumbleMinLevel / 100f;
        float level = Math.Clamp(floor + (intensity - 1f) * 0.20f, floor, 1.00f);
        byte power = (byte)((int)(level * _rumbleStrength) * 255 / 100);
        ForwardVibration(power, power);
        // 连发 → 更长脉冲（240ms），单发 → 中脉冲（120ms）
        _ = PulseStopAsync(burst ? 240 : 120);
    }

    private async Task PulseStopAsync(int ms)
    {
        await Task.Delay(ms);
        if (!_running || _versionLevel != 2) return;
        ForwardVibration(0, 0);
    }

    /// <summary>切换界面语言（设置窗口入口）。</summary>
    private void ToggleLanguage()
    {
        Lang.En = !Lang.En;
        ApplyLang();
        SaveSettingsNow();
    }

    /// <summary>设置窗口（v0.3 二级菜单）：一切可调选项收进这里，主界面只留 启动/状态/总结/致谢。</summary>
    private void OpenSettings(int section = 0)
    {
        // 打开设置前把最新档位/趴下值带进去
        var form = new SettingsForm(
            _versionLevel, _freeSensLevel,
            _enableFastProne, _holdProneDelayMs, _nukeHoldMs, _interactHoldMs,
            _activePackKey, SwitchPack,     // 当前封装 + 封装切换
            SetSensLevelFromSettings,     // 档位 1~20
            SetAdsMultiplierFromSettings, // ADS 开镜倍率
            _adsMultiplier,
            () => new[] { _curveLeft, _curveRight, _curveRightAds }, // 曲线预览（显示对象，随封装/档位/倍率实时）
            SetProneFromSettings,         // 快速趴下（开关 + 时长）
            ms => { _nukeHoldMs = ms; SaveSettingsNow(); },            // 核弹长按时长（独立）
            ms => { _interactHoldMs = ms; SaveSettingsNow(); },        // 快速交互长按时长（独立）
            () => OpenRemap(),            // 键位修正
            () => OpenFeelForm(),         // 微调手感
            () => OpenVoice(),            // 语音设置
            () => OpenRumbleSettings(),   // 震动设置
            () => OpenDiag(),             // 一键诊断
            () => ExportConfig(),         // 导出配置
            () => ImportConfig(),         // 导入配置
            () => RequestStudioReset(),      // 页内确认恢复默认
            () => ToggleLanguage(),       // 切换语言
            () => { MountStudioForm(new TestKeysForm()); }, // 按键测试
            () => { MountStudioForm(new GuideForm()); },   // 使用引导
            () => OpenHidProbe(),          // 原始 HID 报告查看（实验 · 抓国产手柄陀螺仪布局）
            () => { MountStudioForm(new TestLogForm()); }, // 测试记录（自动记录测试日志 · 一键复制）
            () => OpenUnlock(),           // 解锁 Pro 版（免费版可见入口）
            _nudgeCursor, v => { _nudgeCursor = v; SaveSettingsNow(); },   // 推左摇杆移动光标（三版本可勾选）
            _swapShoulderTriggers, SetShoulderPreset, // 一键交换肩键/扳机
            _cursorSwitchMode, v => MappingEdited(()=>_cursorSwitchMode = v ? 1 : 0),   // 一键切光标（单键：Xbox=Home / DS=触摸板）
            _enableFastInteract, v => MappingEdited(()=>_enableFastInteract = v),   // 快速交互（长按触发 F）
            _enableRun, _runThreshold, _autoRun, _runGameDefaultRun, _runReturnWalk,
            (run, thresh, auto, gameDefault, returnWalk) =>
            {
                bool modeChanged = gameDefault != _runGameDefaultRun;
                _enableRun = run;
                _runThreshold = thresh;
                _autoRun = auto;
                _runGameDefaultRun = gameDefault;
                _runReturnWalk = returnWalk;
                if (!run || modeChanged)
                {
                    _l3Prev = false;
                    _runZone = _runGameDefaultRun ? 1 : 0;
                    _runCalibrated = !_runGameDefaultRun;
                    if (_tapHDownTick != 0) { SendKeyState(KEY_H_VK, KEY_H_SCAN, "H", down: false, out _); _tapHDownTick = 0; }
                }
                SaveSettingsNow();
            }, // 左摇杆奔跑（L3 切换 + 可选自动奔跑）
            false, _ => { }, // Retained constructor parameters for old callers; exit cleanup is retired.
            _gyroEnabled, _gyroSens, _gyroDeadzone, _gyroSmooth, _gyroInvertX, _gyroInvertY,
            (en, sens, dz, sm, invX, invY) =>
            {
                _gyroEnabled = en;
                _gyroSens = sens;
                _gyroDeadzone = dz;
                _gyroSmooth = sm;
                _gyroInvertX = invX;
                _gyroInvertY = invY;
                _gyroSx = _gyroSy = 0f; // 参数变化后重置平滑状态
                SaveSettingsNow();
            },
            _expWasdMove, _expWasdThreshold, _expTouchMove, _expTouchRadius, _expTouchSens,
            (wasdOn, wasdThr, touchOn, touchR, touchS) =>
            {
                _gameKeys.Release("wasd:");
                _expWasdMove = wasdOn && !touchOn;
                _expWasdThreshold = wasdThr;
                _expTouchMove = touchOn;
                _expTouchRadius = touchR;
                _expTouchSens = touchS;
                if (!wasdOn) ResetExpMoveKeys(); // 关闭实验移动时松开残留键
                SaveSettingsNow();
            }, // 实验：左摇杆移动替代（触屏保留实验；WASD 已转正移入 手感与键位）
            _enableViewHoldTab, _viewHoldMs,
            (on, ms) =>
            {
                MappingEdited(()=>{_enableViewHoldTab=on;_viewHoldMs=ms;});
            },
            _map.MeleePing, () => OpenRemap(focusMelee: true), // 近战/标点：设置页直接显示绑定 + 重设（打开弹窗即进入录键）
            _sourceIsDs, // 当前输入源是否 DS 系（决定 WASD 奔跑提示）
            // ---- v3.1 新增：按键映射区 / 实验 R3 组合 / 切换武器射击模式 ----
            new List<PadKeyBind>(_padKeyMap),
            bindings =>
            {
                ReplaceKeyboardBindings(bindings);
                SaveSettingsNow();
            },
            _expRsCombo, en => { _expRsCombo = en; if (!en) _rsComboDown = false; SaveSettingsNow(); },
            _fireModeEnabled, _fireModeHoldMs, _fireModeUseCombo, _fireModeKey, _fireModeCombo,
            (on, ms, combo, key, mask) =>
            {
                _fireModeEnabled = on;
                _fireModeHoldMs = ms;
                _fireModeUseCombo = combo;
                _fireModeKey = key;
                _fireModeCombo = mask;
                _fireModeWasDown = _fireModeSent = false;
                MappingEdited(()=>{});
            }, initialSection:section,onReport:()=>MountStudioForm(new DiagnosticForm(BuildDiagnosticReport)),movementStatus:WasdLiveStatus,mappingStatus:()=>_gameKeys.MappingStatus(),onCleanup:()=>{Close();UninstallDrivers();},keyboardChordsEnabled:_keyboardChordsEnabled,keyboardChords:_keyboardChords.Select(c=>c.Normalize()!).ToList(),onKeyboardChords:SetKeyboardChords,experimentalCursorEnabled:_experimentalCursorEnabled,experimentalCursorKey:_experimentalCursorKey,onExperimentalCursor:SetExperimentalCursor,confirmKeyboardBindings:ConfirmKeyboardBindings,captureChanged:SetMappingCapture,readKeyboardChords:()=>{lock(_keyboardBindingGate)return(_keyboardChordsEnabled,_keyboardChords.Select(c=>c.Normalize()!).ToList());},readShortcut:ReadPadShortcut,onShortcut:SetPadShortcut,readPhysical:ReadPhysicalCapture);
        form.PrepareForStudio(section, (child,title) => MountStudioForm(child,title));
        MountStudioForm(form,StudioSections[Math.Clamp(section+1,1,7)],root:true);
        RefreshSensArea(); // 设置窗口里改的档位/趴下 → 主界面总结行同步
    }

    /// <summary>解锁 Pro 窗口：收款码（待嵌入）+ 激活码输入；激活成功 → 保存 + 刷新版本状态。</summary>
    private void OpenUnlock()
    {
        MountStudioForm(new SupportForm());
    }

    /// <summary>设置窗口回调：档位 0~19 选定 → 重算转发曲线并保存（全版本开放）。</summary>
    private void SetSensLevelFromSettings(int idx)
    {
        _freeSensLevel = Math.Clamp(idx, 0, 19);
        ApplySensLevel();
        SaveSettingsNow();
    }

    /// <summary>设置窗口回调：ADS（开镜）灵敏度倍率 0.50~1.50 → 重算开镜曲线并保存（全版本开放）。</summary>
    private void SetAdsMultiplierFromSettings(float v)
    {
        _adsMultiplier = Math.Clamp(v, 0.5f, 1.5f);
        DeriveAllFromMaster();
        SaveSettingsNow();
    }

    /// <summary>设置窗口回调：快速趴下开关 + 时长（全版本开放）。</summary>
    private void SetProneFromSettings(bool enable, int ms)
    {
        MappingEdited(()=>{_enableFastProne=enable;_holdProneDelayMs=Math.Clamp(ms,100,1000);});
    }

    /// <summary>键位修正窗口（设置窗口入口）。</summary>
    private void OpenRemap(bool focusMelee = false)
    {
        var form = new RemapForm(_map, SaveSettingsNow, _comboMask,
            v => { _comboMask = v; SaveSettingsNow(); },
            _enableNukeCombo, v => MappingEdited(()=>_enableNukeCombo = v),
            _enableFastInteract, v => MappingEdited(()=>_enableFastInteract = v),
            _interactKey, v => MappingEdited(()=>_interactKey = v),
            _swapShoulderTriggers, SetShoulderPreset,
            _map.MeleePing, v => { _map.MeleePing = v; SaveSettingsNow(); },
            autoCaptureMelee: focusMelee,assignAction:AssignPhysicalAction,restoreActions:RestoreActionBindings,readPhysical:ReadPhysicalCapture,captureChanged:SetMappingCapture,sourceStatus:SourceOverrideName,readSwap:()=>_swapShoulderTriggers,readInteract:()=>_enableFastInteract);
        MountStudioForm(form);
    }

    /// <summary>语音设置窗口（设置窗口入口，Pro 专属；免费/高级提示升级）。</summary>
    private void OpenVoice()
    {
        if (_versionLevel < 2)
        {
            MessageBox.Show(
                Lang.T("语音设置（语音库/自定义提示音/随机播放）是 Pro 版功能。\n免费版已内置固定切换提示音。",
                       "Voice settings (library / custom prompts / shuffle) are a Pro feature.\nFree tier has a built-in fixed prompt."),
                Lang.T("需 Pro 版", "Pro Required"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var form = new VoiceSettingsForm(_voiceMode, true, mode =>
        {
            _voiceMode = mode;
            SaveSettingsNow();
        });
        MountStudioForm(form);
    }

    /// <summary>一键诊断：驱动/隐藏/手柄/音频 四个状态灯 + 修复提示，砍掉 80% 售后问题。</summary>
    private void OpenDiag()
    {
        var form = new DiagForm(CaptureStartupDiagnosticsAsync,StartConversion,()=>MountStudioForm(new GuideForm()));
        MountStudioForm(form);
    }

    /// <summary>打开原始 HID 报告查看器（实验工具）：抓国产手柄陀螺仪字节布局。</summary>
    private void OpenHidProbe()
    {
        var form = new HidProbeForm();
        MountStudioForm(form);
    }

    /// <summary>打开震动设置窗口：总开关 / 枪声触发 / 强度 / 力度下限 / 低频增强 / 灵敏度 / 前台门控。仅 Pro 版。</summary>
    private void OpenRumbleSettings()
    {
        if (_versionLevel != 2) return;
        string gameName = "";
        if (_gameProcId != 0)
        {
            try { gameName = System.Diagnostics.Process.GetProcessById(_gameProcId).ProcessName; }
            catch { gameName = ""; }
        }
        var form = new RumbleSettingsForm(
            _rumbleOn, _rumbleByShot, _rumbleStrength, _rumbleMinLevel, _rumbleLowBoost, _shotSens,
            _rumbleFocusOnly, gameName,
            (rumbleOn, shotOn, strength, minLevel, lowBoost, sens) =>
            {
                _rumbleOn = rumbleOn;
                _rumbleByShot = shotOn;
                _rumbleStrength = strength;
                _rumbleMinLevel = minLevel;
                _rumbleLowBoost = lowBoost;
                _shotSens = sens;
                _shotRumble.Sensitivity = sens;
                _shotRumble.LowBoost = lowBoost;
                if (_running)
                {
                    if (shotOn) _shotRumble.Start();
                    else _shotRumble.Stop();
                }
                SaveSettingsNow();
            },
            focusOnly =>
            {
                _rumbleFocusOnly = focusOnly;
                SaveSettingsNow();
            },
            () => PickCurrentGame(), // 识别当前前台窗口为游戏
            // 测试震动：按当前强度立即震一下（验证马达 + 手感）
            () =>
            {
                if (!_running || _versionLevel != 2) return;
                byte power = (byte)(_rumbleStrength * 255 / 100);
                ForwardVibration(power, power);
                _ = PulseStopAsync(180);
            });
        MountStudioForm(form);
    }

    /// <summary>把游戏发给虚拟 DS4 的震动指令按"开关 + 强度"转发到真实实体手柄。
    /// 通道优先级：XInput 槽位（Xbox360/One/Series 原生）→ Sony HID → Xbox HID。</summary>
    private void ForwardVibration(byte large, byte small)
    {
        if (!_rumbleOn || _versionLevel != 2) return; // 震动仅 Pro 版
        int s = _rumbleStrength;
        if (s <= 0) return;
        if (s >= 100) { /* 原值直通 */ }
        else
        {
            large = (byte)(large * s / 100);
            small = (byte)(small * s / 100);
        }
        if (_sourceSlot >= 0)
        {
            var vib = new XInputNative.XInputVibration
            {
                wLeftMotorSpeed = (ushort)(large * 257),
                wRightMotorSpeed = (ushort)(small * 257),
            };
            try { XInputNative.XInputSetState(_sourceSlot, ref vib); } catch { }
            return;
        }
        if (SonyHidPad.IsOpen) { SonyHidPad.SetRumble(large, small); return; }
        if (XboxHidPad.IsOpen) { XboxHidPad.SetRumble(large, small); }
    }


    // ---- 枪声震动 · 前台门控（仅游戏前台时检测，后台不误触发）----

    /// <summary>当前台是否为"游戏"：已在震动设置锁定游戏进程 → 精确匹配 PID；
    /// 未锁定 → 严格判定（进程名或窗口标题含游戏特征关键词）才算游戏前台。
    /// 旧版"非系统进程即游戏"太宽松：浏览器/微信等前台也会被误判成游戏 → 后台误震动，已改为严格匹配。</summary>
    private bool IsGameForeground()
    {
        try
        {
            IntPtr h = GetForegroundWindow();
            if (h == IntPtr.Zero) return false;
            GetWindowThreadProcessId(h, out uint pid);
            if (pid == 0 || pid == Environment.ProcessId) return false;
            using var process=System.Diagnostics.Process.GetProcessById((int)pid);
            return GameWindowPolicy.Matches(process.ProcessName,WindowTitle(h)??"",_gameProcKeywords,_gameProcId>0&&pid==(uint)_gameProcId);
        }
        catch { return false; }
    }

    /// <summary>读取前台窗口标题（前台判定用）。</summary>
    private static string? WindowTitle(IntPtr h)
    {
        try
        {
            var sb = new System.Text.StringBuilder(256);
            int n = GetWindowText(h, sb, sb.Capacity);
            return n > 0 ? sb.ToString() : "";
        }
        catch { return ""; }
    }

    /// <summary>把当前前台窗口进程识别为"游戏"（震动设置里点按钮调用），返回进程名用于显示。</summary>
    private string PickCurrentGame()
    {
        try
        {
            IntPtr h = GetForegroundWindow();
            if (h == IntPtr.Zero) return "";
            GetWindowThreadProcessId(h, out uint pid);
            if (pid == 0) return "";
            _gameProcId = (int)pid;
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch { return ""; }
    }

    /// <summary>
    /// 仅由维护页面的明确点击打开配套清理界面。正常关窗不会调用。
    /// 清理范围、配置是否保留与重启时间由用户在清理工具中选择。
    /// </summary>
    private void UninstallDrivers()
    {
        try {
            string helper=Path.Combine(AppContext.BaseDirectory,"CODMControllerProbeUninstaller.exe");
            if(!File.Exists(helper)){CrashLog.Write("缺少配套清理工具，请使用完整安装版或免安装包");return;}
            var start=new ProcessStartInfo(helper){UseShellExecute=true,WindowStyle=ProcessWindowStyle.Normal};
            if(File.Exists(Path.Combine(AppContext.BaseDirectory,"portable-files.json")))start.Arguments="--portable";
            Process.Start(start)?.Dispose();
        } catch(Exception ex){CrashLog.Write("打开清理工具未完成："+ex.Message);}
    }
    // ---- Win32：模拟单次按键（切换游戏输入模式）+ 移动光标 ----

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public MOUSEINPUT mi; // INPUT must include the largest union member: 40 bytes on x64.
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx,dy;
        public uint mouseData,dwFlags,time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError=true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    // ---- 窗口投递（PostMessage 直投，绕过 SendInput 的 INJECTED 标记）----

    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
