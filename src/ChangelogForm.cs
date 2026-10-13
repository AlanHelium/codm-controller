using System;
using System.Drawing;
using System.Windows.Forms;

namespace CODMControllerProbe;

/// <summary>
/// 历史更新日志窗口：从右上角"更新日志"入口打开。
/// 逐版本展示「改了什么 / 为什么这样改 / 背后怎么思考」，双语，深色主题，可滚动。
/// 以后每发布一版，都把新版本条目加在 <see cref="Entries"/> 的最前面。
/// </summary>
public sealed class ChangelogForm : Form
{
    /// <summary>一条版本记录：版本号 + 标题 + 内容 + 为什么（中英各一份）。</summary>
    private sealed record Entry(string Ver, string Title, string Body, string Why,
                                string EnVer, string EnTitle, string EnBody, string EnWhy);

    // 条目顺序 = 时间倒序（最新在前）。新增版本时在数组最前面插入一条。
    private static readonly Entry[] Entries =
    {
        new(
            "v3.4.1", "WASD 外圈奔跑与长按",
            "WASD 方向沿用游戏消息通道，长按增加正确的重复消息。外圈 Shift 在游戏前台同时建立系统按住状态，切出游戏、回中及停止转换时释放。修正 Windows INPUT 结构长度，并按实际发送结果重试。",
            "收到字母或 Shift 消息不等于游戏能读取系统键盘状态。已正常的游戏动作交换、自定义键盘映射及手感算法继续沿用原实现。",
            "v3.4.1", "WASD sprint and held keys",
            "WASD directions retain window messages with paced repeats. Outer-ring Shift also establishes system key state only while the game is foreground, releasing on focus loss, stick centering and stop. Correct the INPUT structure size and retry failed sends.",
            "Posted messages alone do not establish system keyboard state. Existing action swaps, custom key mapping and feel curves remain unchanged."),
        new(
            "v3.4.0", "连接光效与输入修复",
            "按物理设备切换手柄图片，接入渐亮并轻微呼吸；首页摘要居中分栏，卡片增加层次。修复设置文字重叠与黑条，补齐数值单位、曲线渐变和当前手感恢复默认。键盘映射按物理按键替代原动作，修复失败重试与释放；游戏动作改键交换涵盖近战。WASD 外圈冲刺修正 Shift 消息并增加回落滞环。",
            "为避免物理手柄绕过改键，转换前完成实体输入隔离，停止后恢复。请先退出旧游戏实例，启动转换后重新打开游戏；准备检查不放在高频输入循环中。所有设置继续在主窗口内编辑。",
            "v3.4.0", "Connection lighting and input repairs",
            "Physical-device illustrations, connection lighting, centered summaries, layered cards, readable settings, value units, curve gradients and scoped feel defaults. Keyboard mappings replace physical inputs with reliable delivery state and release handling. Action swaps include melee; WASD sprint uses corrected Shift messages and release hysteresis.",
            "Prepare physical-input isolation before conversion and restore it on stop. Start conversion before reopening the game so old device handles cannot bypass remapping. Driver preparation remains outside the input loop; settings stay in one window."),
        new(
            "v3.3.0", "精密控制台 · 单窗口设置",
            "全新石墨黑与暖橙界面；固定分类导航，右侧切换设置。曲线、游戏动作、键盘映射、震动、语音、测试与引导均在主窗口内编辑。手感页面支持曲线预览、滑条、精确数值与加减按钮，保留即时生效与自动保存。",
            "按用户确认的设计方向落实真实控件和交互，减少窗口来回切换。键位重复触发、键盘替换输出及 WASD 外圈冲刺问题单独记录，留待下一轮；本版保留现有转换算法。",
            "v3.3.0", "Precision console · one-window settings",
            "A graphite and warm-orange interface with persistent navigation. Curves, game actions, keyboard mappings, feedback, tests and guides open within the main window. Feel controls combine live curve previews, sliders, precise values and steppers while keeping immediate application and automatic saving.",
            "Implement the selected design as real controls and interactions. Duplicate actions, keyboard replacement and WASD outer-ring sprint remain in the functional backlog; conversion algorithms are retained."),
        new(
            "v3.2.0", "设置分区与界面布局优化",
            "首页保留连接状态、当前配置和启动入口。设置按手感、键位、移动、光标交互、震动语音、工具、实验功能分类；游戏动作和键盘映射有独立入口。滑条、数值和说明分行布局，冲刺阈值显示百分比，长按时间显示毫秒。曲线编辑器只保留曲线参数；映射弹窗改为滚动内容与固定底部按钮。",
            "让每个设置有明确的位置，减少重复开关，解决字体与滑条重叠。本次以回退后的转换逻辑为基础；键位重复触发与 WASD 冲刺问题留在后续功能修复中处理。",
            "v3.2.0", "Organized settings and responsive layouts",
            "The home shows connection, configuration and start controls. Settings are grouped into feel, keys, movement, cursor and holds, feedback, tools, and experiments. Game actions and keyboard binds have separate entries. Sliders, values and help occupy separate rows; sprint uses percentages and holds use milliseconds. The curve editor only contains curve controls; mapping dialogs have scrolling content and fixed footer buttons.",
            "Give each setting a clear home, remove duplicate controls, and prevent text/slider collisions. This release keeps the reverted conversion behavior; duplicate input actions and WASD sprint are queued for the next functional fix."),
        new(
            "v3.1.0", "按键映射区（手柄键→键盘任意键）+ 组合注入实验 + 切换武器射击模式",
            "① 新增「按键映射」区（设置 → 手感与键位）：手柄上的每个键（A/B/X/Y/LB/RB/LT/RT/L3/R3/方向键/视图/Option/Home）都能触发键盘上的任意键——字母 / 数字 / F1-F12 / Tab / CapsLock / Shift / Ctrl / Alt / Win / 方向键 / 常用标点（Fn 由键盘固件处理，不支持）。打开窗口点「重设」按下目标键盘键即可；绑定后该手柄键向游戏注入键盘键（PostMessage 直投，同 U/J 已验证通道），原生手柄输出自动暂停——动作跟着键位走、不双触发。示例：LB→Q（战术道具）、RB→E（杀伤道具）、LS→B（武器模式切换），无需再等逐个定制。② 实验性功能新增「R3 组合注入 V+B」：按下 R3 同时注入 V+B 两个键（组合注入示例，V/B 功能不冲突），默认关闭。③ 长按触发新增「切换武器射击模式」：触发后向游戏注入一次 B（键盘默认键），触发方式可选单键长按（默认 Option）或双键组合（需录制，不占用切光标 L3+R3 / 核弹 LB+RB 默认组合），长按时长独立可调。",
            "社区反馈越来越多是「想要某个手柄键映射某个键盘键」（LB→Q、RB→E、LS→B…），逐个定制永远追不完；把整张手柄键表 × 整张键盘键表做成通用映射区，复用已经验证可靠的 U/J 直投通道，一次解决全部同类需求，且绑定后原生输出自动暂停保证不双触发。切换武器射击模式（键盘 B）此前没有入口，作为游戏事件补上，并给单键/双键两种触发方式让不同习惯的人都能用。",
            "v3.1.0", "Key-map area (pad → any keyboard key) + combo-inject experiment + swap fire mode",
            "① New \"Key Map\" section (Settings → Keys & Feel): every pad button (A/B/X/Y/LB/RB/LT/RT/L3/R3/DPad/View/Option/Home) can trigger any keyboard key — letters / digits / F1-F12 / Tab / CapsLock / Shift / Ctrl / Alt / Win / arrows / common punctuation (Fn is firmware-level, not mappable). Open the window, tap Set, press the target keyboard key. Once bound, that pad button injects the keyboard key to the game (PostMessage direct-inject, the same proven U/J channel) and its native pad output pauses — actions follow the bind, no double trigger. Examples: LB→Q (tactical), RB→E (lethal), LS→B (weapon-mode toggle) — no more waiting for per-user customization. ② Experimental: \"R3 combo injects V+B\" — pressing R3 injects both V and B (combo-injection example; V and B don't conflict), off by default. ③ New hold action \"Swap weapon fire mode\": fires one keyboard B to the game; trigger can be a single held key (default Option) or a two-key combo you record (avoids the cursor-swap L3+R3 / nuke LB+RB defaults), with its own adjustable hold duration.",
            "The community keeps asking for specific pad-key → keyboard-key binds (LB→Q, RB→E, LS→B…) — customizing per user can never keep up. A generic key-map area spanning the full pad-key set × full keyboard-key set, reusing the already-proven U/J direct-inject channel, solves the whole class at once; native output pauses while bound so there is no double trigger. Swap-fire-mode (keyboard B) had no entry before, so it's added as a game event with both single-key and two-key trigger styles for different habits."),
        new(
            "v3.0.1", "近战/标点直接上设置页 + 修复重复入口",
            "① 设置页「手感与键位」直接显示「近战/标点：绑定 R3 [重设]」——不用再进弹窗找；点「重设」自动打开键位修正窗口并直接进入录键，按下新键即完成绑定。② 修复设置页出现两个重复的「键位修正」入口（历史迭代遗留、功能完全相同），删除旧入口只保留一个。③ 近战/标点改为标准键位行：默认绑定 R3（DS 系 R3 原生就是近战+标点），重绑后由新键输出 R3 位、原 R3 位释放可改作他用（如下蹲），不再双触发；移除旧版 V+G 键鼠注入方案。",
            "用户反复在设置页找不到近战/标点——它藏在键位修正弹窗里；设置页还有两个功能相同的「键位修正」入口，容易点错、容易误以为没更新。把入口收敛成一个、近战/标点直接摆到设置页并一键进入录键，是最直接的信任修复。注入 V+G 与 DS 原生 R3 语义不符，改为标准键位行更贴合游戏原生手柄模式。",
            "v3.0.1", "Melee/Ping now on the settings page + duplicate entry fixed",
            "① Settings → Keys & Feel now shows \"Melee/Ping: bound R3 [Set]\" directly — no more hunting inside the popup; tapping Set opens Key Remap straight into recording, press the new pad button and it's bound. ② Removed the duplicated \"Key Remap\" entry (leftover from earlier iterations, functionally identical) — only one entry remains. ③ Melee/Ping is now a standard rebindable row: bound to R3 by default (DS R3 natively IS melee+ping), after rebinding the new key emits the R3 bit and R3 is freed for other uses (e.g. crouch), with no double trigger; the old V+G keyboard injection is removed.",
            "Users kept failing to find Melee/Ping on the settings page — it lived inside the Key Remap popup, and the page had two identical \"Key Remap\" entries that made it easy to click the wrong one and assume nothing changed. Consolidating to a single entry and putting Melee/Ping directly on the page with one-tap recording is the most direct trust fix. V+G injection didn't match DS-native R3 semantics; a standard rebindable row matches the game's native gamepad mode."),
        new(
            "v3.0.0", "DS 系手柄完整适配（大版本更新）",
            "① DS 系列主键位修正：实测 DualSense Edge 确认标准布局 □=bit4 ×=bit5 ○=bit6 △=bit7，方块/三角/圆圈/叉全部与实机一一对应。② 状态栏直接显示具体型号（DualShock 4 / DualSense / DualSense Edge / Sony xxx），DS 与 Xbox 混插一眼可辨。③ 键位修正新增「近战/标点」行（与跳跃/下蹲同规格）：默认绑定 R3——DS 系 R3 原生就是近战+标点同一键；可点「重设」换到其它键，换键后由新键输出 R3 位（游戏原生近战/标点），R3 被释放可改作他用（如下蹲），不再双触发。",
            "DSE 实机逐键日志证明主键位 bit 顺序与旧注释相反——第三方手柄和标准 DS 协议不能混用，必须以实机为准；「R3 下蹲误触发近战/标点」的根因是 R3 原生位仍在转发——正确做法不是注入 V/G，而是把近战/标点绑定位做成可重绑的标准键位行：默认 R3 原生不变，重绑后由新键输出 R3 位、原 R3 位释放，最贴合 DS 原生手感。",
            "v3.0.0", "Full DS-series support (major release)",
            "① DS-series face-key layout fixed: real-device testing of DualSense Edge confirmed standard layout □=bit4 ×=bit5 ○=bit6 △=bit7, all face buttons now 1:1 with hardware. ② Status bar shows the exact model (DualShock 4 / DualSense / DualSense Edge / Sony xxx), DS vs Xbox obvious at a glance. ③ Key remap gained a \"Melee/Ping\" row (same spec as Jump/Crouch): bound to R3 by default — DS R3 natively IS melee+ping in one button; tap Set to rebind to another key, which then outputs the R3 bit (native melee/ping), freeing R3 for other uses (e.g. crouch) with no double trigger.",
            "DSE key-by-key logs proved the face-button bits were opposite to the old comment — third-party pads and standard DS protocols must never be mixed, hardware measurements rule; \"R3-crouch fires melee/ping\" was R3's native bit still forwarding — the correct fix is not injecting V/G but making Melee/Ping a standard rebindable row: R3 stays native by default, after rebinding the new key emits the R3 bit and R3 itself is released."),
        new(
            "v2.2.24", "测试记录窗口 + 通道独占 + 方向诊断",
            "左下角新增「测试记录」入口：自动记录手柄测试日志（输入源切换、WASD 识别方向、数据角度、摇杆幅度与原始数值、Shift 冲刺状态、触摸板按下/松开、按键字节变化），一键复制即可反馈问题，无需手打；多手柄通道独占——DS 系优先，DS 无数据时自动落到 Xbox / XInput，不再互相饿死（修复「DS4 插着时 Xbox 不响应」）；状态栏显示当前输入源。",
            "排查 DS4 奔跑问题时要逐项确认轴映射与幅度，人工打字反馈又慢又容易漏；把诊断数据自动记录成日志是最直接的信任工具。多通道轮询原先在 Sony 无数据时直接 return，把 Xbox 通道饿死——改成有数据优先、无数据顺延。",
            "v2.2.24", "Test log window + channel fallback + axis diagnostic",
            "Added \"Test Log\" at bottom-left: auto-captures pad test logs (source switches, WASD direction, data angle, stick magnitude & raw values, Shift state, touchpad presses, key-byte changes) with one-click copy — no more manual reporting; multi-pad channel fallback — DS first, falls through to Xbox/XInput when DS has no data (fixes \"Xbox dead while DS4 is plugged\"); main screen shows the active source.",
            "Debugging DS4 run requires confirming axis mapping and magnitude item by item, and manual text feedback is slow and lossy — auto-logging diagnostic data is the most direct trust tool. The old polling returned early when Sony had no data, starving the Xbox channel — now data wins, otherwise fall through."),
        new(
            "v2.2.23", "快速趴下跟随蹲键",
            "修复：在键位修正里重绑蹲键后，快速趴下的长按触发键会自动跟随新蹲键（不再写死 B）。例如把蹲改到 A，长按 A 即快速趴下；短按仍是原生蹲。",
            "转发层早就跟随重绑的蹲键，只有快速趴的判定写死了 B，导致「改了蹲键但长按还是 B」的怪现象。统一跟随 _map.Crouch，行为一致。",
            "v2.2.23", "Fast Prone follows crouch key",
            "Fix: after rebinding the crouch key in key remap, Fast Prone's hold trigger now follows your new crouch key (no longer hard-coded B). E.g. bind crouch to A, and holding A fast-prones; a short press stays native crouch.",
            "The forwarding layer already followed the rebound crouch key; only the Fast Prone check was hard-coded to B, causing \"I rebind crouch but it still wants B\". Now unified on _map.Crouch."),
        new(
            "v2.2.23", "DS 系列适配 + 触摸板记分表",
            "完整适配 DS 系手柄（DS4 / DS5 / DSE / 第三方伪装 Sony 的手柄）：16 键位实测布局解析、方向键 / 主键 / 肩键 / 扳机全部一一对应；触摸板按下 → 注入 Tab 打开记分表（DS 系列专属，复用视图键 Tab 直投通道）；摇杆满量程自动校准——部分第三方手柄物理行程不满（推满仅 ~87%），归一化补满后推满即可奔跑。",
            "DS 手柄协议不统一：标准 DS 是 64 字节报告，第三方 XInput 映射型是 36 字节精简报告且按键位完全不同，用实机逐键实测锁定布局；触摸板在 DS 上是最顺手的记分表入口；「推满不奔跑」的根因是摇杆物理范围不足，自动校准到满量程后游戏才能识别全速。",
            "v2.2.23", "DS-series support + touchpad scoreboard",
            "Full DS-series support (DS4 / DS5 / DSE / third-party Sony-lookalikes): 16-key layout parsed from real-device measurements, with D-pad / face / shoulder / trigger all 1:1; touchpad press now injects Tab to open the scoreboard (DS-only, reuses the View-hold Tab channel); automatic stick full-range calibration — some third-party pads only travel ~87%, so output is normalized to 100% so full push actually runs.",
            "DS protocols aren't uniform: standard DS is a 64-byte report while third-party XInput-mapped pads use a 36-byte compact layout with completely different key bits, locked in by testing every key on real hardware; the touchpad is the most natural scoreboard key on DS; \"full push doesn't run\" turned out to be a short physical stick travel — auto-calibration restores full range so the game sees full speed."),
        new(
            "v2.2.22", "历史更新日志 + Buy me Coffee",
            "主界面右上角新增「历史更新日志」入口，点开可查看每个版本改了什么、为什么改、背后的思考；主界面右下角新增「Buy me Coffee」赞赏卡，扫码赞赏任意金额即可解锁 Pro（语音 + 震动）。",
            "开发过程本身就是产品的一部分——每一步为什么这样想、怎样排查解决，都是社区最想看的信任资产；赞赏卡放在主界面可见位置，让支持路径不再难找。",
            "v2.2.22", "Changelog + Buy me Coffee",
            "Added a Changelog button next to Settings on the main screen, showing what changed each version, why, and the thinking behind it; added a \"Buy me Coffee\" tip card at the bottom-right of the main screen — scan and tip any amount to unlock Pro (voice + rumble).",
            "The dev process itself is part of the product — why each step was taken and how it was debugged is the trust asset the community wants most; putting the tip card visibly on the main screen lowers the barrier to supporting."),
        new(
            "v2.2.21", "主界面信息升级",
            "主页新增「本版更新」信息卡与「关键设置」信息行（ADS / 输出频率 / 震动 / 移动方案 / 退出清理驱动），窗口放大到 860×600；升级入口文案改为更诚恳的「支持一下 · 解锁语音+震动」。",
            "之前主界面太简，打开看不到核心更新与当前设置；信息透明 + 诚恳文案比硬推销更能建立信任、提高付费意愿。",
            "v2.2.21", "Main screen info upgrade",
            "Added a \"What's new\" card and a key-settings line (ADS / output Hz / rumble / move scheme / clean-drivers) to the main screen; window enlarged to 860×600; upgrade button reworded sincerely.",
            "The main screen was too bare to show updates and current settings; transparency plus a sincere message builds trust better than hard selling."),
        new(
            "v2.2.20", "WASD 推荐 + 记分表加固",
            "WASD 移动升为推荐方案并移到移动控制首位（轻推走路 / 重推奔跑），提示在游戏内把奔跑设为「长按奔跑」；视图键长按开记分表改用 PostMessage 直投 + 按住期间每 80ms 重发按下，默认触发时长降到 200ms。",
            "玩家反馈 H 键切换奔跑不稳定、记分表按不出来；排查发现游戏会过滤 SendInput，而 U/J/H 走 PostMessage 直投能成功，于是统一直投通道并持续重发，兼容不同引擎的记分板检测。",
            "v2.2.20", "WASD recommended + scoreboard fix",
            "WASD move promoted to recommended (light push walk / full push run), with a tip to set run to \"hold\" in-game; View-hold Tab now uses the same PostMessage channel as U/J/H with the press re-sent every 80ms while held, default trigger lowered to 200ms.",
            "Players reported unreliable H-toggle run and unresponsive scoreboard; the game filters SendInput but accepts PostMessage — so all hotkeys now share the direct-injection channel with continuous re-send."),
        new(
            "v2.2.19", "键位 / 光标 / 方向 / 分区四修",
            "视图键长按映射 Tab（打开记分表）+ 独立时长滑块；切出光标后自动移开左上角（重生避让）；修复实验 WASD 方向全反并转正进「手感与键位 → 移动控制」；设置界面新增分区大标题。",
            "WASD 方向反的根因是扇区起算点错误导致整表 180° 反转，重写 0~7 方向表根治；重生后光标在屏幕中央会挡住视野，移到角落；设置项多了没有分区不好找，加大标题引导。",
            "v2.2.19", "Keys / cursor / directions / sections",
            "View-hold now maps to Tab (scoreboard) with its own hold-time slider; the cursor auto-moves to the corner after spawning; fixed the fully-reversed WASD directions and promoted it into Feel & Keys → Movement; added section titles in Settings.",
            "The reversed directions came from a wrong sector origin flipping the whole table 180°, fixed by rewriting the 0–7 direction table; a centered cursor blocks view after respawn, so it moves to the corner; section titles make Settings scannable."),
        new(
            "v2.2.18", "视图键长按 → Tab",
            "长按视图键（View）注入 Tab 打开记分表，短按保持原生动作；长按时长可调。",
            "手游记分表在键盘布局上没有顺手入口，用长按复用视图键，不额外占用按键。",
            "v2.2.18", "View-hold → Tab",
            "Holding View injects Tab to open the scoreboard; a short press keeps the native action; hold duration adjustable.",
            "The scoreboard has no convenient keyboard entry — reusing View with a long press adds the feature without wasting a key."),
        new(
            "v2.2.17", "实验移动 WASD / 触屏",
            "左摇杆可映射 WASD 八方向移动（外圈 Shift 冲刺），或模拟触屏滑动（模拟器场景）；默认关闭，保留在实验区。",
            "给模拟器玩家多一种移动方案；触屏注入属于输入替代，可能触碰反作弊敏感区，先实验验证、不入宣传。",
            "v2.2.17", "Experimental WASD / touch move",
            "Left stick can map to 8-way WASD (full push = Shift sprint) or simulate touch swipes (emulator); off by default, kept in Labs.",
            "Gives emulator players another movement option; touch injection substitutes input and may touch anti-cheat sensitivity, so it stays experimental and out of marketing."),
        new(
            "v2.2.16", "HID 查看器修复版",
            "修复 HID 报告查看器的稳定性问题（打开 / 实时刷新不再卡顿掉线）。",
            "排查工具自己不稳定就没法当依据，先把它做可靠。",
            "v2.2.16", "HID viewer fix",
            "Stability fixes for the HID report viewer (open & live refresh no longer stutter or drop).",
            "A diagnostic tool that crashes is useless as evidence — make it reliable first."),
        new(
            "v2.2.15", "HID 报告查看器",
            "新增 HID 报告查看器：直接读实体手柄原始 HID 报告，帮助定位键位错乱、扳机带转向等协议层问题。",
            "很多“按键错乱 / 转向”肉眼看不到协议层数据，给玩家一个自查工具，减少来回沟通。",
            "v2.2.15", "HID report viewer",
            "Added an HID report viewer that reads the pad's raw HID reports to diagnose key mix-ups and trigger-steering at the protocol layer.",
            "Most \"wrong key / steering\" issues are invisible without protocol-level data — a self-check tool cuts down back-and-forth debugging."),
        new(
            "v2.2.14", "Gyro 体感（实验）",
            "实验性陀螺仪体感功能：国产手柄大多支持陀螺仪。",
            "验证体感方向是否可行；受设备权限 / 占用限制，先保留实验状态。",
            "v2.2.14", "Gyro (experimental)",
            "Experimental gyro aim support — most Chinese pads ship with a gyro.",
            "Validating whether gyro aiming is viable; limited by device permissions/occupancy, so it stays experimental."),
        new(
            "v2.2.9~v2.2.13", "界面打磨期",
            "全局行距放大 1.5 倍、按钮背景增强、LT/RT 转向按钮文案与背景、底部留白、小字下移等。",
            "反复收到「字重叠看不清、按钮不像按钮」的反馈，用全局行距放大与统一按钮样式一次性根治，而不是逐条打补丁。",
            "v2.2.9~v2.2.13", "UI polish period",
            "Global 1.5× line spacing, stronger button backgrounds, LT/RT steering button copy, bottom breathing space, smaller text nudged down.",
            "Repeated \"text overlaps / buttons don't look clickable\" feedback is fixed at the root with global spacing and unified button styles instead of patch-by-patch."),
        new(
            "v2.2.7", "键位修正全面开放 + 转向排查",
            "键位修正触发键扩充（L3 / R3 / 方向键 / 视图 / Option）；趴下 / 核弹 / 交互长按时长各自独立；主界面新增「LT/RT 带转向」五步排查 SOP。",
            "不同手柄（尤其索尼系）按键叫法不同、组合键录制需求真实存在；扳机带转向是高频问题，给玩家标准化排查路径。",
            "v2.2.7", "Full key remap + steer SOP",
            "Remap triggers expanded (L3 / R3 / D-pad / View / Option); prone / nuke / interact hold times each independent; 5-step LT/RT steering SOP added to the main screen.",
            "Different pads (Sony especially) name buttons differently and combo recording is a real need; trigger-steering is a common issue, so players get a standard troubleshooting path."),
        new(
            "v2.2.1~v2.2.6", "核心能力期（里程碑）",
            "实体手柄伪装为虚拟 DS4 供正作识别；适配 Xbox360 / Xbox One / Series S·X / DS 系列输入协议；灵敏度 1~20 档全调节开放；Setup 一键安装 + 卸载程序；「退出时清理驱动」反作弊安全方案（默认关、绝不自动重启）。",
            "两大痛点：正作检测映射软件带来的封号风险、多协议手柄兼容混乱。红线是不做反作弊规避，改用「退出清理 + 静默重装 + 配置保留在 AppData」的透明方案，玩家来回装删配置 100% 保留。",
            "v2.2.1~v2.2.6", "Core capability milestones",
            "Physical pad disguised as a virtual DS4 for the game; Xbox360 / Xbox One / Series S·X / DS input protocols; all 1–20 sensitivity tiers open in Free; Setup one-click installer + uninstaller; \"clean drivers on exit\" anti-cheat-safe scheme (off by default, never auto-restarts).",
            "Two big pains: ban risk from the game detecting mapping software, and messy multi-protocol pad support. The red line is no anti-cheat evasion — a transparent \"clean-on-exit + silent reinstall + config kept in AppData\" scheme keeps player config 100% across reinstalls."),
        new(
            "正式版之前", "工程化与商业定案期",
            "完成多协议手柄适配（Xbox360 / Xbox One / Series S·X / DS）、Setup 一键安装 + 卸载程序、退出清理驱动反作弊安全方案；商业上把全部调节能力开放给免费版，付费仅多「语音 + 震动」（赞赏码信任制，最低 19.9）。",
            "社区用爱发电：收费只是为了支持持续开发，不是功能垄断；反作弊红线是不做规避，用透明方案保证玩家安全。",
            "Pre-release", "Engineering & business decision",
            "Multi-protocol pad support (Xbox360 / One / Series / DS), one-click Setup + uninstaller, clean-drivers anti-cheat-safe scheme; all tuning opened to Free, paid adds only \"voice + rumble\" (tip-code trust model, from ¥19.9).",
            "The community runs on love: payment exists to support development, not to gate features; the anti-cheat red line is no evasion, with a transparent scheme keeping players safe."),
        new(
            "正式版之前", "功能扩展期",
            "震动功能（游戏震动转发 + 枪声音频检测）、语音包（TTS / 真人语音 / 随机播报 / 检视·核弹播报）、键位修正全面开放（LB / RB / LT / RT / L3 / R3 / 方向键 / 视图 / Option）、一键交换肩键扳机、快速趴 / 快速交互 / 核弹 / 检视、左摇杆移动方案（WASD / 触屏 / 奔跑）、输出频率 60~1000Hz。",
            "手感之外，玩家最想要的是「手柄该有的体验」——震动、语音、顺手的键位，一步步补齐。",
            "Pre-release", "Feature expansion",
            "Rumble (game rumble forwarding + gunshot audio detection), voice pack (TTS / real voice / shuffle / inspect & nuke callouts), full remap triggers (LB / RB / LT / RT / L3 / R3 / D-pad / View / Option), one-tap shoulder-trigger swap, fast prone / interact / nuke / inspect, left-stick move schemes (WASD / touch / run), 60–1000Hz output.",
            "Beyond feel, players want \"everything a pad should do\" — rumble, voice, comfortable keys — filled in step by step."),
        new(
            "正式版之前", "手感引擎期",
            "总曲线母版架构（一条母版曲线作为「总指挥」，按档位等比例传播到所有封装）、12 套封装（COD20/21/22/23 × 动态/线性/标准，后精简为 COD 动态/线性/标准）、灵敏度 1~20 档、微调界面、反死区与曲线编辑。",
            "一套曲线不可能适配所有档位与封装：母版改一个点，其他档位按比例、按死区约束跟随变化，保持单调递增与曲率风格——这是手感的灵魂。",
            "Pre-release", "Feel engine",
            "Master-curve architecture (one master curve as the \"conductor\", propagated proportionally to every preset), 12 presets (COD20/21/22/23 × dynamic/linear/standard, later trimmed to COD dynamic/linear/standard), 1–20 sensitivity tiers, fine-tune UI, anti-deadzone & curve editing.",
            "One curve can't fit every tier and preset: the master moves one point and every tier follows proportionally within deadzone constraints, keeping monotonicity and curvature — the soul of the feel."),
        new(
            "正式版之前", "早期原型期",
            "最初只是「让手柄能被 CODM 正作识别」的原型：实体手柄伪装为虚拟 DS4、灵敏度快捷档位（5~10 数字档）、免费 / 高级 / Pro 三版本商业框架雏形。",
            "先解决「能不能玩」再谈「好不好用」；档位用数字是为了让用户一眼看懂大概手感。",
            "Pre-release", "Early prototype",
            "It started as a prototype just to make the pad recognized by CODM: physical pad disguised as a virtual DS4, quick sensitivity tiers (5–10 numbers), and the seeds of the Free / Advanced / Pro framework.",
            "Solve \"can it play\" before \"is it comfy\"; tiers use plain numbers so players instantly grasp the feel."),
    };

