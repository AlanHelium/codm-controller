$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
Push-Location -LiteralPath $taskRoot
try {
    dotnet publish '源码\uninstall\Uninstaller.csproj' -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o 'payload\uninstaller-build'
    if ($LASTEXITCODE -ne 0) { throw '卸载器编译失败' }
    Copy-Item -LiteralPath 'payload\uninstaller-build\CODMControllerProbeUninstaller.exe' -Destination 'payload\CODMControllerProbeUninstaller.exe' -Force
    $taskItems = @(@('CODMControllerProbeUninstaller.exe','payload.uninstall'),@('CODMControllerProbe.exe','payload.main'),@('必看 README.TXT','payload.readme'),@('赞赏码.png','payload.donation'),@('components/ViGEmBus_Setup.exe','payload.vigem'),@('components/HidHide_1.5.230_x64.exe','payload.hidhide'))
    $taskManifest = foreach ($taskItem in $taskItems) {
        $taskFile = Get-Item -LiteralPath (Join-Path 'payload' $taskItem[0])
        [pscustomobject]@{Name=$taskItem[0];Resource=$taskItem[1];Hash=(Get-FileHash -LiteralPath $taskFile.FullName -Algorithm SHA256).Hash;Bytes=$taskFile.Length}
    }
    $taskManifest | ConvertTo-Json | Set-Content -LiteralPath 'payload\manifest.json' -Encoding UTF8
    dotnet publish '源码\setup\Setup.csproj' -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o 'dist\setup-build'
    if ($LASTEXITCODE -ne 0) { throw '安装器编译失败' }
    $taskRelease = Join-Path $taskRoot '待发布'
    New-Item -ItemType Directory -Path $taskRelease -Force | Out-Null
    Copy-Item -LiteralPath 'dist\setup-build\CODMControllerProbeSetup.exe' -Destination (Join-Path $taskRelease 'CODM手柄转换器-Setup-v3.4.6.exe') -Force
    Copy-Item -LiteralPath 'payload\必看 README.TXT' -Destination $taskRelease -Force
    Copy-Item -LiteralPath 'payload\赞赏码.png' -Destination $taskRelease -Force
    $taskPublicFiles = @('CODM手柄转换器-Setup-v3.4.6.exe','必看 README.TXT','赞赏码.png')
    $taskPublicFiles | ForEach-Object { $taskHash=Get-FileHash -LiteralPath (Join-Path $taskRelease $_) -Algorithm SHA256; $taskHash.Hash+'  '+$_ } | Set-Content -LiteralPath (Join-Path $taskRelease 'SHA256.txt') -Encoding UTF8
} finally { Pop-Location }
