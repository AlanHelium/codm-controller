# 从源码构建

需要 Windows x64、.NET 10 SDK 和 PowerShell；首次还原依赖需要访问 NuGet。终端进入仓库根目录后执行：

```powershell
./build.ps1
```

输出位于 `artifacts/release`：安装版 EXE、免安装 ZIP、必看说明、赞赏码和 SHA256。构建不安装驱动，也不自动运行软件。

主程序使用固定版本 `NAudio 2.2.1`、`Nefarius.ViGEm.Client 1.21.256`。仓库中的官方驱动包固定为 ViGEmBus 1.22.0 和 HidHide 1.5.230：

| 文件 | SHA256 |
| --- | --- |
| ViGEmBus_Setup.exe | 89220A7865076B342892F98865F3499FB7C4CFD673159E89D352C360FD014C6A |
| HidHide_1.5.230_x64.exe | F4BBBCB82E6258641B887C74BC81C4C5F66E4AA811808DFC304347687B7605F6 |

安装器嵌入六项文件及其哈希，安装前先释放到临时目录并核验。免安装包使用同一组文件及 `portable-files.json`，组件准备前核验文件，再记录实际解压位置。

安全逻辑检查：

```powershell
dotnet run --project installer/源码/tests/Checks.csproj -c Release
```

免费功能与界面检查：

```powershell
dotnet run --project src/_uitest/FreeUi.csproj -c Release -- artifacts/ui-check
```

界面检查使用程序自己的预览模式；不会开启输入转换或改写个人配置。驱动真实安装、卸载、重启和游戏测试需要另行进行，不能由编译或界面检查替代。

发布记录：v3.4.1 的 DS5 USB 游戏测试已由使用者确认；v3.4.2 保留这套输入核心，免费功能检查及真实 DS5 隔离/交换/恢复检查通过。安装版升级、中文路径安装与清理、免安装组件准备及保留驱动/配置清理均已实测。共享驱动完整卸载和重启清理的底层实现已在本机实测；其他系统与设备不能据此保证无差异。

v3.4.3 修复记录：曲线编辑页参数加载结束后解除回调屏蔽。反死区、横纵灵敏度重新应用并保存，切换摇杆与重开页面保持正常。54 项回归检查验证滑块鼠标/键盘操作、实时曲线、配置导出及其他手感滑块；未修改个人配置。

```powershell
dotnet run --project src/_uitest/FeelSliders.csproj -c Release -- artifacts/slider-check
```

v3.4.4 首次启动、实时诊断、关闭行为与圆角重绘检查（55 项）：

```powershell
dotnet run --project src/_uitest/FirstRunChecks.csproj -c Release -- artifacts/first-run-check
```

该检查需要本机已经安装且生效的 ViGEmBus 和 HidHide；不会替你安装组件。它会创建短暂的测试进程、一个中立虚拟手柄并读取组件接口，检查完恢复退出，不改写个人配置或隐藏列表。维护后端 44 项和滑块 54 项回归检查通过。本机复用现有驱动升级 v3.4.4 后接口就绪，无需重启；真实 DS5 USB 输入、隔离、映射交换及恢复检查通过。该结果不等同于在全新 Windows 上首次安装驱动的测试。Windows 若明确要求重启，界面会保留真实的待重启状态，允许选择稍后重启，不自动重启。
v3.4.5 快捷方式修复验证：Unicode 快捷方式 81 项（简体、繁体、日文、emoji 路径；STA/MTA；覆盖与失败保留；无临时文件残留），快捷方式警告界面 3 项，维护后端 44 项通过。测试在独立临时目录运行，不创建系统开始菜单入口，不改动共享驱动或个人配置。未在报告问题的用户机器上实测；需要该用户用新安装包确认。

```powershell
dotnet run --project installer/源码/tests/ShortcutChecks.csproj -c Release -- artifacts/shortcut-check.json
dotnet run --project installer/源码/tests/ShortcutUiChecks.csproj -c Release -- artifacts/shortcut-ui-check
```

修复使用 IShellLinkW 与 IPersistFile，写入成功后才覆盖既有快捷方式。系统卸载登记仍为必需步骤；只有开始菜单快捷方式创建视为可恢复的警告，不能掩盖驱动安装或接口检查失败。

v3.4.6 映射一致性检查：

```powershell
dotnet run --project src/_uitest/MappingChecks.csproj -c Release -- artifacts/mapping-check
```

使用模拟的 DS4 输出和键盘路由，不向真实游戏注入按键。覆盖反复肩键 / 扳机交换、动作重绑与释放、冲突确认与取消、共享键盘键、组合接管与单键光标、旧配置迁移、录制时暂停输出，以及标题含 CODM 的群聊 / 浏览器 / 编辑器排除。预览模式禁止准备或恢复真实隔离会话，也不写入真实使用日志；个人配置哈希保持不变。本机能读取 HidHide 时还比较检查前后的启用状态、黑名单和白名单，报告读取不到时不声称已验证驱动配置。真实手柄和游戏效果仍需另行验证。

2026-10-14 第一阶段实测完成：本机国产 Xbox 协议 USB 手柄，实体隔离通过，当前安装程序与候选包主程序哈希一致，复用现有驱动升级无需重启、配置保留。使用者在训练场确认肩键/扳机交换和射击重绑正常、确认冲突后只触发新用途、单键到两个键盘键组合可用、单键切光标正常。947 项回归通过，预览检查前后 HidHide 实际状态和个人配置一致。第二阶段的双手柄键自选键盘目标、Tab 与 F 专项不属于本版已完成范围。

第二阶段 v3.4.7 增加双手柄来源组合（含 LT/RT）、任意支持的键盘目标、立即/长按阈值、来源独占及原生恢复，录制仅使用主输入循环的物理帧缓存。1784 项映射检查覆盖136种双键组合、所有键盘目标、部分组合与完整组合、共享键盘目标释放、按住时修改、取消和替换冲突、配置回读、物理双扳机录制、实际内嵌设置页的确认与取消。54 项滑块、44 项维护及免费界面检查通过，检查前后个人配置保持不变。标准Tab/F映射沿用v3.4.6；使用者已确认其效果符合需求。

2026-10-14 第二阶段实测完成：本机国产 Xbox 协议 USB 手柄，安装程序与候选包主程序哈希一致，配置保留、复用已有驱动升级无需重启，实体隔离通过。使用者在训练场确认 LB＋RB→Tab、0ms 立即触发、冲突取消和确认、来源独占、按住不反复切换、松开无卡键、关闭后恢复肩键以及原 View→Tab、单键键盘组合、单键光标均正常。自动检查不向真实游戏注入按键，实际游戏结果由使用者确认；不将本机升级结果等同于全新 Windows 的首次安装验证。
