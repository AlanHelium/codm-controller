# 构建普通正式版 v3.5.18

需要 Windows x64、.NET 10 SDK 和 PowerShell。运行 `./build.ps1`。
主程序、维护工具和安装器使用同一版本，产物位于 `artifacts/release/`。
驱动安装器位于 `installer/payload/components/`，构建仅打包，不运行驱动安装。
安装包自带运行时、驱动安装器、卸载器和修复入口；免安装包仍需系统驱动。
测试项目仅在明确需要时手动执行，构建脚本不运行测试。

## v3.5.18 检查记录

- `dotnet build src\Probe.csproj -c Release`：成功，12 个既有警告（CS0414 × 9、CS0108、CS8602、WFO0003），0 错误。
- `dotnet run --project _uitest-inputmode\InputModeChecks.csproj -c Release -- artifacts/inputmode-check`：PASS 38 assertions。
  覆盖：五通道枚举与文案、越界值归 0、配置往返与落盘回读、无句柄/自身窗口拒绝发送、设置页实验区五个方式按钮与当前项标签、按钮不超出卡片宽度、点击「方式 3」后注入通道与主界面状态同步刷新并真实写入 settings.json、点回「方式 1」后复原（脚本结束把原配置整体还原，运行前后 hash 一致）。截图 `artifacts/inputmode-check/设置-输入方式.png`。
- 五通道语义：0 窗口消息·主窗口（等同旧 PostGameKey）；1 窗口消息·渲染子窗口（同进程最大可见子窗口，失败回退兄弟顶层窗口、再回主窗口）；2 系统模拟·扫描码（SendInput + KEYEVENTF_SCANCODE）；3 系统模拟·虚拟键（SendInput + wVk）；4 双通道（0 与 2 同时发，可能双触发）。
- 方式 2/3/4 仅在目标进程为前台时发送；切换方式时 `_gameKeys.Release()` 释放已按住键并立即保存；WASD 外圈 Shift 仍走 `SendRealKey`，不受通道切换影响。
- 未做：真实游戏内实机验证（需用户进 CODM 逐个测试）、方式 5 双触发去重。

