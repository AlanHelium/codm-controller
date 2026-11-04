param()
$ErrorActionPreference = 'Stop'
$taskFlag='false'
$taskFlavor='owner'
$taskSuffix=''
$taskRoot = $PSScriptRoot
Push-Location -LiteralPath $taskRoot
try {
    $taskReadme='../必看 README.TXT'
    Copy-Item -LiteralPath (Join-Path $taskRoot $taskReadme) -Destination 'payload/必看 README.TXT' -Force
    Copy-Item -LiteralPath (Join-Path $taskRoot '../赞赏码.png') -Destination 'payload/赞赏码.png' -Force
    @('@echo off','start "" "%~dp0CODMControllerProbeUninstaller.exe" --repair') | Set-Content -LiteralPath 'payload/修复连接.cmd' -Encoding ASCII
    dotnet publish '源码\uninstall\Uninstaller.csproj' -c Release -r win-x64 --self-contained true  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ("payload/uninstaller-build/"+$taskFlavor)
    if ($LASTEXITCODE -ne 0) { throw '卸载器编译失败' }
    Copy-Item -LiteralPath ("payload/uninstaller-build/"+$taskFlavor+'/CODMControllerProbeUninstaller.exe') -Destination 'payload\CODMControllerProbeUninstaller.exe' -Force
    $taskItems = @(@('CODMControllerProbeUninstaller.exe','payload.uninstall'),@('CODMControllerProbe.exe','payload.main'),@('必看 README.TXT','payload.readme'),@('赞赏码.png','payload.donation'),@('修复连接.cmd','payload.repairlink'),@('components/ViGEmBus_Setup.exe','payload.vigem'),@('components/HidHide_1.5.230_x64.exe','payload.hidhide'))
    $taskManifest = foreach ($taskItem in $taskItems) {
        $taskFile = Get-Item -LiteralPath (Join-Path 'payload' $taskItem[0])
        [pscustomobject]@{Name=$taskItem[0];Resource=$taskItem[1];Hash=(Get-FileHash -LiteralPath $taskFile.FullName -Algorithm SHA256).Hash;Bytes=$taskFile.Length}
    }
    $taskManifest | ConvertTo-Json | Set-Content -LiteralPath 'payload\manifest.json' -Encoding UTF8
    dotnet publish '源码\setup\Setup.csproj' -c Release -r win-x64 --self-contained true  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ("dist/setup-build/"+$taskFlavor)
    if ($LASTEXITCODE -ne 0) { throw '安装器编译失败' }
    $taskRelease = Join-Path $taskRoot ('待发布/'+$taskFlavor)
    New-Item -ItemType Directory -Path $taskRelease -Force | Out-Null
    Copy-Item -LiteralPath ("dist/setup-build/"+$taskFlavor+'/CODMControllerProbeSetup.exe') -Destination (Join-Path $taskRelease ("CODM手柄转换器-Setup-v3.5.15"+$taskSuffix+'.exe')) -Force
    Copy-Item -LiteralPath 'payload\必看 README.TXT' -Destination $taskRelease -Force
    Copy-Item -LiteralPath 'payload\赞赏码.png' -Destination $taskRelease -Force
    $taskPublicFiles = @(("CODM手柄转换器-Setup-v3.5.15"+$taskSuffix+'.exe'),'必看 README.TXT','赞赏码.png')
    $taskPublicFiles | ForEach-Object { $taskHash=Get-FileHash -LiteralPath (Join-Path $taskRelease $_) -Algorithm SHA256; $taskHash.Hash+'  '+$_ } | Set-Content -LiteralPath (Join-Path $taskRelease 'SHA256.txt') -Encoding UTF8
} finally { Pop-Location }