    public ChangelogForm()
    {
        Dpi.Scale = Math.Max(1f, DeviceDpi / 96f);
        Text = Lang.T("历史更新日志", "Changelog");
        ClientSize = new Size(Dpi.Px(620), Dpi.Px(680));
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = Assets.Ui(9.5F);
        Assets.Skin(this);
        // 日志窗口采用深色背景（与主界面一致）：浅字 + 深底才能保证对比度可读。
        // 注意不能沿用 Skin 默认的奶油浅底——那会让下面的亮色文字全部看不清。
        BackColor = Color.FromArgb(0x0E, 0x14, 0x1F);

        BuildLayout();
    }

    private void BuildLayout()
    {
        int m = Dpi.Px(24), w = ClientSize.Width - m * 2;

        // 顶部标题 + 说明
        var title = new Label
        {
            Text = Lang.T("历史更新日志 · 每一步为什么这样做", "Changelog · why every step was made"),
            Font = Assets.Ui(15F, FontStyle.Bold), ForeColor = Color.FromArgb(0xF4, 0xF8, 0xFF),
            BackColor = Color.Transparent,
            Location = new Point(m, Dpi.Px(16)), Size = new Size(w, Dpi.Px(28)),
        };
        Controls.Add(title);

        var hint = new Label
        {
            Text = Lang.T("按时间倒序：本版在最上面。记录每一次更新的内容、原因与思考。",
                          "Newest first: what changed, why, and the thinking behind it."),
            Font = Assets.Ui(9F), ForeColor = Color.FromArgb(0xD6, 0xE2, 0xF4),
            BackColor = Color.Transparent,
            Location = new Point(m, Dpi.Px(48)), Size = new Size(w, Dpi.Px(20)),
        };
        Controls.Add(hint);

        // 滚动内容区
        var scroll = new Panel
        {
            Location = new Point(m, Dpi.Px(74)),
            Size = new Size(w, ClientSize.Height - Dpi.Px(74) - Dpi.Px(24)),
            AutoScroll = true,
            BackColor = Color.Transparent,
        };
        Controls.Add(scroll);

        int y = Dpi.Px(8);
        foreach (var e in Entries)
        {
            bool zh = !Lang.En;
            string ver = zh ? e.Ver : e.EnVer;
            string t = zh ? e.Title : e.EnTitle;
            string body = zh ? e.Body : e.EnBody;
            string why = zh ? e.Why : e.EnWhy;

            // 版本 + 标题行（橙色加粗，深底上高亮）
            var head = new Label
            {
                Text = $"{ver} · {t}",
                Font = Assets.Ui(11F, FontStyle.Bold), ForeColor = Color.FromArgb(0xFD, 0xA0, 0x50),
                BackColor = Color.Transparent,
                Location = new Point(Dpi.Px(8), y), Size = new Size(scroll.ClientSize.Width - Dpi.Px(16), Dpi.Px(22)),
                AutoEllipsis = true,
            };
            scroll.Controls.Add(head);
            y += Dpi.Px(26);

            // 内容行（近白，深底高对比，自动换行）
            var bodyLbl = new Label
            {
                Text = body,
                Font = Assets.Ui(10F), ForeColor = Color.FromArgb(0xF4, 0xF8, 0xFF),
                BackColor = Color.Transparent,
                Location = new Point(Dpi.Px(8), y), Size = new Size(scroll.ClientSize.Width - Dpi.Px(16), 0),
                AutoSize = true, MaximumSize = new Size(scroll.ClientSize.Width - Dpi.Px(16), 0),
            };
            scroll.Controls.Add(bodyLbl);
            y += bodyLbl.Height + Dpi.Px(4);

            // 为什么行（亮蓝白，深底高对比，自动换行）
            var whyLbl = new Label
            {
                Text = Lang.T("为什么 · ", "Why · ") + why,
                Font = Assets.Ui(9.5F), ForeColor = Color.FromArgb(0xD6, 0xE2, 0xF4),
                BackColor = Color.Transparent,
                Location = new Point(Dpi.Px(8), y), Size = new Size(scroll.ClientSize.Width - Dpi.Px(16), 0),
                AutoSize = true, MaximumSize = new Size(scroll.ClientSize.Width - Dpi.Px(16), 0),
            };
            scroll.Controls.Add(whyLbl);
            y += whyLbl.Height + Dpi.Px(6);

            // 条目间分隔线（弱化色块感）
            var sep = new Label
            {
                Text = "────────────────────────────────",
                Font = Assets.Ui(7.5F), ForeColor = Color.FromArgb(0x5E, 0x6E, 0x8A),
                BackColor = Color.Transparent,
                Location = new Point(Dpi.Px(8), y), Size = new Size(scroll.ClientSize.Width - Dpi.Px(16), Dpi.Px(14)),
            };
            scroll.Controls.Add(sep);
            y += Dpi.Px(16);
        }
    }
}
