$ErrorActionPreference = 'Stop'
Push-Location -LiteralPath $PSScriptRoot
try {
    dotnet publish 'src/Probe.csproj' -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o 'artifacts/app'
    if ($LASTEXITCODE -ne 0) { throw '主程序编译失败' }
    Copy-Item -LiteralPath 'artifacts/app/CODMControllerProbe.exe' -Destination 'installer/payload/CODMControllerProbe.exe' -Force
    & './installer/构建安装包.ps1'
    $taskReleaseRoot = Join-Path $PSScriptRoot 'artifacts/release'
    $taskPortableRoot = Join-Path $PSScriptRoot 'artifacts/portable/CODM手柄转换器-v3.4.2'
    New-Item -ItemType Directory -Path $taskReleaseRoot,(Join-Path $taskPortableRoot 'components') -Force | Out-Null
    $taskManifest = Get-Content -LiteralPath 'installer/payload/manifest.json' -Raw | ConvertFrom-Json
    foreach ($taskEntry in $taskManifest) {
        Copy-Item -LiteralPath (Join-Path 'installer/payload' $taskEntry.Name) -Destination (Join-Path $taskPortableRoot $taskEntry.Name) -Force
    }
    @($taskManifest | Select-Object Name,Hash) | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskPortableRoot 'portable-files.json') -Encoding UTF8
    @('@echo off','start "" "%~dp0CODMControllerProbeUninstaller.exe" --portable --prepare') | Set-Content -LiteralPath (Join-Path $taskPortableRoot '准备组件.cmd') -Encoding ASCII
    @('@echo off','start "" "%~dp0CODMControllerProbeUninstaller.exe" --portable') | Set-Content -LiteralPath (Join-Path $taskPortableRoot '清理卸载.cmd') -Encoding ASCII
    Copy-Item -LiteralPath 'installer/待发布/CODM手柄转换器-Setup-v3.4.2.exe' -Destination (Join-Path $taskReleaseRoot 'CODM手柄转换器-v3.4.2-安装版.exe') -Force
    Copy-Item -LiteralPath '必看 README.TXT','赞赏码.png' -Destination $taskReleaseRoot -Force
    Compress-Archive -LiteralPath $taskPortableRoot -DestinationPath (Join-Path $taskReleaseRoot 'CODM手柄转换器-v3.4.2-免安装版.zip') -CompressionLevel Optimal -Force
    Get-ChildItem -LiteralPath $taskReleaseRoot -File | Where-Object Name -ne 'SHA256.txt' | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName).Hash + '  ' + $_.Name } | Set-Content -LiteralPath (Join-Path $taskReleaseRoot 'SHA256.txt') -Encoding UTF8
} finally { Pop-Location }
