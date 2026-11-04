# 构建普通正式版 v3.5.15

需要 Windows x64、.NET 10 SDK 和 PowerShell。运行 `./build.ps1`。
主程序、维护工具和安装器使用同一版本，产物位于 `artifacts/release/`。
驱动安装器位于 `installer/payload/components/`，构建仅打包，不运行驱动安装。
安装包自带运行时、驱动安装器、卸载器和修复入口；免安装包仍需系统驱动。
测试项目仅在明确需要时手动执行，构建脚本不运行测试。
