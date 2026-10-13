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
